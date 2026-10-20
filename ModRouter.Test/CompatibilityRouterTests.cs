using System.Reflection;
using Bunit;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Navigation;
using ModRouter.Sample.Extra;
using ModRouter.Test.Fixtures;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static ModRouter.Test.ComponentTestKit;
using NavigationContext = Microsoft.AspNetCore.Components.Routing.NavigationContext;

namespace ModRouter.Test;

/// <summary>
/// The compatibility <c>Router</c>, <c>RouteView</c> and <c>AuthorizeRouteView</c> under an application shaped like a
/// typical site's <c>App.razor</c>: CascadingAuthenticationState, Router with Found and NotFound, AuthorizeRouteView
/// with NotAuthorized, OnNavigate adding a "lazy" assembly. The test project has no Razor compiler, so the shape is built
/// with the RenderTreeBuilder (the sample's <c>AppCompat.razor</c> is the Razor form, checked in a real browser).
/// </summary>
[TestFixture]
public sealed class CompatibilityRouterTests
{
    /// <summary>A typical App.razor shape. Every knob is a parameter so one component covers the variants.</summary>
    public sealed class CompatApp : ComponentBase
    {
        public List<Assembly> LazyLoadedAssemblies { get; } = [];

        public List<NavigationContext> Contexts { get; } = [];

        public List<RouteData> FoundCalls { get; } = [];

        /// <summary>When set, OnNavigate waits for it (a slow lazy load); the context's token cancels the wait.</summary>
        public Func<NavigationContext, Task>? Gate { get; set; }

        [Parameter]
        public bool AddExtra { get; set; } = true;

        [Parameter]
        public RenderFragment? Navigating { get; set; }

        [Parameter]
        public RenderFragment? Authorizing { get; set; }

        [Parameter]
        public Assembly? ExtraAssemblyAfterStart { get; set; }

        [Parameter]
        public Type? NotFoundPage { get; set; }

        [Parameter]
        public bool WithNotFound { get; set; } = true;

        [Parameter]
        public bool WithCascadingState { get; set; } = true;

        /// <summary>The plain RouteView instead of AuthorizeRouteView.</summary>
        [Parameter]
        public bool PlainRouteView { get; set; }

        [Parameter]
        public bool PreferExactMatches { get; set; }

        /// <summary>Like <see cref="Gate"/> but armed before the render; it applies to every OnNavigateAsync after the first (a navigation's hook).</summary>
        [Parameter]
        public Func<NavigationContext, Task>? LaterGate { get; set; }

        /// <summary>Runs each time the NotFound fragment is invoked (a render of the app's fragment), before its markup.</summary>
        [Parameter]
        public Action? OnNotFoundInvoked { get; set; }

        async Task OnNavigate(NavigationContext context)
        {
            Contexts.Add(context);

            if (AddExtra && !LazyLoadedAssemblies.Contains(typeof(ExtraMarker).Assembly))
            {
                LazyLoadedAssemblies.Add(typeof(ExtraMarker).Assembly);
            }

            if (Gate is { } gate)
            {
                await gate(context);
            }

            if (Contexts.Count > 1 && LaterGate is { } laterGate)
            {
                await laterGate(context);
            }
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (ExtraAssemblyAfterStart is { } extra && !LazyLoadedAssemblies.Contains(extra))
            {
                LazyLoadedAssemblies.Add(extra);
            }

            if (WithCascadingState)
            {
                builder.OpenComponent<CascadingAuthenticationState>(0);
                builder.AddComponentParameter(1, nameof(CascadingAuthenticationState.ChildContent), (RenderFragment)RenderRouter);
                builder.CloseComponent();
            }
            else
            {
                RenderRouter(builder);
            }
        }

        void RenderRouter(RenderTreeBuilder builder)
        {
            builder.OpenComponent<Compatibility.Router>(0);
            builder.AddComponentParameter(1, nameof(Compatibility.Router.AppAssembly), typeof(CompatHomePage).Assembly);
            builder.AddComponentParameter(2, nameof(Compatibility.Router.AdditionalAssemblies), LazyLoadedAssemblies);
            builder.AddComponentParameter(3, nameof(Compatibility.Router.OnNavigateAsync), EventCallback.Factory.Create<NavigationContext>(this, OnNavigate));
            builder.AddComponentParameter(4, nameof(Compatibility.Router.Found), (RenderFragment<RouteData>)RenderFound);
            builder.AddComponentParameter(5, nameof(Compatibility.Router.NotFound), WithNotFound && NotFoundPage is null ? (RenderFragment)RenderNotFound : null);
            builder.AddComponentParameter(6, nameof(Compatibility.Router.NotFoundPage), NotFoundPage);
            builder.AddComponentParameter(7, nameof(Compatibility.Router.Navigating), Navigating);
            builder.AddComponentParameter(8, nameof(Compatibility.Router.PreferExactMatches), PreferExactMatches);
            builder.CloseComponent();
        }

