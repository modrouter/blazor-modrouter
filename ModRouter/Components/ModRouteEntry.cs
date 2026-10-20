using System.Diagnostics.CodeAnalysis;
using ModRouter.Core.Definitions;
using ModRouter.Core.Loading;
using ModRouter.Core.Matching;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ModRouter.Components;

/// <summary>
/// Declares one route in markup, inside <c>&lt;ModRouter&gt;&lt;Routes&gt;</c>. It renders nothing: it registers its parameters
/// with the router on its first render, and nested entries become child routes. The parameters mirror the fluent map's.
/// <c>&lt;Routes&gt;</c> content must be static: every entry is read once, on the router's first render; an entry rendered later
/// throws, and a parameter that changes afterwards is ignored.
/// </summary>
public sealed class ModRouteEntry : ComponentBase
{
    RouteEntryNode? _node;

    [CascadingParameter]
    RouteEntryCollector? Collector { get; set; }

    [CascadingParameter]
    RouteEntryNode? Parent { get; set; }

    [Inject]
    IServiceProvider Services { get; set; } = default!;

    /// <summary>The template: rooted (<c>/customers</c>) for a top-level entry, relative (<c>{id:int}</c>) under a parent entry. Not for an <see cref="Index"/> entry.</summary>
    [Parameter]
    public string Path { get; set; } = string.Empty;

    /// <summary>The component rendered for this route.</summary>
    [Parameter]
    [EditorRequired]
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type? Component { get; set; }

    /// <summary>The route name (<c>name:</c>).</summary>
    [Parameter]
    public string? Name { get; set; }

    /// <summary>A static title (<c>title:</c>).</summary>
    [Parameter]
    public string? Title { get; set; }

    /// <summary>A title computed from the route parameters, instead of <see cref="Title"/>.</summary>
    [Parameter]
    public Func<RouteParameters, string>? TitleFunc { get; set; }

    /// <summary>Alternative templates that also match (<c>aliases:</c>).</summary>
    [Parameter]
    public IEnumerable<string>? Aliases { get; set; }

    /// <summary>The parent's index route, rendered at the parent's own path (<c>Index(...)</c>).</summary>
    [Parameter]
    public bool Index { get; set; }

    /// <summary>
    /// A pathless group (<c>Group(...)</c>): <see cref="Component"/> is a layout with an outlet that wraps the nested entries
    /// without adding a path segment. Needs nested entries, no <see cref="Path"/>, <see cref="Name"/>, <see cref="Aliases"/> or <see cref="Title"/>.
    /// </summary>
    [Parameter]
    public bool Group { get; set; }

    /// <summary>Keeps this route's page alive when it is left, up to this many hidden instances per outlet (<c>.KeepAlive(max)</c>).</summary>
    [Parameter]
    public int? KeepAlive { get; set; }

    /// <summary>The components that fill named outlets while this route is on screen, by outlet name (<c>.Slot(name, component)</c>).</summary>
    [Parameter]
    public IReadOnlyDictionary<string, Type>? Slots { get; set; }

    /// <summary>A fixed breadcrumb label (<c>.Breadcrumb(label)</c>).</summary>
    [Parameter]
    public string? Breadcrumb { get; set; }

    /// <summary>Leaves the level out of breadcrumb trails (<c>.Breadcrumb(hidden: true)</c>).</summary>
    [Parameter]
    public bool BreadcrumbHidden { get; set; }

    /// <summary>A guard type (<c>.Guard&lt;T&gt;()</c>), validated when the router compiles.</summary>
    [Parameter]
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type? Guard { get; set; }

    /// <summary>How long loaded data is used without loading again (<c>.CacheFor(freshFor)</c>); needs a <see cref="Loader"/>.</summary>
    [Parameter]
    public TimeSpan? CacheFreshFor { get; set; }

    /// <summary>How much longer cached data is shown while it refreshes in the background (<c>.CacheFor(freshFor, staleFor)</c>).</summary>
    [Parameter]
    public TimeSpan? CacheStaleFor { get; set; }

    /// <summary>A leave guard type (<c>.LeaveGuard&lt;T&gt;()</c>), validated when the router compiles.</summary>
    [Parameter]
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type? LeaveGuard { get; set; }

    /// <summary>A loader type (<c>.Loader&lt;T&gt;(policy)</c>), validated when the router compiles.</summary>
    [Parameter]
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type? Loader { get; set; }

    /// <summary>When the loader runs again for a navigation that keeps the level.</summary>
    [Parameter]
    public RouteReloadPolicy ReloadPolicy { get; set; } = RouteReloadPolicy.WhenParametersChange;

    /// <summary>The element to focus after a navigation here (<c>.Focus(selector)</c>).</summary>
    [Parameter]
    public string? Focus { get; set; }

