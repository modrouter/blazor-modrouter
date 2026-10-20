using System.Globalization;
using ModRouter.Core.Definitions;
using ModRouter.Core.Matching;
using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace ModRouter.Components;

/// <summary>
/// Renders one level of the already-resolved route chain (at its depth) and cascades the next level. Place it in a
/// route component's markup where its child route should appear. It never matches URLs.
/// </summary>
public sealed class ModRouterOutlet : ComponentBase, IDisposable
{
    [CascadingParameter]
    RouteOutletContext? Context { get; set; }

    /// <summary>
    /// Makes this a named outlet: it renders the component that the deepest matched level declares for this name with
    /// <c>RouteMap.Slot</c> (and <see cref="EmptyContent"/> when none does, or when the state is an error), anywhere in the layout. A named
    /// outlet takes no depth of the chain and cascades nothing, so it can sit beside the unnamed outlet that renders the page.
    /// </summary>
    [Parameter]
    public string? Name { get; set; }

    /// <summary>Rendered when the chain has no level at this depth (for example the parent route itself was matched).</summary>
    [Parameter]
    public RenderFragment? EmptyContent { get; set; }

    /// <summary>Rendered, instead of the page, when a guard says this level is not authorized. Overrides the router's.</summary>
    [Parameter]
    public RenderFragment<RouteError>? NotAuthorized { get; set; }

    /// <summary>Rendered, instead of the page, when this level's guard or loader failed. Overrides the router's.</summary>
    [Parameter]
    public RenderFragment<RouteError>? Error { get; set; }

    [Inject]
    RouteComponentBinder Binder { get; set; } = default!;

    [Inject]
    RouteComponentRegistry Registry { get; set; } = default!;

    /// <summary>
    /// The component type, reuse key and supplied parameter names rendered last: Blazor keeps the instance when the next
    /// render puts the same type with the same key at the same place, and such a reused instance needs the parameters the
    /// previous render supplied and this one does not reset, so it keeps no stale route value.
    /// </summary>
    (Type Component, object? Key, string[] Supplied)? _rendered;

    /// <summary>What a named outlet rendered last: the declaring level and its component, so a reused instance gets resets.</summary>
    (Type Component, string[] Supplied)? _slotRendered;

    /// <summary>The pages this outlet keeps alive (<c>RouteMap.KeepAlive</c>): the shown one and the hidden ones, in creation order.</summary>
    readonly List<KeptEntry> _kept = [];

    int _keptIds;
    long _keptClock;

    /// <summary>One page kept alive: what is needed to render it again, hidden, exactly as it was when it was last the current one.</summary>
    sealed class KeptEntry(int id, RouteDefinition route, string parameterKey)
    {
        public int Id { get; } = id;

        public RouteDefinition Route { get; } = route;

        public string ParameterKey { get; } = parameterKey;

        public int Max { get; set; }

        public long LastShown { get; set; }

        public bool Active { get; set; }

        public RouteContext Level { get; set; } = default!;

        public RouteOutletContext Context { get; set; } = default!;

        public ModRouteData Data { get; set; } = ModRouteData.Empty;

        public IReadOnlyList<KeyValuePair<string, object?>> Supplied { get; set; } = [];

        public object? Instance { get; set; }
    }

    /// <summary>Checks that the outlet sits inside an <see cref="ModRouter"/>.</summary>
    /// <exception cref="InvalidOperationException">There is no router above the outlet.</exception>
    protected override void OnParametersSet()
    {
        if (Context is null)
        {
            throw new InvalidOperationException("ModRouterOutlet must be rendered inside an ModRouter (no route outlet context was found).");
        }
    }

    /// <summary>
    /// Renders this outlet's level of the committed chain, or <see cref="EmptyContent"/> when the chain has no level at
    /// this depth, and cascades the next depth to the component it renders.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///   Another outlet is already rendering this depth of the chain: a route component may hold only one.
    /// </exception>
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var context = Context!;

        if (Name is not null)
        {
            RenderSlot(builder, context);

            return;
        }

        // The claim is made here, not in OnParametersSet: a cascading-value change reaches an outlet that is about to be
        // replaced in the same batch (its parent route changed) before the replacement renders, and Blazor disposes it
        // before any later render runs, so claiming at render time only ever sees outlets that are really alive.
        if (!context.TryClaim(this))
        {
            throw new InvalidOperationException($"More than one ModRouterOutlet is rendering depth {context.Depth} of the current route chain. A route component may contain only one outlet.");
        }

