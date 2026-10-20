using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using ModRouter.Authorization;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Matching;
using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace ModRouter.Components;

/// <summary>
/// The root router. Owns navigation for the application (only one may be mounted), renders the committed route
/// chain through nested <see cref="ModRouterOutlet"/>s inside the layout, and renders <see cref="NotFound"/>
/// (inside the same layout) when no route matches. Replaces the built-in <c>Router</c> in <c>App.razor</c>.
/// </summary>
/// <remarks>
/// With <see cref="Routes"/> markup the first pass renders the entries (which register themselves) and a bare
/// <see cref="Loading"/>, outside the layout: a layout that builds a markup route's URL would otherwise ask for a table
/// that is not final. The first after-render callback compiles the startup definitions plus the markup entries, freezes
/// the <see cref="RouteTableHolder"/> and only then starts the initial navigation. Without markup the startup table is
/// frozen and the navigation started during initialization, as before.
/// </remarks>
public sealed class ModRouter : ComponentBase, IAsyncDisposable
{
    const string ModulePath = "./_content/ModRouter/mod-router.js";

    // The holder signature of a router that declares no markup routes: the startup table is the final table.
    const string StartupSignature = "startup";

    // Visually hidden but still read by screen readers: the live region that announces the new page.
    const string AnnouncerStyle = "position:absolute;width:1px;height:1px;margin:-1px;padding:0;overflow:hidden;clip:rect(0,0,0,0);white-space:nowrap;border:0";

    static readonly ConcurrentDictionary<Type, Type?> LayoutTypes = new();

    IDisposable? _lease;
    IRouteStatusSink? _statusSink;
    PersistentComponentState? _persistentState;
    PersistingComponentStateSubscription _persisting;
    RouteState? _state;
    RouteOutletContext? _root;
    RouteState? _effectsRanFor;
    Task<IJSObjectReference?>? _module;
    bool _disposed;
    string _announcement = string.Empty;
    bool _announcementToggle;
    RouteEntryCollector? _collector;
    bool _started;
    bool _transitionOpen;
    RouteError? _configurationError;
    RouteData? _routeData;
    ILogger? _logger;

    // The outlet warning: when the state on screen first rendered, and the routes already warned about (once each).
    long _stateRenderedAt;
    readonly HashSet<RouteDefinition> _outletWarnings = [];

    /// <summary>A state shown for less than this is not judged by the outlet warning: a parent may still have been loading.</summary>
    static readonly TimeSpan OutletSettle = TimeSpan.FromSeconds(1);

    /// <summary>The layout for every route that does not declare one with <c>@layout</c> or <c>[Layout]</c> on its depth-0 component.</summary>
    [Parameter]
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type? DefaultLayout { get; set; }

    /// <summary>
    /// Route declarations in markup: <see cref="ModRouteEntry"/> (nested for child routes) and <see cref="ModRoutePages"/>.
    /// They are compiled once, with the startup routes of <c>AddModRouter</c>, on the router's first render; the content must
    /// be static (an entry rendered later throws, a changed parameter is ignored). A compile or binding error is rendered
    /// through <see cref="Error"/> as a <see cref="RouteErrorKind.ConfigurationFailed"/> error.
    /// </summary>
    [Parameter]
    public RenderFragment? Routes { get; set; }

    /// <summary>Rendered, inside the default layout, when the URL matches no route (or its parameters cannot bind).</summary>
    [Parameter]
    public RenderFragment? NotFound { get; set; }

    /// <summary>Rendered, inside the default layout, until the first navigation commits.</summary>
    [Parameter]
    public RenderFragment? Loading { get; set; }

    /// <summary>
    /// Rendered at the level whose guard said not-authorized, unless that level's outlet has its own. Receives the
    /// <see cref="RouteError"/> (its <c>Retry()</c> runs the guards again).
    /// </summary>
    [Parameter]
    public RenderFragment<RouteError>? NotAuthorized { get; set; }

    /// <summary>
    /// Rendered at the level whose guard or loader failed (unless that level's outlet has its own), and in place of
    /// the whole page for a redirect loop or a failed navigation. Receives the <see cref="RouteError"/>.
    /// </summary>
    [Parameter]
    public RenderFragment<RouteError>? Error { get; set; }