        RenderFragment RenderFound(RouteData routeData) => found =>
        {
            FoundCalls.Add(routeData);

            if (PlainRouteView)
            {
                found.OpenComponent<Compatibility.RouteView>(0);
                found.AddComponentParameter(1, nameof(Compatibility.RouteView.RouteData), routeData);
                found.AddComponentParameter(2, nameof(Compatibility.RouteView.DefaultLayout), typeof(CompatLayout));
                found.CloseComponent();

                return;
            }

            found.OpenComponent<Compatibility.AuthorizeRouteView>(3);
            found.AddComponentParameter(4, nameof(Compatibility.AuthorizeRouteView.RouteData), routeData);
            found.AddComponentParameter(5, nameof(Compatibility.AuthorizeRouteView.DefaultLayout), typeof(CompatLayout));
            found.AddComponentParameter(6, nameof(Compatibility.AuthorizeRouteView.Authorizing), Authorizing);
            found.AddComponentParameter(7, nameof(Compatibility.AuthorizeRouteView.NotAuthorized), (RenderFragment<AuthenticationState>)(context => body =>
            {
                if (context.User.Identity?.IsAuthenticated != true)
                {
                    body.OpenComponent<RedirectToLoginProbe>(0);
                    body.CloseComponent();
                }
                else
                {
                    body.OpenElement(1, "p");
                    body.AddAttribute(2, "id", "not-authorized");
                    body.AddContent(3, $"Not authorized: {context.User.Identity.Name} at {routeData.PageType.Name}");
                    body.CloseElement();
                }
            }));
            found.CloseComponent();
        };

        void RenderNotFound(RenderTreeBuilder notFound)
        {
            OnNotFoundInvoked?.Invoke();
            notFound.OpenComponent<LayoutView>(0);
            notFound.AddComponentParameter(1, nameof(LayoutView.Layout), typeof(CompatLayout));
            notFound.AddComponentParameter(2, nameof(LayoutView.ChildContent), (RenderFragment)(b => b.AddMarkupContent(0, "<p id=\"not-found\">Not found.</p>")));
            notFound.CloseComponent();
        }
    }

    /// <summary>The sites' RedirectToLogin: rendered by the NotAuthorized fragment for an anonymous user, it navigates to the login page.</summary>
    public sealed class RedirectToLoginProbe : ComponentBase
    {
        public static int Renders { get; set; }

        [Inject]
        INavigationService Navigation { get; set; } = default!;

        protected override void OnInitialized()
        {
            Renders++;
            Navigation.NavigateTo("login", replaceHistory: true);
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<p id=\"redirecting\">Redirecting to login...</p>");
    }

    /// <summary>A guard that holds its route's navigation until <see cref="Gate"/> is released (a slow target stage).</summary>
    sealed class SlowTargetGuard : Core.Guards.IRouteGuard
    {
        public static TaskCompletionSource? Gate { get; set; }

        public static int Entered { get; set; }

        public async ValueTask<Core.Guards.RouteGuardResult> CanActivateAsync(Core.Guards.RouteGuardContext context)
        {
            Entered++;

            if (Gate is { } gate)
            {
                await gate.Task.WaitAsync(context.CancellationToken);
            }

            return Core.Guards.RouteGuardResult.Allow();
        }
    }

    /// <summary>Like <see cref="CompatApp"/> but the first OnNavigate waits on a static gate, armed before the render.</summary>
    public sealed class SlowFirstNavigateApp : ComponentBase
    {
        public static TaskCompletionSource Gate { get; set; } = new();

        /// <summary>When false the callback ignores its token, as an application's often does, and completes normally.</summary>
        public static bool HonorsToken { get; set; } = true;