        var level = context.Current;
        var keep = level is not null && !context.IsFailedLevel ? level.Route.Metadata.Get<RouteKeepAliveMetadata>() : null;

        if (keep is null && _kept.Count == 0)
        {
            RenderOrdinary(builder, context, level);

            return;
        }

        RenderKept(builder, context, level, keep);
    }

    /// <summary>The level as a plain page: its component, with a shape that Blazor's diff keeps alive across navigations that keep the level.</summary>
    void RenderOrdinary(RenderTreeBuilder builder, RouteOutletContext context, RouteContext? level)
    {
        if (level is null)
        {
            _rendered = null;
            Registry.Clear(context.Depth, this);
            builder.AddContent(0, EmptyContent);

            return;
        }

        // The shape is fixed (cascading value, then the route component) so Blazor's diff keeps unchanged levels alive.
        builder.OpenComponent<CascadingValue<RouteOutletContext>>(0);
        builder.AddComponentParameter(1, nameof(CascadingValue<RouteOutletContext>.Value), context.Next());
        builder.AddComponentParameter(2, nameof(CascadingValue<RouteOutletContext>.ChildContent), (RenderFragment)(inner => RenderLevel(inner, level, context)));
        builder.CloseComponent();
    }

    /// <summary>
    /// An outlet that keeps pages alive: every kept page is rendered in a keyed wrapper (shown with <c>display: contents</c>, hidden and
    /// inert otherwise), so its component instance and the DOM it built survive while another page is current. A level that is not kept,
    /// or an error, is rendered after them as an ordinary page.
    /// </summary>
    void RenderKept(RenderTreeBuilder builder, RouteOutletContext context, RouteContext? level, RouteKeepAliveMetadata? keep)
    {
        _rendered = null;

        if (keep is not null)
        {
            ShowKept(context, level!, keep);
        }
        else
        {
            foreach (var entry in _kept)
            {
                HideKept(entry);
            }
        }

        foreach (var entry in _kept)
        {
            RenderKeptEntry(builder, entry, context);
        }

        if (keep is null)
        {
            builder.OpenRegion(1);
            RenderOrdinary(builder, context, level);
            builder.CloseRegion();
        }
    }

    void RenderKeptEntry(RenderTreeBuilder builder, KeptEntry entry, RouteOutletContext live)
    {
        var context = entry.Active ? live : entry.Context;

        builder.OpenElement(0, "div");
        builder.SetKey(entry.Id);
        builder.AddAttribute(1, "data-mod-keep-alive", entry.Active ? "active" : "inactive");
        builder.AddAttribute(2, "style", entry.Active ? "display:contents" : "display:none");
        builder.AddAttribute(3, "inert", !entry.Active);

        // A hidden page is rendered from what it last saw, so a navigation elsewhere never reaches it.
        builder.OpenComponent<CascadingValue<RouteOutletContext>>(4);
        builder.AddComponentParameter(5, nameof(CascadingValue<RouteOutletContext>.Value), context.Next());
        builder.AddComponentParameter(6, nameof(CascadingValue<RouteOutletContext>.ChildContent), (RenderFragment)(inner =>
        {
            inner.OpenComponent<CascadingValue<ModRouteData>>(0);
            inner.AddComponentParameter(1, nameof(CascadingValue<ModRouteData>.Value), entry.Active ? live.Data : entry.Data);
            inner.AddComponentParameter(2, nameof(CascadingValue<ModRouteData>.ChildContent), (RenderFragment)(content =>
            {
                content.OpenComponent(0, entry.Route.Component);

                foreach (var parameter in entry.Supplied)
                {
                    content.AddComponentParameter(1, parameter.Key, parameter.Value);
                }

                content.AddComponentReferenceCapture(2, instance =>
                {
                    entry.Instance = instance;

                    if (entry.Active)
                    {
                        Registry.Set(entry.Context.Depth, this, instance);
                    }
                });
                content.CloseComponent();
            }));
            inner.CloseComponent();
        }));
        builder.CloseComponent();
        builder.CloseElement();
    }

    /// <summary>Makes the page of <paramref name="level"/> the shown one (creating its entry on first sight), hides the others and trims the hidden ones.</summary>
    void ShowKept(RouteOutletContext context, RouteContext level, RouteKeepAliveMetadata keep)
    {
        var key = KeptKey(level);
        var entry = _kept.Find(e => ReferenceEquals(e.Route, level.Route) && e.ParameterKey == key);

        if (entry is null)
        {
            entry = new KeptEntry(++_keptIds, level.Route, key);
            _kept.Add(entry);
        }

        foreach (var other in _kept.Where(e => !ReferenceEquals(e, entry)))
        {
            HideKept(other);
        }

        entry.Max = keep.Max;
        entry.Level = level;
        entry.Context = context;
        entry.Data = context.Data;
        entry.Supplied = Binder.Bind(level);
        entry.LastShown = ++_keptClock;

        if (!entry.Active)
        {
            entry.Active = true;

            // A page shown for the first time has no instance yet: only a hidden one that comes back is told and registered here.
            if (entry.Instance is { } instance)
            {
                Registry.Set(context.Depth, this, instance);
                _ = NotifyAsync(entry, activated: true);
            }
        }

        Trim();
    }

    void HideKept(KeptEntry entry)
    {
        if (!entry.Active)
        {
            return;
        }

        entry.Active = false;
        Registry.Clear(entry.Context.Depth, this);
        _ = NotifyAsync(entry, activated: false);
    }

    /// <summary>Drops the least recently shown hidden pages of each route beyond its <c>Max</c>; removing one from the tree disposes its component.</summary>
    void Trim()
    {
        foreach (var route in _kept.Where(e => !e.Active).GroupBy(e => e.Route).ToList())
        {
            foreach (var stale in route.OrderByDescending(e => e.LastShown).Skip(route.First().Max))
            {
                _kept.Remove(stale);
            }
        }
    }

    /// <summary>The route values of a level in a stable text form: two visits to the same route with the same values are the same kept page.</summary>
    static string KeptKey(RouteContext level)
        => string.Join('&', level.InheritedParameters.Values
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={Convert.ToString(pair.Value, CultureInfo.InvariantCulture)}"));

    /// <summary>Tells a page that implements <see cref="IKeepAliveRoute"/> it was hidden or shown again; a failure there is an unhandled exception of the outlet, like one in a lifecycle method.</summary>
    async Task NotifyAsync(KeptEntry entry, bool activated)
    {
        if (entry.Instance is not IKeepAliveRoute page)
        {
            return;
        }

        try
        {
            await (activated ? page.OnActivatedAsync() : page.OnDeactivatedAsync());
        }
        catch (Exception exception)
        {
            await DispatchExceptionAsync(exception);
        }
    }

    /// <summary>Gives up this outlet's claim on its depth so that its replacement can take it.</summary>
    public void Dispose()
    {
        if (Name is null && Context is { } context)
        {
            Registry.Clear(context.Depth, this);
            context.Release(this);
        }
    }

    /// <summary>
    /// A named outlet: the slot component of the deepest level that declares this name, inside that level's data. The shape is
    /// fixed (cascading data, then the component) so the instance survives navigations that keep the same slot component.
    /// </summary>
    void RenderSlot(RenderTreeBuilder builder, RouteOutletContext context)
    {
        var chain = context.State.Chain;
        var depth = -1;
        RouteSlotMetadata? slot = null;

        if (chain is not null && context.State.Error is null)
        {
            for (depth = chain.Matches.Count - 1; depth >= 0 && slot is null; depth--)
            {
                slot = chain.Matches[depth].Metadata.GetAll<RouteSlotMetadata>().FirstOrDefault(s => string.Equals(s.Name, Name, StringComparison.OrdinalIgnoreCase));
            }

            depth++;
        }

        if (slot is null)
        {
            _slotRendered = null;
            builder.AddContent(0, EmptyContent);

            return;
        }

        var level = chain!.Matches[depth];

        builder.OpenComponent<CascadingValue<ModRouteData>>(0);
        builder.AddComponentParameter(1, nameof(CascadingValue<ModRouteData>.Value), context.DataAt(depth));
        builder.AddComponentParameter(2, nameof(CascadingValue<ModRouteData>.ChildContent), (RenderFragment)(content =>
        {
            var supplied = Binder.Bind(level, slot.Component);
            IReadOnlyList<KeyValuePair<string, object?>> resets = [];

            if (_slotRendered is { } previous && previous.Component == slot.Component)
            {
                resets = Binder.Resets(slot.Component, supplied, previous.Supplied);
            }

            _slotRendered = (slot.Component, [.. supplied.Select(p => p.Key)]);

            content.OpenComponent(0, slot.Component);

            foreach (var parameter in supplied)
            {
                content.AddComponentParameter(1, parameter.Key, parameter.Value);
            }

            foreach (var reset in resets)
            {
                content.AddComponentParameter(1, reset.Key, reset.Value);
            }

            content.CloseComponent();
        }));
        builder.CloseComponent();
    }

    /// <summary>
    /// A second fixed-shape cascading value carries the level's loaded data. The shape is the same for a page and for
    /// an error, so a level that fails keeps its place in the tree and only swaps what is inside it.
    /// </summary>
    void RenderLevel(RenderTreeBuilder builder, RouteContext level, RouteOutletContext context)
    {
        builder.OpenComponent<CascadingValue<ModRouteData>>(0);
        builder.AddComponentParameter(1, nameof(CascadingValue<ModRouteData>.Value), context.Data);
        builder.AddComponentParameter(2, nameof(CascadingValue<ModRouteData>.ChildContent), (RenderFragment)(content =>
        {
            if (context.IsFailedLevel)
            {
                _rendered = null;
                Registry.Clear(context.Depth, this);
                RouteErrorContent.Render(content, context.State.Error!, NotAuthorized, Error, context.Fragments);
            }
            else
            {
                RenderRoute(content, level, context);
            }
        }));
        builder.CloseComponent();
    }

    void RenderRoute(RenderTreeBuilder builder, RouteContext level, RouteOutletContext outlet)
    {
        var state = outlet.State;
        var component = level.Route.Component;

        // The reuse key is built from the supplied values only; the resets of a reused instance never change it.
        var supplied = Binder.Bind(level);
        var key = ReuseKey(level, state, supplied);
        IReadOnlyList<KeyValuePair<string, object?>> resets = [];

        if (_rendered is { } previous && previous.Component == component && Equals(previous.Key, key))
        {
            // Only what the instance really received can be stale: a parameter no URL supplied keeps its initializer.
            resets = Binder.Resets(component, supplied, previous.Supplied);
        }

        _rendered = (component, key, [.. supplied.Select(p => p.Key)]);

        builder.OpenComponent(0, component);

        foreach (var parameter in supplied)
        {
            builder.AddComponentParameter(1, parameter.Key, parameter.Value);
        }

        foreach (var reset in resets)
        {
            builder.AddComponentParameter(1, reset.Key, reset.Value);
        }

        if (key is not null)
        {
            builder.SetKey(key);
        }

        // The instance on screen, for the leave-guard stage (a page that implements IRouteNavigationLock is asked).
        builder.AddComponentReferenceCapture(2, instance => Registry.Set(outlet.Depth, this, instance));
        builder.CloseComponent();
    }

    static object? ReuseKey(RouteContext level, RouteState state, IReadOnlyList<KeyValuePair<string, object?>> supplied)
        => level.Route.ReusePolicy switch
        {
            RouteComponentReuse.RecreateWhenRouteChanges => level.Route,
            RouteComponentReuse.RecreateWhenParametersChange => new ParameterKey(supplied),
            RouteComponentReuse.NeverReuse => state.NavigationId,
            _ => null,
        };

    sealed class ParameterKey(IReadOnlyList<KeyValuePair<string, object?>> pairs) : IEquatable<ParameterKey>
    {
        readonly IReadOnlyList<KeyValuePair<string, object?>> _pairs = pairs;

        public bool Equals(ParameterKey? other)
            => other is not null
                && _pairs.Count == other._pairs.Count
                && _pairs.Zip(other._pairs).All(t => t.First.Key == t.Second.Key && Equals(t.First.Value, t.Second.Value));

        public override bool Equals(object? obj) => Equals(obj as ParameterKey);

        public override int GetHashCode()
        {
            var hash = new HashCode();

            foreach (var pair in _pairs)
            {
                hash.Add(pair.Key);
                hash.Add(pair.Value);
            }

            return hash.ToHashCode();
        }
    }
}