    /// <summary>Switches the screen-reader announcement off for this route (<c>.NoAnnounce()</c>).</summary>
    [Parameter]
    public bool NoAnnounce { get; set; }

    /// <summary>Makes the alias URLs redirect to the canonical one (<c>.CanonicalAlias()</c>).</summary>
    [Parameter]
    public bool CanonicalAlias { get; set; }

    /// <summary>When the route's component instance is recreated (<c>reuse:</c>).</summary>
    [Parameter]
    public RouteComponentReuse Reuse { get; set; } = RouteComponentReuse.ReuseWhenComponentTypeMatches;

    /// <summary>Requires any authenticated user, like <c>[Authorize]</c> on the component (<c>.Authorize()</c>). Implied by <see cref="Roles"/> and <see cref="Policy"/>.</summary>
    [Parameter]
    public bool Authorize { get; set; }

    /// <summary>A comma-separated list of roles, any one of which admits the user (<c>.Authorize(roles: ...)</c>).</summary>
    [Parameter]
    public string? Roles { get; set; }

    /// <summary>The authorization policy the user must satisfy (<c>.Authorize(policy: ...)</c>).</summary>
    [Parameter]
    public string? Policy { get; set; }

    /// <summary>Opens the route to everyone even when its component carries <c>[Authorize]</c> (<c>.AllowAnonymous()</c>). Not with <see cref="Authorize"/>, <see cref="Roles"/> or <see cref="Policy"/>.</summary>
    [Parameter]
    public bool AllowAnonymous { get; set; }

    /// <summary>Nested entries: the child routes of this one.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// Takes a snapshot of the parameters and registers it with the router's collector, under the parent entry when
    /// there is one.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///   The entry is not inside <c>&lt;ModRouter&gt;&lt;Routes&gt;</c>, or it is rendered after the router's first
    ///   render closed the collector.
    /// </exception>
    protected override void OnInitialized()
    {
        if (Collector is null)
        {
            throw new InvalidOperationException("ModRouteEntry must be rendered inside <ModRouter><Routes>...</Routes></ModRouter>.");
        }

        _node = Snapshot();
        Collector.Register(_node, Parent);
    }

    /// <summary>
    /// Does nothing to the route: the table is compiled once from the snapshot taken in <c>OnInitialized</c>. A changed
    /// parameter is only reported, as a debug log event, when logging is enabled.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (_node is { } registered)
        {
            var current = Snapshot();

            // The table is compiled from the snapshot taken at registration; a later change cannot reach it.
            if (registered.SameAs(current) || Services.GetService<ILoggerFactory>()?.CreateLogger<ModRouteEntry>() is not { } created)
            {
                return;
            }

            // Described only when the event is enabled (CA1873 does not see this guard, hence the locals).
            var logger = Diagnostics.RouterLog.Guard(created);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                var before = registered.Describe();
                var after = current.Describe();
                Diagnostics.RouterLog.MarkupEntryParametersChanged(logger, before, after);
            }
        }
    }

    /// <summary>
    /// Renders nothing itself. Nested entries (<see cref="ChildContent"/>) render under this entry so they register as
    /// its children.
    /// </summary>
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (ChildContent is null || _node is null)
        {
            return;
        }

        builder.OpenComponent<CascadingValue<RouteEntryNode>>(0);
        builder.AddComponentParameter(1, nameof(CascadingValue<RouteEntryNode>.Value), _node);
        builder.AddComponentParameter(2, nameof(CascadingValue<RouteEntryNode>.IsFixed), true);
        builder.AddComponentParameter(3, nameof(CascadingValue<RouteEntryNode>.ChildContent), ChildContent);
        builder.CloseComponent();
    }

    RouteEntryNode Snapshot()
        => new()
        {
            Path = Path,
            Component = Component,
            Name = Name,
            Title = Title,
            TitleFunc = TitleFunc,
            Aliases = Aliases?.ToArray() ?? [],
            IsIndex = Index,
            IsGroup = Group,
            KeepAlive = KeepAlive,
            Slots = Slots ?? new Dictionary<string, Type>(),
            Breadcrumb = Breadcrumb,
            BreadcrumbHidden = BreadcrumbHidden,
            Guard = Guard,
            LeaveGuard = LeaveGuard,
            CacheFreshFor = CacheFreshFor,
            CacheStaleFor = CacheStaleFor,
            Loader = Loader,
            ReloadPolicy = ReloadPolicy,
            Focus = Focus,
            NoAnnounce = NoAnnounce,
            CanonicalAlias = CanonicalAlias,
            Reuse = Reuse,
            Authorize = Authorize,
            Roles = Roles,
            Policy = Policy,
            AllowAnonymous = AllowAnonymous,
        };
}