        readonly List<Assembly> _lazy = [];

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<Compatibility.Router>(0);
            builder.AddComponentParameter(1, nameof(Compatibility.Router.AppAssembly), typeof(CompatHomePage).Assembly);
            builder.AddComponentParameter(2, nameof(Compatibility.Router.AdditionalAssemblies), _lazy);
            builder.AddComponentParameter(3, nameof(Compatibility.Router.OnNavigateAsync), EventCallback.Factory.Create<NavigationContext>(this, async context =>
            {
                _lazy.Add(typeof(ExtraMarker).Assembly);
                await (HonorsToken ? Gate.Task.WaitAsync(context.CancellationToken) : Gate.Task);
            }));
            builder.AddComponentParameter(4, nameof(Compatibility.Router.Found), (RenderFragment<RouteData>)(routeData => found =>
            {
                found.OpenComponent<Compatibility.RouteView>(0);
                found.AddComponentParameter(1, nameof(Compatibility.RouteView.RouteData), routeData);
                found.AddComponentParameter(2, nameof(Compatibility.RouteView.DefaultLayout), typeof(CompatLayout));
                found.CloseComponent();
            }));
            builder.AddComponentParameter(5, nameof(Compatibility.Router.Navigating), Markup("<p id=\"navigating\">Navigating...</p>"));
            builder.CloseComponent();
        }
    }

    sealed class ListLog : ILoggerProvider, ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        public void Dispose()
        {
        }
    }

    BunitContext _ctx = default!;
    FakeNavigationAdapter _adapter = default!;
    FakeAuthenticationStateProvider _auth = default!;
    ListLog _log = default!;
    bool _expectsRenderException;

    [SetUp]
    public void SetUp()
    {
        CompatCounters.Reset();
        RedirectToLoginProbe.Renders = 0;
        SlowTargetGuard.Gate = null;
        SlowTargetGuard.Entered = 0;
        SlowFirstNavigateApp.Gate = new TaskCompletionSource();
        SlowFirstNavigateApp.HonorsToken = true;
        _expectsRenderException = false;
        _ctx = new BunitContext();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _auth = new FakeAuthenticationStateProvider();
        _log = new ListLog();
        _ctx.Services.AddTestAuthorization(_auth);
        _ctx.Services.AddLogging(b => b.AddProvider(_log));
    }

    [TearDown]
    public async Task TearDown()
    {
        SlowFirstNavigateApp.Gate.TrySetResult();
        SlowTargetGuard.Gate?.TrySetResult();

        if (!_expectsRenderException)
        {
            Assert.That(_ctx.Renderer.UnhandledException.IsCompleted, Is.False, () => "a render exception was swallowed: " + _ctx.Renderer.UnhandledException.Result);
        }

        await _ctx.DisposeAsync();
        _log.Dispose();
    }

    RouteStateStore Store => _ctx.Services.GetRequiredService<RouteStateStore>();

    /// <summary>A link click, then the wait for the post-change OnNavigateAsync and the commit it releases.</summary>
    async Task GoAsync(string uri)
    {
        Assert.That(await _adapter.NavigateAsync(uri, intercepted: true), Is.True);
        await EventuallyAsync(() => Store.Current?.AbsoluteUri, Is.EqualTo("http://localhost/" + uri));
        await _adapter.SettledAsync();
    }

    IRenderedComponent<CompatApp> Mount(string uri = "http://localhost/", Action<ComponentParameterCollectionBuilder<CompatApp>>? parameters = null, Action<RouteMap>? startup = null)
    {
        _ctx.Services.AddModRouter(startup);
        _adapter = new FakeNavigationAdapter("http://localhost/", uri) { ExecuteNavigateTo = true };
        _ctx.Services.AddSingleton<INavigationPlatformAdapter>(_adapter);

        return _ctx.Render<CompatApp>(parameters ?? (_ => { }));
    }

    // ---- Found, RouteData, instances, back/forward ------------------------------------------------------------------

    [Test]
    public void A_deep_link_renders_the_nested_chain_inside_the_layout_through_the_apps_Found_fragment()
    {
        var cut = Mount("http://localhost/customers/42");

        Assert.That(cut.Find("#layout #customers #customer").TextContent, Is.EqualTo("Customer 42"));
        Assert.That(cut.Instance.FoundCalls.Distinct(), Has.Exactly(1).Items, "one RouteData per committed state");

        var routeData = cut.Instance.FoundCalls[0];
        Assert.That(routeData.PageType, Is.EqualTo(typeof(CompatCustomersPage)), "the depth-0 component, not the leaf");
        Assert.That(routeData.RouteValues, Is.Empty, "the depth-0 level declares no parameters; the leaf's {Id} is not the page's");
        Assert.That(routeData.Template, Is.EqualTo("/customers"));
        Assert.That(cut.Instance.Contexts.Select(c => c.Path), Is.EqualTo(new[] { "customers/42" }), "OnNavigateAsync ran once, before the first navigation, with the built-in's base-relative path");
        Assert.That(cut.Instance.Contexts[0].CancellationToken.IsCancellationRequested, Is.False, "the first navigation's token lives as long as the router");
    }

    [Test]
    public async Task Navigating_between_pages_keeps_the_layout_and_the_parent_instance_while_Found_re_renders_per_navigation()
    {
        var cut = Mount("http://localhost/customers");
        Assert.That(cut.Find("#layout #customers"), Is.Not.Null);

        await GoAsync("customers/42");
        await GoAsync("customers/43");

        Assert.That(cut.Find("#layout #customers #customer").TextContent, Is.EqualTo("Customer 43"));
        Assert.That(CompatLayout.Instances, Is.EqualTo(1), "the layout survives every navigation");
        Assert.That(CompatCustomersPage.Instances, Is.EqualTo(1), "the depth-0 parent is retained");
        Assert.That(CompatCustomerPage.Instances, Is.EqualTo(1), "the child is updated in place");
        Assert.That(CompatCustomerPage.ParameterSets, Is.EqualTo(2));
        Assert.That(cut.Instance.FoundCalls.Distinct(), Has.Exactly(3).Items, "a RouteData per committed state, as the built-in gives its Found fragment");
        Assert.That(cut.Instance.Contexts.Select(c => c.Path), Is.EqualTo(new[] { "customers", "customers/42", "customers/43" }), "OnNavigateAsync runs on every navigation, after the location changed");
    }

    [Test]
    public async Task Back_and_forward_render_the_right_page_and_are_recognized_as_history_traversals()
    {
        var cut = Mount("http://localhost/customers/42");
        await GoAsync("customers/43");

        await _adapter.NavigateAsync("customers/42", historyTraversal: true);
        await EventuallyAsync(() => Store.Current!.Path, Is.EqualTo("/customers/42"));

        Assert.That(cut.Find("#customer").TextContent, Is.EqualTo("Customer 42"));
        Assert.That(Store.Current!.Trigger, Is.EqualTo(NavigationTrigger.HistoryRestore));
        Assert.That(CompatLayout.Instances, Is.EqualTo(1));
    }

    [Test]
    public void A_page_with_its_own_Layout_renders_in_it_and_the_others_in_DefaultLayout()
    {
        var cut = Mount("http://localhost/alt");

        Assert.That(cut.Find("#alt-layout #alt"), Is.Not.Null);
        Assert.That(cut.FindAll("#layout"), Is.Empty);
        Assert.That((CompatAltLayout.Instances, CompatLayout.Instances), Is.EqualTo((1, 0)));
    }

    [Test]
    public void A_top_level_template_without_a_leading_slash_is_routable_as_the_built_in_reads_it()
    {
        var cut = Mount("http://localhost/tool");

        Assert.That(cut.Find("#layout #tool"), Is.Not.Null);
        Assert.That(_ctx.Services.GetRequiredService<RouteTableHolder>().GetRequiredRoute("tool").Template, Is.EqualTo("/tool"));
    }

    // ---- NotFound / NotFoundPage -------------------------------------------------------------------------------------

    [Test]
    public void NotFound_renders_the_apps_fragment_with_its_own_LayoutView()
    {
        var cut = Mount("http://localhost/nope");

        Assert.That(cut.Find("#layout #not-found").TextContent, Is.EqualTo("Not found."));
        Assert.That(cut.Instance.FoundCalls, Is.Empty);
    }

    [Test]
    public async Task Moving_between_a_page_and_not_found_recreates_the_layout_like_the_built_in_Router()
    {
        var cut = Mount("http://localhost/");
        await GoAsync("nope");

        Assert.That(cut.Find("#layout #not-found"), Is.Not.Null);
        Assert.That(CompatLayout.Instances, Is.EqualTo(2), "Found and NotFound own separate LayoutViews, as in the built-in");
    }

    [Test]
    public void NotFoundPage_renders_through_Found_with_an_empty_RouteData_inside_the_layout()
    {
        var cut = Mount("http://localhost/nope", p => p.Add(a => a.NotFoundPage, typeof(CompatNotFoundPage)));

        Assert.That(cut.Find("#layout #not-found-page").TextContent, Is.EqualTo("Nothing here."));
        Assert.That(cut.Instance.FoundCalls.Single().PageType, Is.EqualTo(typeof(CompatNotFoundPage)));
        Assert.That(cut.Instance.FoundCalls.Single().RouteValues, Is.Empty);
        Assert.That(Store.Current!.Failure, Is.EqualTo(RouteFailure.NotFound));
    }

    [Test]
    public void Setting_NotFound_and_NotFoundPage_together_throws_the_built_ins_message()
    {
        _expectsRenderException = true;
        _ctx.Services.AddModRouter();
        _ctx.Services.AddSingleton<INavigationPlatformAdapter>(new FakeNavigationAdapter());

        var exception = Assert.Throws<InvalidOperationException>(() => _ctx.Render(b =>
        {
            b.OpenComponent<Compatibility.Router>(0);
            b.AddComponentParameter(1, nameof(Compatibility.Router.AppAssembly), typeof(CompatHomePage).Assembly);
            b.AddComponentParameter(2, nameof(Compatibility.Router.Found), (RenderFragment<RouteData>)(_ => _ => { }));
            b.AddComponentParameter(3, nameof(Compatibility.Router.NotFound), (RenderFragment)(_ => { }));
            b.AddComponentParameter(4, nameof(Compatibility.Router.NotFoundPage), typeof(CompatNotFoundPage));
            b.CloseComponent();
        }));

        Assert.That(exception!.Message, Is.EqualTo("Setting NotFound and NotFoundPage properties simultaneously is not supported. Use either NotFound or NotFoundPage."));
    }

    // ---- assemblies and OnNavigateAsync ------------------------------------------------------------------------------

    [Test]
    public void OnNavigate_adds_an_assembly_before_the_first_compile_so_its_page_is_routable_on_the_first_navigation()
    {
        var cut = Mount("http://localhost/extra/3");

        Assert.That(cut.Find("#layout #extra #extra-child").TextContent, Is.EqualTo("Extra child 3"));
        Assert.That(cut.Instance.Contexts, Has.Count.EqualTo(1));
        Assert.That(cut.Instance.LazyLoadedAssemblies, Is.EqualTo(new[] { typeof(ExtraMarker).Assembly }));
        Assert.That(_ctx.Services.GetRequiredService<RouteTableHolder>().TryGetRoute("extra-child", out _), Is.True);
    }

    [Test]
    public async Task An_assembly_added_after_the_first_navigation_is_ignored_with_one_warning()
    {
        var cut = Mount("http://localhost/", p => p.Add(a => a.AddExtra, false));
        Assert.That(cut.Instance.LazyLoadedAssemblies, Is.Empty);

        cut.Render(p => p.Add(a => a.ExtraAssemblyAfterStart, typeof(ExtraMarker).Assembly));
        await GoAsync("extra");
        cut.Render(p => p.Add(a => a.ExtraAssemblyAfterStart, typeof(ExtraMarker).Assembly));

        Assert.That(cut.Find("#not-found"), Is.Not.Null, "the late assembly's pages are not routes");
        Assert.That(_log.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message), Has.Exactly(1).Contains("changed after the route table was compiled"));
    }

    [Test]
    public void A_startup_redirect_rule_still_works_next_to_the_discovered_pages()
    {
        var cut = Mount("http://localhost/old-home", startup: routes => routes
            .Route("/fluent-home", typeof(CompatHomePage), name: "fluent-home")
            .Redirect("/old-home", "fluent-home"));

        Assert.That(cut.Find("#layout #home"), Is.Not.Null);
        Assert.That(Store.Current!.Path, Is.EqualTo("/fluent-home"));
    }

    /// <summary>The documented limitation: the compatibility Router discovers at its first navigation, so the startup compile cannot see a page's name.</summary>
    [Test]
    public void A_startup_redirect_to_a_discovered_page_fails_at_AddModRouter_with_RT0017()
    {
        var exception = Assert.Throws<RouteCompilationException>(() => new ServiceCollection().AddModRouter(routes => routes.Redirect("/old-home", "home")))!;

        Assert.That(exception.Errors.Select(e => e.Code), Is.EqualTo(new[] { "RT0017" }));
        Assert.That(exception.Errors[0].Message, Does.Contain("The redirect to the route 'home' has no target"));
    }

    [Test]
    public async Task A_slow_later_OnNavigateAsync_keeps_the_old_page_and_shows_no_Navigating()
    {
        var gate = new TaskCompletionSource();
        var cut = Mount("http://localhost/", p => p.Add(a => a.Navigating, Markup("<p id=\"navigating\">Navigating...</p>")));
        cut.Instance.Gate = async context => await gate.Task.WaitAsync(context.CancellationToken);

        Assert.That(await _adapter.NavigateAsync("customers", intercepted: true), Is.True, "the location changed");
        await EventuallyAsync(() => cut.Instance.Contexts.Count, Is.EqualTo(2));

        Assert.That(cut.Find("#home"), Is.Not.Null, "the old page stays while OnNavigateAsync runs");
        Assert.That(cut.FindAll("#navigating"), Is.Empty, "Navigating is not shown for a later navigation");
        Assert.That(_ctx.Services.GetRequiredService<INavigationService>().IsNavigating, Is.True, "the navigation indicator is the analogue");
        Assert.That(Store.Current!.Path, Is.EqualTo("/"), "the new state waits for OnNavigateAsync");

        gate.SetResult();
        await EventuallyAsync(() => All(cut, "#customers").Count, Is.EqualTo(1));
        Assert.That(_ctx.Services.GetRequiredService<INavigationService>().IsNavigating, Is.False);
    }

    [Test]
    public async Task The_first_pass_shows_Navigating_bare_while_the_first_OnNavigateAsync_is_slow()
    {
        _ctx.Services.AddModRouter();
        _adapter = new FakeNavigationAdapter("http://localhost/", "http://localhost/customers/7") { ExecuteNavigateTo = true };
        _ctx.Services.AddSingleton<INavigationPlatformAdapter>(_adapter);

        var cut = _ctx.Render<SlowFirstNavigateApp>();

        Assert.That(cut.Find("#navigating"), Is.Not.Null, "Navigating is what the user sees during a slow first OnNavigateAsync");
        Assert.That(cut.FindAll("#layout"), Is.Empty, "no layout yet: the route table does not exist");
        Assert.That(Store.Current, Is.Null);

        SlowFirstNavigateApp.Gate.SetResult();
        await EventuallyAsync(() => All(cut, "#layout #customers #customer").Count, Is.EqualTo(1));

        Assert.That(All(cut, "#navigating"), Is.Empty);
        Assert.That(One(cut, "#customer").TextContent, Is.EqualTo("Customer 7"));
    }

    [Test]
    public async Task A_router_disposed_during_its_first_OnNavigateAsync_installs_no_hook_when_the_callback_completes()
    {
        _ctx.Services.AddModRouter();
        _adapter = new FakeNavigationAdapter("http://localhost/", "http://localhost/customers/7") { ExecuteNavigateTo = true };
        _ctx.Services.AddSingleton<INavigationPlatformAdapter>(_adapter);
        SlowFirstNavigateApp.HonorsToken = false;
        _ctx.Render<SlowFirstNavigateApp>();
        var coordinator = _ctx.Services.GetRequiredService<NavigationCoordinator>();

        await _ctx.DisposeComponentsAsync();
        await _ctx.Renderer.Dispatcher.InvokeAsync(SlowFirstNavigateApp.Gate.SetResult);

        // The continuation of the router's OnInitializedAsync is posted to the dispatcher: let it run.
        await _ctx.Renderer.Dispatcher.InvokeAsync(() => { });

        Assert.That(coordinator.OnNavigate, Is.Null, "a later plain ModRouter must not call into the disposed component");
    }

    [Test]
    public async Task A_superseded_navigation_has_its_OnNavigateAsync_token_canceled_and_never_commits()
    {
        var cut = Mount("http://localhost/");
        var gates = new Dictionary<string, TaskCompletionSource>();
        cut.Instance.Gate = async context =>
        {
            var gate = gates[context.Path] = new TaskCompletionSource();
            await gate.Task.WaitAsync(context.CancellationToken);
        };

        Assert.That(await _adapter.NavigateAsync("customers/1", intercepted: true), Is.True);
        await EventuallyAsync(() => gates.ContainsKey("customers/1"), Is.True);
        Assert.That(cut.Find("#home"), Is.Not.Null, "the old page stays while OnNavigateAsync runs");
        Assert.That(await _adapter.NavigateAsync("customers/2", intercepted: true), Is.True);
        await EventuallyAsync(() => gates.ContainsKey("customers/2"), Is.True);

        Assert.That(cut.Instance.Contexts.Single(c => c.Path == "customers/1").CancellationToken.IsCancellationRequested, Is.True, "the older navigation's token is canceled by the newer one");
        Assert.That(cut.Instance.Contexts.Single(c => c.Path == "customers/2").CancellationToken.IsCancellationRequested, Is.False);
        Assert.That(cut.FindAll("#customer"), Is.Empty, "the superseded navigation never commits");

        gates["customers/2"].SetResult();
        await EventuallyAsync(() => All(cut, "#customer").Count, Is.EqualTo(1));
        Assert.That(One(cut, "#customer").TextContent, Is.EqualTo("Customer 2"));
        Assert.That(Store.Current!.Path, Is.EqualTo("/customers/2"));
    }

    /// <summary>
    /// Every site's App.razor has <c>&lt;NotFound&gt;...@RedirectToDashboard();&lt;/NotFound&gt;</c>: the fragment navigates while it
    /// renders. The re-render an async OnNavigateAsync causes (EventCallback re-renders the application's root) must not run
    /// that fragment again for the same committed state: it would navigate again, which cancels the hook that is running
    /// and starts the next, so no hook would ever finish. The redirects are capped so a regression fails instead of
    /// running until the host runs out of memory.
    /// </summary>
    [Test]
    public async Task A_NotFound_fragment_that_navigates_is_not_run_again_by_the_re_render_an_async_OnNavigateAsync_causes()
    {
        const int Cap = 6;
        var gate = new TaskCompletionSource();
        var invocations = 0;
        var redirects = 0;

        void Redirect()
        {
            invocations++;

            if (redirects < Cap)
            {
                redirects++;
                _ctx.Services.GetRequiredService<INavigationService>().NavigateTo("customers/7");
            }
        }

        var cut = Mount(
            "http://localhost/nope",
            p => p
                .Add(a => a.OnNotFoundInvoked, Redirect)
                .Add(a => a.LaterGate, async context => await gate.Task.WaitAsync(context.CancellationToken)));

        await EventuallyAsync(() => cut.Instance.Contexts.Count, Is.GreaterThanOrEqualTo(2));
        await _adapter.SettledAsync();

        Assert.That(redirects, Is.EqualTo(1), "one redirect for the one not-found state; each further navigation would cancel the hook that is running");
        Assert.That(invocations, Is.EqualTo(1), "the NotFound fragment ran once for the committed state");
        Assert.That(cut.Instance.Contexts[1].CancellationToken.IsCancellationRequested, Is.False, "the hook was not superseded");
        Assert.That(One(cut, "#not-found").TextContent, Is.EqualTo("Not found."), "the old output stays while the hook runs");

        gate.SetResult();
        await EventuallyAsync(() => All(cut, "#customer").Count, Is.EqualTo(1));

        Assert.That(One(cut, "#customer").TextContent, Is.EqualTo("Customer 7"));
        Assert.That(Store.Current!.Path, Is.EqualTo("/customers/7"));
        Assert.That(invocations, Is.EqualTo(1));
    }

    /// <summary>The same root cause without a hook: the announcement the router makes after a not-found state renders re-renders the router while the redirect's target is still preparing.</summary>
    [Test]
    public async Task A_NotFound_fragment_that_navigates_runs_once_per_committed_state_not_again_for_the_announcement()
    {
        var gate = new TaskCompletionSource();
        SlowTargetGuard.Gate = gate;
        var invocations = 0;
        var redirects = 0;
        var armed = false;

        void Redirect()
        {
            invocations++;

            if (armed && redirects < 6)
            {
                redirects++;
                _ctx.Services.GetRequiredService<INavigationService>().NavigateTo("slow");
            }
        }

        var cut = Mount(
            "http://localhost/",
            p => p.Add(a => a.OnNotFoundInvoked, Redirect),
            routes => routes.Route("/slow", typeof(CompatHomePage), name: "slow").Guard<SlowTargetGuard>());
        armed = true;

        Assert.That(await _adapter.NavigateAsync("nope", intercepted: true), Is.True);
        await EventuallyAsync(() => One(cut, "[data-mod-router-announcer]").TextContent, Is.Not.Empty);

        Assert.That(invocations, Is.EqualTo(1), "the announcement's re-render does not run the fragment (and its NavigateTo) again");
        Assert.That(redirects, Is.EqualTo(1));
        Assert.That(SlowTargetGuard.Entered, Is.EqualTo(1), "the redirect's target is preparing, held by its guard");

        gate.SetResult();
        await EventuallyAsync(() => Store.Current!.Path, Is.EqualTo("/slow"));
        Assert.That(redirects, Is.EqualTo(1));
    }

    // ---- AuthorizeRouteView ----------------------------------------------------------------------------------------

    [Test]
    public void A_not_authorized_outcome_renders_the_apps_NotAuthorized_fragment_with_the_AuthenticationState_inside_the_layout()
    {
        _auth.User = Users.Authenticated(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "bob"));
        var cut = Mount("http://localhost/admin", p => p.Add(a => a.Authorizing, Markup("<p id=\"authorizing\">Authorizing...</p>")));

        Assert.That(cut.Find("#layout #not-authorized").TextContent, Is.EqualTo("Not authorized: bob at CompatAdminPage"));
        Assert.That(cut.FindAll("#admin"), Is.Empty);
        Assert.That(cut.FindAll("#authorizing"), Is.Empty, "Authorizing is accepted but never shown: authorization completed before the commit");
        Assert.That(Store.Current!.Error!.Kind, Is.EqualTo(RouteErrorKind.NotAuthorized));
        Assert.That(cut.Markup, Does.Not.Contain("Authorizing"));
    }

    [Test]
    public void The_NotAuthorized_fragment_of_AuthorizeRouteView_is_inside_the_error_host()
    {
        _auth.User = Users.Authenticated(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "bob"));
        var cut = Mount("http://localhost/admin");

        AssertInErrorHost(One(cut, "#layout #not-authorized"));
        Assert.That(All(cut, "[data-mod-route-error-host]"), Has.Count.EqualTo(1));
    }

    [Test]
    public void The_denial_of_a_plain_RouteView_is_inside_the_error_host()
    {
        _auth.User = Users.Authenticated();
        var cut = Mount("http://localhost/admin", p => p.Add(a => a.PlainRouteView, true));

        AssertInErrorHost(One(cut, "#layout [data-mod-route-error=\"NotAuthorized\"]"));
    }

    [Test]
    public void An_administrator_sees_the_authorized_page()
    {
        _auth.User = Users.WithRoles("admin");
        var cut = Mount("http://localhost/admin");

        Assert.That(cut.Find("#layout #admin"), Is.Not.Null);
        Assert.That(Store.Current!.Error, Is.Null);
    }

    [Test]
    public void An_AllowAnonymous_page_in_an_authorized_assembly_is_open_to_everyone()
    {
        var cut = Mount("http://localhost/open");

        Assert.That(cut.Find("#layout #open"), Is.Not.Null, "[Authorize] + [AllowAnonymous], the real sites' Index page, renders for an anonymous user");
        Assert.That(Store.Current!.Error, Is.Null);
    }

    [Test]
    public async Task For_an_anonymous_user_the_fragment_can_navigate_to_the_login_page_without_looping()
    {
        var cut = Mount("http://localhost/admin");
        await _adapter.SettledAsync();

        await EventuallyAsync(() => All(cut, "#layout #login").Count, Is.EqualTo(1));
        Assert.That(Store.Current!.Path, Is.EqualTo("/login"));
        Assert.That(RedirectToLoginProbe.Renders, Is.EqualTo(1));
        Assert.That(_adapter.History, Is.EqualTo(new[] { "http://localhost/login" }), "replaced, like the sites' RedirectToLogin");
    }

    [Test]
    public void Without_CascadingAuthenticationState_the_state_comes_from_the_provider()
    {
        _auth.User = Users.Authenticated(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, "carol"));
        var cut = Mount("http://localhost/admin", p => p.Add(a => a.WithCascadingState, false));

        Assert.That(cut.Find("#layout #not-authorized").TextContent, Is.EqualTo("Not authorized: carol at CompatAdminPage"));
    }

    [Test]
    public void The_plain_RouteView_renders_the_chain_in_the_layout_and_PreferExactMatches_is_accepted()
    {
        var cut = Mount("http://localhost/customers/5", p => p.Add(a => a.PlainRouteView, true).Add(a => a.PreferExactMatches, true));

        Assert.That(cut.Find("#layout #customers #customer").TextContent, Is.EqualTo("Customer 5"));
    }

    [Test]
    public void The_plain_RouteView_leaves_a_denial_to_the_outlets_default_content()
    {
        _auth.User = Users.Authenticated();
        var cut = Mount("http://localhost/admin", p => p.Add(a => a.PlainRouteView, true));

        Assert.That(cut.Find("#layout [data-mod-route-error=\"NotAuthorized\"]"), Is.Not.Null, "no NotAuthorized mapping without AuthorizeRouteView");
    }

    [Test]
    public void The_plain_RouteView_still_enforces_Authorize_for_an_anonymous_user()
    {
        var cut = Mount("http://localhost/admin", p => p.Add(a => a.PlainRouteView, true));

        Assert.That(Store.Current!.Error!.Kind, Is.EqualTo(RouteErrorKind.NotAuthorized), "the router's guard decided, whichever view renders the route (the built-in RouteView ignores [Authorize])");
        Assert.That(cut.Find("#layout [data-mod-route-error=\"NotAuthorized\"]"), Is.Not.Null);
        Assert.That(cut.FindAll("#admin"), Is.Empty, "the protected page is not rendered for an anonymous user");
    }

    // ---- outside the compat Router -------------------------------------------------------------------------------------

    [Test]
    public void A_RouteView_or_AuthorizeRouteView_outside_the_compat_Router_throws_a_clear_error()
    {
        _expectsRenderException = true;
        var routeData = new RouteData(typeof(CompatHomePage), new Dictionary<string, object?>());

        var authorize = Assert.Throws<InvalidOperationException>(() => _ctx.Render<Compatibility.AuthorizeRouteView>(p => p.Add(v => v.RouteData, routeData)));
        Assert.That(authorize!.Message, Does.StartWith("AuthorizeRouteView (ModRouter.Compatibility) must be rendered inside the <Found> fragment of the compatibility Router, ModRouter.Compatibility.Router"));
        Assert.That(authorize.Message, Does.Contain("a plain <Router> binds to the built-in router"));

        var plain = Assert.Throws<InvalidOperationException>(() => _ctx.Render<Compatibility.RouteView>(p => p.Add(v => v.RouteData, routeData)));
        Assert.That(plain!.Message, Does.StartWith("RouteView (ModRouter.Compatibility) must be rendered inside"));
    }

    [Test]
    public void A_RouteView_without_RouteData_throws_like_the_built_in()
    {
        _expectsRenderException = true;

        var exception = Assert.Throws<InvalidOperationException>(() => _ctx.Render<Compatibility.RouteView>());

        Assert.That(exception!.Message, Is.EqualTo("The RouteView component requires a non-null value for the parameter RouteData."));
    }
}