    /// <summary>
    /// Renders the screen-reader announcement of a new page (the text it receives) instead of the default visually
    /// hidden <c>role="status"</c> region. It must keep a live region (<c>aria-live</c>) around the text.
    /// </summary>
    [Parameter]
    public RenderFragment<string>? AnnounceTemplate { get; set; }

    /// <summary>
    /// The compatibility shape: when set, a matched chain renders through this fragment instead of the router's own
    /// <c>LayoutView</c> and outlet. It receives a <see cref="RouteData"/> for the depth-0 component (its type, its own
    /// route values and its template), cached per committed state so a fixed-shape fragment keeps its instances, and
    /// must place an <see cref="ModRouterOutlet"/> (the compatibility <c>RouteView</c> does) where the chain goes.
    /// <see cref="NotFound"/>, <see cref="Loading"/> and router-level errors then render bare, as under the built-in
    /// <c>Router</c>; <see cref="DefaultLayout"/> is not used. A <see cref="NotFound"/> fragment that navigates while it
    /// renders runs once per committed state while a navigation is in progress (its output stays on screen), not once per
    /// re-render, so such a redirect does not cancel itself.
    /// </summary>
    [Parameter]
    public RenderFragment<RouteData>? Found { get; set; }

    [Inject]
    NavigationCoordinator Coordinator { get; set; } = default!;

    [Inject]
    ModRouterOptions Options { get; set; } = default!;

    [Inject]
    IJSRuntime Js { get; set; } = default!;

    [Inject]
    RouteStateStore Store { get; set; } = default!;

    [Inject]
    IServiceProvider Services { get; set; } = default!;

    [Inject]
    RouteTableHolder Holder { get; set; } = default!;

    [Inject]
    TimeProvider Time { get; set; } = default!;

    /// <summary>The router's logger (guarded: a throwing provider changes nothing); nothing is logged without an <see cref="ILoggerFactory"/>.</summary>
    ILogger Logger => _logger ??= Diagnostics.RouterLog.Guard(Services.GetService<ILoggerFactory>()?.CreateLogger<ModRouter>() ?? (ILogger)NullLogger.Instance);

    /// <summary>
    /// Takes ownership of navigation and subscribes to committed states. Without <see cref="Routes"/> markup it also
    /// freezes the startup route table and starts the initial navigation; with markup it only prepares the entry
    /// collector, and compiling and the first navigation follow after the first render.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///   Another <see cref="ModRouter"/> is already mounted in this application scope.
    /// </exception>
    protected override async Task OnInitializedAsync()
    {
        _lease = Coordinator.Attach();
        Store.Changed += OnStateChanged;
        _statusSink = Services.GetService<IRouteStatusSink>();

        // Loader data that opted in travels from the prerender to the interactive pass (nothing happens without a prerender).
        if (Services.GetService<PersistentComponentState>() is { } persistentState)
        {
            _persistentState = persistentState;
            _persisting = persistentState.RegisterOnPersisting(PersistLoaderDataAsync);
        }

        if (Routes is not null)
        {
            // The entries register while the first pass renders; the compile and the first navigation follow in OnAfterRenderAsync.
            _collector = new RouteEntryCollector();

            return;
        }

        // The startup table is final: freeze it (a remounted router finds it frozen with the same signature and goes on).
        Holder.Freeze(Holder.Table, StartupSignature);
        await StartAsync();
    }

    /// <summary>
    /// On the first render, turns on link interception (without it every <c>&lt;a href&gt;</c> is a full page load)
    /// and, for a router with <see cref="Routes"/> markup, compiles the route table and starts the initial navigation.
    /// After a committed state has rendered, runs its browser effects (title, announcement, focus, scroll) once.
    /// </summary>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Without this, Blazor does not intercept link clicks and every <a href> is a full page load.
        if (firstRender && Services.GetService<INavigationInterception>() is { } interception)
        {
            await interception.EnableNavigationInterceptionAsync();
        }

        if (firstRender && _collector is { } collector && !_started)
        {
            await CompileMarkupAndStartAsync(collector);
        }

        // Once per committed state that has rendered: a state that was superseded before it rendered never gets here.
        if (_state is { } state && !ReferenceEquals(state, _effectsRanFor))
        {
            _effectsRanFor = state;
            _stateRenderedAt = Time.GetTimestamp();
            await RunEffectsAsync(state);

            // After the scroll and the focus: the picture of the new page should already show them.
            if (_transitionOpen)
            {
                await EndTransitionAsync();
            }
        }
    }

    /// <summary>
    /// Closes the collector, compiles the startup definitions with the markup ones, validates the bindings and freezes the
    /// holder, then starts the initial navigation. A compile or binding failure is logged and rendered through
    /// <see cref="Error"/> instead of leaving the user on <see cref="Loading"/> forever.
    /// </summary>
    async Task CompileMarkupAndStartAsync(RouteEntryCollector collector)
    {
        CompiledRouteTable table;
        string signature;

        try
        {
            // The markup and discovered routes that need authorization get the guard here, like the startup ones in AddModRouter.
            var definitions = AuthorizationRouteContributor.Apply(collector.Close(Holder.StartupDefinitions, out signature));
            table = RouteCompiler.Compile(definitions, Options.ConstraintRegistry);

            if (Options.ValidateComponentBindings)
            {
                var errors = Services.GetRequiredService<RouteComponentBinder>().Validate(table);

                if (errors.Count > 0)
                {
                    throw new RouteBindingValidationException(errors);
                }
            }
        }
        catch (Exception exception) when (exception is RouteCompilationException or RouteBindingValidationException)
        {
            Diagnostics.RouterLog.MarkupRoutesCompileFailed(Logger, exception);
            _configurationError = new RouteError(RouteErrorKind.ConfigurationFailed, exception.Message, exception);
            _started = true;

            // The table will never be final. Give navigation up: a link click or NavigateTo would otherwise run the pipeline
            // against the startup table and commit states the error hides. The not-final hint would only mislead now.
            _lease?.Dispose();
            _lease = null;
            Holder.MarkCompilationFailed();
            StateHasChanged();

            return;
        }

        Holder.Freeze(table, signature);
        await StartAsync();
    }

    async Task StartAsync()
    {
        _started = true;

        await Coordinator.NavigateInitialAsync();

        Apply(Store.Current);
    }

    /// <summary>
    /// Renders one fixed shape for loading, found, not-found and failed states, so the layout instance survives moves
    /// between them: <see cref="Loading"/> until the first commit, the route chain through <see cref="ModRouterOutlet"/>
    /// when a route matched, <see cref="NotFound"/> when none did, and <see cref="Error"/> for router-level failures;
    /// with <see cref="Found"/> set, the application's fragment owns the layout. A screen-reader announcer region
    /// follows.
    /// </summary>
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        var layout = _state is null ? DefaultLayout : LayoutFor(_state);
        var root = _root;
        var state = _state;
        var notFound = NotFound;
        var loading = Loading;

        if (_collector is { } collector)
        {
            // The entries render (to nothing) under the collector and register with it; they stay mounted afterwards, so
            // an entry rendered later and a parameter changed later are noticed.
            builder.OpenComponent<CascadingValue<RouteEntryCollector>>(0);
            builder.AddComponentParameter(1, nameof(CascadingValue<RouteEntryCollector>.Value), collector);
            builder.AddComponentParameter(2, nameof(CascadingValue<RouteEntryCollector>.IsFixed), true);
            builder.AddComponentParameter(3, nameof(CascadingValue<RouteEntryCollector>.ChildContent), Routes);
            builder.CloseComponent();

            if (_configurationError is { } configurationError)
            {
                // Bare, like the first pass: a layout could ask for a route of the table that failed to compile.
                builder.OpenRegion(4);
                RouteErrorContent.Render(builder, configurationError, NotAuthorized, Error, null);
                builder.CloseRegion();

                return;
            }

            if (!_started)
            {
                // Before the compile nothing, not even the layout, may ask for a route: bare Loading until the table is final.
                builder.AddContent(5, loading);

                return;
            }
        }

        // One fixed shape for loading, found, not-found and failed, so the layout instance survives moving between them.
        builder.OpenComponent<CascadingValue<RouteOutletContext>>(10);
        builder.AddComponentParameter(11, nameof(CascadingValue<RouteOutletContext>.Value), root);
        builder.AddComponentParameter(12, nameof(CascadingValue<RouteOutletContext>.ChildContent), (RenderFragment)(inner =>
        {
            if (Found is { } found)
            {
                // The compatibility shape: the application's fragment owns the layout (the built-in Router's contract),
                // so everything that is not a matched chain renders bare.
                inner.OpenRegion(0);

                if (state is null)
                {
                    inner.AddContent(0, loading);
                }
                else if (state.Chain is { } chain)
                {
                    inner.AddContent(1, found, RouteDataFor(chain));
                }
                else if (state.Error is { } error && state.Failure is RouteFailure.NavigationFailed or RouteFailure.RedirectLoop)
                {
                    RouteErrorContent.Render(inner, error, NotAuthorized, Error, null);
                }
                else
                {
                    // Through a host so the fragment, which may navigate while it renders, is not run again for this state
                    // by the re-renders a running navigation causes (see ModNotFoundHost).
                    inner.OpenComponent<ModNotFoundHost>(2);
                    inner.AddComponentParameter(3, nameof(ModNotFoundHost.Content), notFound);
                    inner.AddComponentParameter(4, nameof(ModNotFoundHost.State), state);
                    inner.AddComponentParameter(5, nameof(ModNotFoundHost.Navigating), Store.IsNavigating);
                    inner.CloseComponent();
                }

                inner.CloseRegion();
                RenderAnnouncer(inner);

                return;
            }

            inner.OpenComponent<LayoutView>(0);
            inner.AddComponentParameter(1, nameof(LayoutView.Layout), layout);
            inner.AddComponentParameter(2, nameof(LayoutView.ChildContent), (RenderFragment)(body =>
            {
                if (state is null)
                {
                    body.AddContent(2, loading);
                }
                else if (state.Chain is not null)
                {
                    body.OpenComponent<ModRouterOutlet>(0);
                    body.CloseComponent();
                }
                else if (state.Error is { } error && state.Failure is RouteFailure.NavigationFailed or RouteFailure.RedirectLoop)
                {
                    RouteErrorContent.Render(body, error, NotAuthorized, Error, null);
                }
                else
                {
                    body.AddContent(1, notFound);
                }
            }));
            inner.CloseComponent();
            RenderAnnouncer(inner);
        }));
        builder.CloseComponent();
    }

    /// <summary>
    /// The <see cref="RouteData"/> of the committed chain: the depth-0 component, that level's own route values (a child's
    /// values are not the page's) and its template, created once per state so the fragment sees a new value only per navigation.
    /// </summary>
    RouteData RouteDataFor(RouteMatchChain chain)
    {
        if (_routeData is null)
        {
            var level = chain.Matches[0];
            _routeData = new RouteData(level.Route.Component, level.Parameters.Values) { Template = level.Template };
        }

        return _routeData;
    }

    void RenderAnnouncer(RenderTreeBuilder inner)
    {
        if (AnnounceTemplate is { } template)
        {
            inner.AddContent(3, template, _announcement);
        }
        else
        {
            inner.OpenElement(4, "div");
            inner.AddAttribute(5, "role", "status");
            inner.AddAttribute(6, "aria-live", "polite");
            inner.AddAttribute(7, "data-mod-router-announcer", string.Empty);
            inner.AddAttribute(8, "style", AnnouncerStyle);
            inner.AddContent(9, _announcement);
            inner.CloseElement();
        }
    }

    /// <summary>
    /// Releases navigation (another router may then mount), stops listening to committed states and disposes the
    /// JavaScript module of the browser effects. Safe against an import that is still in flight.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        CheckOutlets();
        Store.Changed -= OnStateChanged;
        _persisting.Dispose();
        _lease?.Dispose();

        // A handle whose import is still in flight is disposed by ImportModuleAsync when it arrives.
        if (_module is { IsCompletedSuccessfully: true } loaded && await loaded is { } module)
        {
            await DisposeModuleAsync(module);
        }
    }

    static async Task DisposeModuleAsync(IJSObjectReference module)
    {
        try
        {
            await module.DisposeAsync();
        }
        catch (Exception exception) when (exception is JSDisconnectedException or OperationCanceledException or ObjectDisposedException)
        {
            // The page or circuit is already gone.
        }
    }

    /// <summary>Announce, then title, focus and scroll in one call into the JS module. A missing module only costs these effects, never a navigation.</summary>
    async Task RunEffectsAsync(RouteState state)
    {
        var effects = NavigationEffectsPlanner.Plan(state, Options);

        if (effects.Announcement is { } text)
        {
            // The same text twice in a row is not announced again by every screen reader, so the text alternates by a trailing no-break space.
            _announcement = text + (_announcementToggle ? "\u00A0" : string.Empty);
            _announcementToggle = !_announcementToggle;
            StateHasChanged();
        }

        // Every run, not once: the container may not exist yet at the first run, or a layout switch may have replaced it
        // since. The module resolves the selector each time and tracks an element only once.
        var tracksScroll = Options.Scroll is { Enabled: true, Restore: true };
        var hasBrowserEffects = effects.Title is not null || effects.Focus is not null || effects.Scroll is not null;

        if (!hasBrowserEffects && !tracksScroll)
        {
            return;
        }

        var module = await ModuleAsync();

        // The import (or the call before) may have taken a while: the router may be gone, or a newer state may be on
        // screen by now, and that state's own run is the one that must reach the browser last.
        if (module is null || IsStale(state))
        {
            return;
        }

        try
        {
            if (tracksScroll)
            {
                await module.InvokeVoidAsync("trackScroll", Options.Scroll.Container);

                if (IsStale(state))
                {
                    return;
                }
            }

            if (!hasBrowserEffects)
            {
                return;
            }

            await module.InvokeVoidAsync("afterNavigation", new
            {
                title = effects.Title,
                focus = effects.Focus is { } focus ? new { selector = focus.Selector, timeoutMs = focus.TimeoutMilliseconds } : null,
                scroll = effects.Scroll is { } scroll
                    ? new { mode = scroll.Mode.ToString().ToLowerInvariant(), fragment = scroll.Fragment, container = scroll.Container, timeoutMs = scroll.TimeoutMilliseconds }
                    : null,
            });
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or OperationCanceledException or InvalidOperationException)
        {
            // InvalidOperationException covers ObjectDisposedException: a handle disposed while a call was in flight.
            Diagnostics.RouterLog.EffectsFailed(Logger, exception);
        }
    }

    /// <summary>True once the router is disposed or <paramref name="state"/> is no longer the one on screen.</summary>
    bool IsStale(RouteState state) => _disposed || !ReferenceEquals(state, _state);

    /// <summary>One import for the lifetime of the router, however many runs ask for it while it is in flight.</summary>
    Task<IJSObjectReference?> ModuleAsync() => _module ??= ImportModuleAsync();

    async Task<IJSObjectReference?> ImportModuleAsync()
    {
        IJSObjectReference module;

        try
        {
            module = await Js.InvokeAsync<IJSObjectReference>("import", ModulePath);
        }
        catch (Exception exception)
        {
            // Whatever went wrong (a missing asset, a host without JavaScript, a test double), navigation must go on.
            Diagnostics.RouterLog.JsModuleLoadFailed(Logger, exception);

            return null;
        }

        if (_disposed)
        {
            // The router went away while the import was in flight: the handle must not leak.
            await DisposeModuleAsync(module);

            return null;
        }

        return module;
    }

    Type? LayoutFor(RouteState state)
    {
        if (state.Chain is { } chain)
        {
            var declared = LayoutTypes.GetOrAdd(
                chain.Matches[0].Route.Component,
                static component => component.GetCustomAttribute<LayoutAttribute>(inherit: true)?.LayoutType);

            return declared ?? DefaultLayout;
        }

        return DefaultLayout;
    }

    void Apply(RouteState? state)
    {
        // Idempotent: the initial navigation commits through Store.Changed and is then applied again after the await;
        // one state must map to one context, or every outlet re-claims and re-renders for nothing.
        if (ReferenceEquals(_state, state))
        {
            return;
        }

        CheckOutlets();
        _state = state;
        _routeData = null;

        // Always a context, even without a chain: an outlet in a parent that is being replaced by the not-found content
        // still receives the new cascaded value before it is disposed, and a null value would make it throw.
        _root = state is null ? null : new RouteOutletContext(state, 0, new RouteErrorFragments(NotAuthorized, Error));

        if (state is not null && RouteStatus.For(state) is { } status)
        {
            _statusSink?.Report(status);
        }
    }

    Task PersistLoaderDataAsync()
    {
        if (_state is { } state && _persistentState is { } persistentState)
        {
            LoaderStatePersistence.Persist(state, persistentState);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The outlet warning (event 1410), run when the state on screen leaves it (replaced or the router disposed): every
    /// depth whose parent level rendered its page must have been claimed by an outlet at least once while the state was
    /// shown, or the parent's component has no outlet for the child the chain matched. Not judged: a state without a
    /// chain, one that never rendered, one shown for less than <see cref="OutletSettle"/> (a parent that renders its outlet
    /// after loading), and nothing past a failed level (its error replaces the page). Each route warns once.
    /// </summary>
    void CheckOutlets()
    {
        if (_root is not { } context || _state?.Chain is not { } chain || !ReferenceEquals(_state, _effectsRanFor) || Time.GetElapsedTime(_stateRenderedAt) < OutletSettle)
        {
            return;
        }

        for (var depth = 0; depth < chain.Matches.Count; depth++)
        {
            if (!context.EverClaimed)
            {
                // Depth 0 unclaimed is a different mistake (a Found fragment without an outlet); below it, the parent
                // level rendered its page (its outlet claimed depth - 1) but nothing ever rendered this one.
                if (depth > 0 && chain.Matches[depth - 1] is var parent && _outletWarnings.Add(parent.Route))
                {
                    Diagnostics.RouterLog.OutletNotRendered(Logger, parent.RouteName, parent.Template, parent.Route.Component.FullName ?? parent.Route.Component.Name, depth);
                }

                return;
            }

            // The failed level shows its error instead of the page, so no outlet is expected below it.
            if (context.IsFailedLevel || context.NextIfCreated is not { } next)
            {
                return;
            }

            context = next;
        }
    }

    void OnStateChanged(RouteState state)
    {
        var previous = _state;
        Apply(state);

        if (ShouldTransition(previous, state))
        {
            _ = RenderInTransitionAsync();

            return;
        }

        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// A page change is animated when the option is on, the JS module is already loaded (the first navigation never waits for the
    /// import) and the path changes between two pages: not a first load, a query or fragment change, or an error page.
    /// </summary>
    bool ShouldTransition(RouteState? previous, RouteState state)
        => Options.ViewTransitions.Enabled
            && _module is { IsCompletedSuccessfully: true }
            && previous is { Chain: not null, Error: null }
            && state is { Chain: not null, Error: null }
            && !string.Equals(previous.Path, state.Path, StringComparison.Ordinal);

    /// <summary>
    /// Asks the browser to freeze the old page (the picture it cross-fades from), then renders the new one; the transition is ended
    /// after the render by <see cref="EndTransitionAsync"/>, and by the module's own timeout when the render never comes.
    /// </summary>
    async Task RenderInTransitionAsync()
    {
        var began = false;

        try
        {
            if (await _module! is { } module)
            {
                began = await module.InvokeAsync<bool>("beginTransition", (int)Math.Clamp(Options.ViewTransitions.Timeout.TotalMilliseconds, 1, int.MaxValue));
            }
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or OperationCanceledException or InvalidOperationException)
        {
            Diagnostics.RouterLog.EffectsFailed(Logger, exception);
        }

        await InvokeAsync(() =>
        {
            _transitionOpen = began;
            StateHasChanged();
        });
    }

    /// <summary>Tells the browser the new page is in place so it can take the picture to animate to.</summary>
    async Task EndTransitionAsync()
    {
        _transitionOpen = false;

        try
        {
            if (await ModuleAsync() is { } module)
            {
                await module.InvokeVoidAsync("endTransition");
            }
        }
        catch (Exception exception) when (exception is JSException or JSDisconnectedException or OperationCanceledException or InvalidOperationException)
        {
            Diagnostics.RouterLog.EffectsFailed(Logger, exception);
        }
    }
}
