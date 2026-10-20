using Bunit;
using ModRouter.Components;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Guards;
using ModRouter.Core.Loading;
using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static ModRouter.Test.ComponentTestKit;

namespace ModRouter.Test;

/// <summary>
/// The Razor facade: <c>&lt;ModRouter&gt;&lt;Routes&gt;</c> with <c>ModRouteEntry</c> and <c>ModRoutePages</c>. The entries register
/// with the router's collector on the first pass (bare Loading, no layout), the router compiles them with the startup
/// definitions, freezes the holder and only then navigates; a compile error is rendered through <c>Error</c>.
/// </summary>
[TestFixture]
public sealed class MarkupRoutesTests
{
    BunitContext _ctx = default!;
    FakeNavigationAdapter _adapter = default!;
    ListLogger _log = default!;
    bool _expectsRenderException;

    /// <summary>A loader held back by the test, so the first pass can be observed before the navigation commits.</summary>
    sealed class GatedLoader : IRouteLoader
    {
        public static TaskCompletionSource Gate { get; set; } = new();

        public async ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context)
        {
            await Gate.Task.WaitAsync(context.CancellationToken);

            return RouteLoadResult.Create("loaded");
        }
    }

    sealed class RedirectingGuard : IRouteGuard
    {
        public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context)
            => ValueTask.FromResult(RouteGuardResult.RedirectToRoute("customer-details", new { id = 77 }));
    }

    /// <summary>A layout that asks for a markup route's URL while rendering (what a nav bar with RouteLinks does).</summary>
    sealed class LinkingLayoutProbe : LayoutComponentBase
    {
        [Inject]
        INavigationService Navigation { get; set; } = default!;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "id", "layout");
            builder.OpenElement(2, "a");
            builder.AddAttribute(3, "id", "nav-link");
            builder.AddAttribute(4, "href", Navigation.GetRouteUrl("customer-details", new Dictionary<string, object?> { ["id"] = 1 }));
            builder.CloseElement();
            builder.AddContent(5, Body);
            builder.CloseElement();
        }
    }

    sealed class NotFoundProbe : ProbeBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<p id=\"not-found\">Not found.</p>");
    }

    sealed class ListLogger : ILoggerProvider, ILogger
    {
        public List<(LogLevel Level, string Category, string Message)> Entries { get; } = [];

        string _category = string.Empty;

        public ILogger CreateLogger(string categoryName) => new ListLogger { _category = categoryName, Shared = Entries };

        List<(LogLevel, string, string)>? Shared { get; set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => (Shared ?? Entries).Add((logLevel, _category, formatter(state, exception) + (exception is null ? string.Empty : " " + exception.Message)));

        public void Dispose()
        {
        }
    }

    [SetUp]
    public void SetUp()
    {
        Probes.Reset();
        GatedLoader.Gate = new TaskCompletionSource();
        _expectsRenderException = false;
        _log = new ListLogger();
        _ctx = new BunitContext();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        _ctx.Services.AddLogging(logging => logging.AddProvider(_log).SetMinimumLevel(LogLevel.Trace));
    }

    [TearDown]
    public async Task TearDown()
    {
        if (!_expectsRenderException)
        {
            Assert.That(_ctx.Renderer.UnhandledException.IsCompleted, Is.False, () => "a render exception was swallowed: " + _ctx.Renderer.UnhandledException.Result);
        }

        GatedLoader.Gate.TrySetResult();
        await _ctx.DisposeAsync();
        _log.Dispose();
    }

    RouteStateStore Store => _ctx.Services.GetRequiredService<RouteStateStore>();

    RouteTableHolder Holder => _ctx.Services.GetRequiredService<RouteTableHolder>();

    INavigationService Navigation => _ctx.Services.GetRequiredService<INavigationService>();

    // ---- markup builders -----------------------------------------------------------------------------------------

    static RenderFragment Entry(
        string path,
        Type? component,
        string? name = null,
        string? title = null,
        bool index = false,
        RenderFragment? children = null,
        Type? guard = null,
        Type? loader = null,
        string? breadcrumb = null,
        bool breadcrumbHidden = false,
        IEnumerable<string>? aliases = null,
        Func<Core.Matching.RouteParameters, string>? titleFunc = null,
        bool canonicalAlias = false,
        string? focus = null,
        bool noAnnounce = false)
        => builder =>
        {
            builder.OpenComponent<ModRouteEntry>(0);
            builder.AddComponentParameter(1, nameof(ModRouteEntry.Path), path);
            builder.AddComponentParameter(2, nameof(ModRouteEntry.Component), component);
            builder.AddComponentParameter(3, nameof(ModRouteEntry.Name), name);
            builder.AddComponentParameter(4, nameof(ModRouteEntry.Title), title);
            builder.AddComponentParameter(5, nameof(ModRouteEntry.Index), index);
            builder.AddComponentParameter(6, nameof(ModRouteEntry.ChildContent), children);
            builder.AddComponentParameter(7, nameof(ModRouteEntry.Guard), guard);
            builder.AddComponentParameter(8, nameof(ModRouteEntry.Loader), loader);
            builder.AddComponentParameter(9, nameof(ModRouteEntry.Breadcrumb), breadcrumb);
            builder.AddComponentParameter(10, nameof(ModRouteEntry.BreadcrumbHidden), breadcrumbHidden);
            builder.AddComponentParameter(11, nameof(ModRouteEntry.Aliases), aliases);
            builder.AddComponentParameter(12, nameof(ModRouteEntry.TitleFunc), titleFunc);
            builder.AddComponentParameter(13, nameof(ModRouteEntry.CanonicalAlias), canonicalAlias);
            builder.AddComponentParameter(14, nameof(ModRouteEntry.Focus), focus);
            builder.AddComponentParameter(15, nameof(ModRouteEntry.NoAnnounce), noAnnounce);
            builder.CloseComponent();
        };

    static RenderFragment Pages(Func<Type, bool>? filter, System.Reflection.Assembly? assembly = null)
        => builder =>
        {
            builder.OpenComponent<ModRoutePages>(0);
            builder.AddComponentParameter(1, nameof(ModRoutePages.Assembly), assembly ?? typeof(MarkupRoutesTests).Assembly);
            builder.AddComponentParameter(2, nameof(ModRoutePages.Filter), filter);
            builder.CloseComponent();
        };

    /// <summary>An assembly whose exported types cannot be loaded (a missing dependency at runtime).</summary>
    sealed class UnloadableAssembly : System.Reflection.Assembly
    {
        public override string FullName => "Fake.Pages, Version=1.0.0.0";

        public override System.Reflection.AssemblyName GetName() => new("Fake.Pages");

        public override Type[] GetExportedTypes() => throw new System.Reflection.ReflectionTypeLoadException([], [new FileNotFoundException("Could not load file or assembly 'Fake.Dependency'.")]);
    }

    static RenderFragment Entries(params RenderFragment[] entries)
        => builder =>
        {
            var sequence = 0;

            foreach (var entry in entries)
            {
                builder.AddContent(sequence++, entry);
            }
        };

    /// <summary>The customers tree as markup: /customers (index list, {id:int} details with a title factory (orders/{orderId:guid})).</summary>
    static RenderFragment CustomersEntry()
        => Entry("/customers", typeof(CustomersProbe), "customers", "Customers", children: Entries(
            Entry(string.Empty, typeof(ListProbe), "customer-list", index: true),
            Entry("{id:int}", typeof(DetailsProbe), "customer-details", titleFunc: p => $"Customer {p["id"]}", children: Entries(
                Entry("orders/{orderId:guid}", typeof(OrderProbe), "customer-order-details", "Order")))));

    static RenderFragment AboutEntry(string name = "about", string path = "/about") => Entry(path, typeof(ShowProbe), name, "About");

    static RenderFragment CustomersMarkup(string aboutName = "about") => Entries(CustomersEntry(), AboutEntry(aboutName));

    IRenderedComponent<Components.ModRouter> Mount(
        RenderFragment? routes,
        string uri = "http://localhost/",
        Action<RouteMap>? startup = null,
        Type? layout = null,
        bool noStartup = false)
    {
        if (noStartup)
        {
            _ctx.Services.AddModRouter();
        }
        else
        {
            _ctx.Services.AddModRouter(startup ?? (map => map.Route("/", typeof(HomeProbe), name: "home", title: "Home")));
        }

        _adapter = new FakeNavigationAdapter("http://localhost/", uri) { ExecuteNavigateTo = true };
        _ctx.Services.AddSingleton<INavigationPlatformAdapter>(_adapter);

        return _ctx.Render<Components.ModRouter>(p =>
        {
            p.Add(r => r.DefaultLayout, layout ?? typeof(MainLayoutProbe));
            p.Add(r => r.Loading, b => b.AddMarkupContent(0, "<p id=\"loading\">Loading...</p>"));
            p.Add(r => r.NotFound, b =>
            {
                b.OpenComponent<NotFoundProbe>(0);
                b.CloseComponent();
            });
            p.Add(r => r.Error, error => b =>
            {
                // The message is text (it quotes tags such as <ModRouteEntry ...>), never markup.
                b.OpenElement(0, "p");
                b.AddAttribute(1, "id", "route-error");
                b.AddAttribute(2, "data-kind", error.Kind.ToString());
                b.AddContent(3, error.Message);
                b.CloseElement();
            });

            if (routes is not null)
            {
                p.Add(r => r.Routes, routes);
            }
        });
    }

    async Task GoAsync(string uri, bool historyTraversal = false)
    {
        await _adapter.NavigateAsync(uri, intercepted: !historyTraversal, historyTraversal: historyTraversal);
        await _adapter.SettledAsync();
    }

    // ---- collect, compile, freeze, navigate ---------------------------------------------------------------------

    [Test]
    public async Task Every_entry_nested_ones_included_is_compiled_before_the_initial_navigation_which_commits_the_deep_link_first()
    {
        // That the freeze precedes the first navigation is proved by the gated-loader test below, which holds the navigation open.
        var cut = Mount(CustomersMarkup(), "http://localhost/customers/5/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77");
        await EventuallyAsync(() => All(cut, "#order").Count, Is.EqualTo(1));

        Assert.That(Holder.IsFrozen, Is.True);
        Assert.That(Holder.Table.TryGetRoute("customer-order-details", out _), Is.True, "the nested markup route is in the frozen table");
        Assert.That(Holder.Table.TryGetRoute("customer-list", out _), Is.True);
        Assert.That(Holder.Table.TryGetRoute("home", out _), Is.True, "and so is the startup route");
        Assert.That(Store.Current!.Path, Is.EqualTo("/customers/5/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77"), "the deep link is the first committed state");
        Assert.That(Store.Current.Chain!.Leaf.RouteName, Is.EqualTo("customer-order-details"));
        Assert.That(Store.Current.Title, Is.EqualTo("Order"));
        Assert.That(One(cut, "#layout #customers #details #order").TextContent, Does.StartWith("Order 4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77 of customer 5"));
        Assert.That(Probes.Count("ctor:NotFoundProbe"), Is.Zero, "no flash of not-found");
    }

    [Test]
    public async Task The_initial_navigation_starts_only_after_the_freeze_and_the_layout_is_not_rendered_on_the_first_pass()
    {
        var cut = Mount(Entries(CustomersMarkup(), Entry("/slow", typeof(ShowProbe), "slow", loader: typeof(GatedLoader))), "http://localhost/slow", layout: typeof(LinkingLayoutProbe));

        // The loader holds the navigation: what is on screen is the first pass.
        Assert.That(All(cut, "#loading"), Has.Count.EqualTo(1), "bare Loading");
        Assert.That(All(cut, "#layout"), Is.Empty, "the layout (which asks for a markup route's URL) must not render before the table is final");
        Assert.That(Holder.IsFrozen, Is.True, "the table was frozen before the navigation started");
        Assert.That(Store.Current, Is.Null);
        var rendersBefore = cut.RenderCount;

        GatedLoader.Gate.SetResult();
        await EventuallyAsync(() => All(cut, "#layout #show").Count, Is.EqualTo(1));

        Assert.That(One(cut, "#nav-link").GetAttribute("href"), Is.EqualTo("/customers/1"), "the layout resolves the markup route once it renders");
        Assert.That(All(cut, "#loading"), Is.Empty);
        Assert.That(rendersBefore, Is.EqualTo(1), "one render on the first pass");
        Assert.That(cut.RenderCount, Is.EqualTo(2), "then one for the committed chain");
    }

    [Test]
    public void A_markup_router_renders_twice_on_first_load_and_a_router_without_markup_once()
    {
        var markup = Mount(CustomersMarkup(), "http://localhost/customers");
        Assert.That(All(markup, "#customers #list"), Has.Count.EqualTo(1));
        Assert.That(markup.RenderCount, Is.EqualTo(2), "bare Loading, then the chain");
        Assert.That(Probes.Count("ctor:MainLayoutProbe"), Is.EqualTo(1), "the layout is created once, after the compile");
        Assert.That(Probes.Count("ctor:NotFoundProbe"), Is.Zero);
    }

    [Test]
    public void Without_markup_nothing_changes_one_render_inside_the_layout()
    {
        var cut = Mount(null, "http://localhost/");

        Assert.That(All(cut, "#layout #home"), Has.Count.EqualTo(1));
        Assert.That(cut.RenderCount, Is.EqualTo(1));
    }

    // ---- after the first render ----------------------------------------------------------------------------------

    [Test]
    public async Task Deep_link_back_forward_and_GetRouteUrl_work_for_markup_routes_after_the_first_render()
    {
        var cut = Mount(CustomersMarkup(), "http://localhost/customers/5");
        await EventuallyAsync(() => All(cut, "#details").Count, Is.EqualTo(1));

        Assert.That(Navigation.GetRouteUrl("customer-order-details", new Dictionary<string, object?> { ["id"] = 5, ["orderId"] = Guid.Empty }), Is.EqualTo("/customers/5/orders/00000000-0000-0000-0000-000000000000"));
        Assert.That(Navigation.GetRouteUrl("home"), Is.EqualTo("/"));

        await GoAsync("about");
        Assert.That(One(cut, "#show").TextContent, Is.EqualTo("show"));
        await GoAsync("customers/7");
        Assert.That(One(cut, "#details").TextContent, Does.Contain("Customer 7"));
        Assert.That(Store.Current!.Title, Is.EqualTo("Customer 7"), "the markup title factory");

        await GoAsync("about", historyTraversal: true);
        Assert.That(Store.Current!.Trigger, Is.EqualTo(NavigationTrigger.HistoryRestore));
        Assert.That(All(cut, "#show"), Has.Count.EqualTo(1));

        await GoAsync("customers/7", historyTraversal: true);
        Assert.That(Store.Current!.Trigger, Is.EqualTo(NavigationTrigger.HistoryRestore));
        Assert.That(One(cut, "#details").TextContent, Does.Contain("Customer 7"));

        Navigation.NavigateToRoute("customer-order-details", new { orderId = Guid.Empty }, useAmbientValues: true);
        await _adapter.SettledAsync();
        Assert.That(Store.Current!.Path, Is.EqualTo("/customers/7/orders/00000000-0000-0000-0000-000000000000"));
    }

    [Test]
    public async Task Guards_loaders_aliases_breadcrumbs_and_the_other_entry_parameters_reach_the_definition()
    {
        var cut = Mount(Entries(
            CustomersMarkup(),
            Entry("/secret", typeof(ShowProbe), "secret", guard: typeof(RedirectingGuard)),
            Entry("/vendors", typeof(ShowProbe), "vendors", aliases: ["/suppliers"], canonicalAlias: true, breadcrumb: "Vendors", focus: "#v", noAnnounce: true),
            Entry("/quiet", typeof(ShowProbe), "quiet", breadcrumbHidden: true, loader: typeof(GatedLoader))), "http://localhost/");
        GatedLoader.Gate.SetResult();
        await EventuallyAsync(() => All(cut, "#home").Count, Is.EqualTo(1));

        var vendors = Holder.Table.GetRequiredRoute("vendors");
        Assert.That(vendors.Aliases, Is.EqualTo(new[] { "/suppliers" }));
        Assert.That(vendors.Metadata.GetRequired<RouteAliasMetadata>().Behavior, Is.EqualTo(AliasBehavior.RedirectToCanonical));
        Assert.That(vendors.Metadata.GetRequired<RouteBreadcrumbMetadata>().Label, Is.EqualTo("Vendors"));
        Assert.That(vendors.Metadata.GetRequired<RouteFocusMetadata>().Selector, Is.EqualTo("#v"));
        Assert.That(vendors.Metadata.GetRequired<RouteAnnounceMetadata>().Enabled, Is.False);
        Assert.That(vendors.Source, Is.EqualTo("markup: <ModRouteEntry Path=\"/vendors\" Component=\"ShowProbe\" Name=\"vendors\" Aliases=\"/suppliers\" Breadcrumb=\"Vendors\" Focus=\"#v\" NoAnnounce CanonicalAlias>"));
        var quiet = Holder.Table.GetRequiredRoute("quiet");
        Assert.That(quiet.Metadata.GetRequired<RouteBreadcrumbMetadata>().Hidden, Is.True);
        Assert.That(quiet.Metadata.GetRequired<RouteLoaderMetadata>(), Is.EqualTo(new RouteLoaderMetadata(typeof(GatedLoader), RouteReloadPolicy.WhenParametersChange)));
        Assert.That(Holder.Table.GetRequiredRoute("secret").Metadata.GetRequired<RouteGuardMetadata>().GuardType, Is.EqualTo(typeof(RedirectingGuard)));

        await GoAsync("secret");
        Assert.That(Store.Current!.Path, Is.EqualTo("/customers/77"), "the guard redirected by a markup route name");
        await GoAsync("suppliers");
        Assert.That(Store.Current!.Path, Is.EqualTo("/vendors"), "the canonical alias redirected");
    }

    [Test]
    public async Task A_markup_only_app_registers_the_router_without_routes()
    {
        var cut = Mount(Entries(Entry("/", typeof(HomeProbe), "home", "Home"), CustomersMarkup()), "http://localhost/customers/3", noStartup: true);
        await EventuallyAsync(() => All(cut, "#details").Count, Is.EqualTo(1));

        Assert.That(Holder.StartupDefinitions, Is.Empty);
        Assert.That(One(cut, "#details").TextContent, Does.Contain("Customer 3"));
        Assert.That(Navigation.GetRouteUrl("home"), Is.EqualTo("/"));
    }

    [Test]
    public async Task ModRoutePages_discovers_pages_from_markup()
    {
        // The App fixture set declares "/" as "home" itself, so the router starts without startup routes.
        var cut = Mount(Entries(AboutEntry(), Pages(type => type.Namespace == "ModRouter.Test.Pages.App")), "http://localhost/org-chart/4", noStartup: true);
        await EventuallyAsync(() => All(cut, "#org-chart").Count, Is.EqualTo(1));

        Assert.That(One(cut, "#org-chart").TextContent, Is.EqualTo("employee 4"));
        Assert.That(Holder.Table.GetRequiredRoute("org-chart").Source, Does.StartWith("page: "));
        Assert.That(Navigation.GetRouteUrl("about"), Is.EqualTo("/about"), "the markup entry is still there");
        Assert.That(Navigation.GetRouteUrl("customer", new Dictionary<string, object?> { ["id"] = 2 }), Is.EqualTo("/customers/2"), "a discovered nested page");
        Assert.That(Holder.StartupDefinitions, Is.Empty, "discovered pages belong to the markup, not the startup definitions");
        Assert.That(Navigation.GetRouteUrl("home"), Is.EqualTo("/"), "a discovered page");
    }

    // ---- static content -------------------------------------------------------------------------------------------

    [Test]
    public async Task An_entry_rendered_after_the_freeze_throws_and_the_table_is_unchanged()
    {
        _expectsRenderException = true;
        var cut = Mount(CustomersMarkup(), "http://localhost/");
        await EventuallyAsync(() => All(cut, "#home").Count, Is.EqualTo(1));
        var before = Holder.Table;

        Exception? thrown = null;

        try
        {
            cut.Render(p => p.Add(r => r.Routes, Entries(CustomersEntry(), AboutEntry(), Entry("/late", typeof(ShowProbe), "late"))));
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        await EventuallyAsync(() => thrown is not null || _ctx.Renderer.UnhandledException.IsCompleted, Is.True);
        var error = thrown ?? _ctx.Renderer.UnhandledException.Result;
        Assert.That(error, Is.InstanceOf<InvalidOperationException>().With.Message.Contains("after ModRouter's first render").And.Message.Contains("<ModRouteEntry Path=\"/late\""));
        Assert.That(Holder.Table, Is.SameAs(before));
        Assert.That(Holder.Table.TryGetRoute("late", out _), Is.False);
    }

    [Test]
    public async Task A_changed_parameter_on_a_registered_entry_is_ignored_with_a_debug_log()
    {
        var cut = Mount(CustomersMarkup(), "http://localhost/");
        await EventuallyAsync(() => All(cut, "#home").Count, Is.EqualTo(1));

        cut.Render(p => p.Add(r => r.Routes, Entries(CustomersEntry(), AboutEntry(path: "/about-moved"))));

        Assert.That(_log.Entries.Where(e => e.Level == LogLevel.Debug).Select(e => e.Message), Has.Some.Contains("ignored").And.Some.Contains("/about-moved"));
        await GoAsync("about");
        Assert.That(All(cut, "#show"), Has.Count.EqualTo(1), "the old path still matches");
        await GoAsync("about-moved");
        Assert.That(All(cut, "#not-found"), Has.Count.EqualTo(1), "the new path does not");
    }

    [Test]
    public void An_entry_outside_a_router_is_a_clear_error()
    {
        _expectsRenderException = true;

        var error = Assert.Throws<InvalidOperationException>(() => _ctx.Render(Entry("/x", typeof(ShowProbe))))!;

        Assert.That(error.Message, Does.Contain("<ModRouter><Routes>"));
    }

    // ---- configuration errors are rendered -----------------------------------------------------------------------

    [Test]
    public void A_duplicate_route_name_in_markup_renders_the_Error_fragment_instead_of_an_endless_Loading()
    {
        // "home" is already the startup route's name.
        var cut = Mount(CustomersMarkup(aboutName: "home"), "http://localhost/");

        var error = One(cut, "#route-error");
        Assert.That(error.GetAttribute("data-kind"), Is.EqualTo(nameof(RouteErrorKind.ConfigurationFailed)));
        Assert.That(error.TextContent, Does.Contain("RT0010").And.Contain("markup: <ModRouteEntry Path=\"/about\""));
        Assert.That(All(cut, "#loading"), Is.Empty);
        Assert.That(All(cut, "#layout"), Is.Empty, "a layout could ask for a route that does not exist: the error renders bare");
        Assert.That(Holder.IsFrozen, Is.False, "nothing was frozen");
        Assert.That(Store.Current, Is.Null, "nothing committed");
        Assert.That(_log.Entries.Where(e => e.Level == LogLevel.Error).Select(e => e.Message), Has.Some.Contains("RT0010"));
    }

    [Test]
    public void A_configuration_error_is_rendered_inside_the_error_host()
    {
        var cut = Mount(CustomersMarkup(aboutName: "home"), "http://localhost/");

        AssertInErrorHost(One(cut, "#route-error"));
    }

    [Test]
    public async Task After_a_configuration_error_the_router_handles_no_navigation_and_the_not_final_hint_is_gone()
    {
        var cut = Mount(CustomersMarkup(aboutName: "home"), "http://localhost/");
        Assert.That(All(cut, "#route-error"), Has.Count.EqualTo(1));
        Assert.That(_adapter.HandlerCount, Is.Zero, "the router gave up navigation: the startup table is not what the app declared");

        // A link click: the pipeline must not run against the startup table and commit a state the error hides.
        await GoAsync("about");
        Assert.That(Store.Current, Is.Null, "nothing committed");
        Assert.That(All(cut, "#route-error"), Has.Count.EqualTo(1), "the error is still the only thing on screen");
        Assert.That(All(cut, "#not-found"), Is.Empty);
        Assert.That(All(cut, "#show"), Is.Empty);

        // A programmatic navigation.
        Navigation.NavigateTo("/customers");
        await _adapter.SettledAsync();
        Assert.That(Store.Current, Is.Null);
        Assert.That(All(cut, "#route-error"), Has.Count.EqualTo(1));
        Assert.That(All(cut, "#customers"), Is.Empty);

        // The table will never become final, so the hint would only mislead.
        var error = Assert.Throws<Core.Generation.RouteUrlGenerationException>(() => Navigation.GetRouteUrl("nope"))!;
        Assert.That(error.Message, Does.Not.Contain(RouteTableHolder.NotFinalHint));
        Assert.That(Holder.IsFrozen, Is.False, "still nothing was frozen");
    }

    [Test]
    public void A_binding_error_in_markup_renders_the_same_way()
    {
        // DetailsProbe.Id is an int but the route says guid: never convertible.
        var cut = Mount(Entry("/d/{id:guid}", typeof(DetailsProbe), "d"), "http://localhost/");

        var error = One(cut, "#route-error");
        Assert.That(error.GetAttribute("data-kind"), Is.EqualTo(nameof(RouteErrorKind.ConfigurationFailed)));
        Assert.That(error.TextContent, Does.Contain("can never be converted"));
    }

    [Test]
    public void A_throwing_ModRoutePages_filter_renders_ConfigurationFailed_instead_of_an_endless_Loading()
    {
        RouteError? captured = null;
        var cut = Mount(Entries(AboutEntry(), Pages(_ => throw new InvalidOperationException("filter boom"))), "http://localhost/", noStartup: true);
        cut.Render(p => p.Add(r => r.Error, error => b =>
        {
            captured = error;
            b.OpenElement(0, "p");
            b.AddAttribute(1, "id", "route-error");
            b.AddContent(2, error.Message);
            b.CloseElement();
        }));

        var text = One(cut, "#route-error").TextContent;
        Assert.That(All(cut, "#loading"), Is.Empty, "no endless Loading");
        Assert.That(text, Does.Contain("RT0023").And.Contain("<ModRoutePages Assembly=\"ModRouter.Test\">").And.Contain("Filter threw").And.Contain("filter boom"));
        Assert.That(captured!.Kind, Is.EqualTo(RouteErrorKind.ConfigurationFailed));
        Assert.That(captured.Exception, Is.InstanceOf<RouteCompilationException>().With.InnerException.InstanceOf<InvalidOperationException>());
        Assert.That(_log.Entries.Where(e => e.Level == LogLevel.Error).Select(e => e.Message), Has.Some.Contains("filter boom"));
    }

    [Test]
    public void An_assembly_whose_types_cannot_be_loaded_renders_ConfigurationFailed_naming_the_assembly()
    {
        var cut = Mount(Entries(AboutEntry(), Pages(null, new UnloadableAssembly())), "http://localhost/", noStartup: true);

        var error = One(cut, "#route-error");
        Assert.That(error.GetAttribute("data-kind"), Is.EqualTo(nameof(RouteErrorKind.ConfigurationFailed)));
        Assert.That(error.TextContent, Does.Contain("<ModRoutePages Assembly=\"Fake.Pages\">").And.Contain("types could not be loaded").And.Contain("Fake.Dependency"));
        Assert.That(All(cut, "#loading"), Is.Empty);
        Assert.That(Holder.IsFrozen, Is.False);
    }

    [Test]
    public void RT0023_an_invalid_entry_is_reported_with_its_tag()
    {
        var cut = Mount(Entries(
            Entry("/no-component", null),
            Entry("/with-path", typeof(ShowProbe), index: true),
            Entry("/bad-guard", typeof(ShowProbe), guard: typeof(string)),
            Entry("/bad-loader", typeof(ShowProbe), loader: typeof(string)),
            Entry("/both", typeof(ShowProbe), breadcrumb: "x", breadcrumbHidden: true)), "http://localhost/");

        var text = One(cut, "#route-error").TextContent;
        Assert.That(text, Does.Contain("5 errors"));
        Assert.That(text.Split("RT0023"), Has.Length.EqualTo(6), "five RT0023 errors");
        Assert.That(text, Does.Contain("<ModRouteEntry Path=\"/no-component\"").And.Contain("Index").And.Contain("IRouteGuard").And.Contain("IRouteLoader").And.Contain("BreadcrumbHidden"));
    }

    [Test]
    public async Task A_configuration_error_is_router_level_and_its_retry_does_nothing()
    {
        RouteError? captured = null;
        var renders = 0;
        _ctx.Services.AddModRouter();
        _adapter = new FakeNavigationAdapter("http://localhost/", "http://localhost/") { ExecuteNavigateTo = true };
        _ctx.Services.AddSingleton<INavigationPlatformAdapter>(_adapter);

        var cut = _ctx.Render<Components.ModRouter>(p =>
        {
            p.Add(r => r.Routes, Entry("/x", null));
            p.Add(r => r.Error, error => b =>
            {
                captured = error;
                renders++;
                b.AddMarkupContent(0, "<p id=\"route-error\"></p>");
            });
        });

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Kind, Is.EqualTo(RouteErrorKind.ConfigurationFailed));
        Assert.That(captured.IsChainLevel, Is.False);
        Assert.That(captured.Level, Is.Null);
        Assert.That(captured.Exception, Is.InstanceOf<RouteCompilationException>());
        var markup = cut.Markup;
        var renderCount = cut.RenderCount;

        await captured.Retry();
        await _adapter.SettledAsync();

        Assert.That(Store.Current, Is.Null, "a retry cannot compile the markup again: nothing commits");
        Assert.That(Holder.IsFrozen, Is.False);
        Assert.That(_adapter.NavigateCalls, Is.Empty, "and it navigates nowhere");
        Assert.That(cut.RenderCount, Is.EqualTo(renderCount), "no re-render");
        Assert.That(renders, Is.EqualTo(1), "the error fragment rendered once");
        Assert.That(cut.Markup, Is.EqualTo(markup), "the error is still what is on screen");
        Assert.That(One(cut, "#route-error"), Is.Not.Null);
    }

    [Test]
    public void A_startup_error_still_throws_from_AddModRouter()
    {
        Assert.Throws<RouteCompilationException>(() => _ctx.Services.AddModRouter(map => map.Route("/a", typeof(HomeProbe), name: "a").Route("/b", typeof(ShowProbe), name: "a")));
    }

    // ---- the holder and the router ---------------------------------------------------------------------------------

    [Test]
    public async Task A_remount_with_the_same_markup_is_a_no_op_and_a_different_markup_throws()
    {
        var cut = Mount(CustomersMarkup(), "http://localhost/customers/5");
        await EventuallyAsync(() => All(cut, "#details").Count, Is.EqualTo(1));
        var table = Holder.Table;

        await _ctx.DisposeComponentsAsync();
        var again = _ctx.Render<Components.ModRouter>(p =>
        {
            p.Add(r => r.DefaultLayout, typeof(MainLayoutProbe));
            p.Add(r => r.Routes, CustomersMarkup());
        });
        await EventuallyAsync(() => All(again, "#details").Count, Is.EqualTo(1));
        Assert.That(Holder.Table, Is.SameAs(table), "the first table stays");

        _expectsRenderException = true;
        await _ctx.DisposeComponentsAsync();
        Exception? thrown = null;

        try
        {
            _ctx.Render<Components.ModRouter>(p =>
            {
                p.Add(r => r.DefaultLayout, typeof(MainLayoutProbe));
                p.Add(r => r.Routes, Entries(CustomersEntry(), AboutEntry(path: "/elsewhere")));
            });
        }
        catch (Exception exception)
        {
            thrown = exception;
        }

        await EventuallyAsync(() => thrown is not null || _ctx.Renderer.UnhandledException.IsCompleted, Is.True);
        var error = thrown ?? _ctx.Renderer.UnhandledException.Result;
        Assert.That(Unwrap(error), Is.InstanceOf<InvalidOperationException>().With.Message.StartsWith("The route table is already frozen."));
    }

    static Exception Unwrap(Exception exception) => exception is AggregateException { InnerException: { } inner } ? Unwrap(inner) : exception;

    [Test]
    public async Task Before_the_first_render_a_markup_route_name_fails_with_the_not_final_hint_and_resolves_afterwards()
    {
        _ctx.Services.AddModRouter(map => map.Route("/", typeof(HomeProbe), name: "home"));
        _adapter = new FakeNavigationAdapter("http://localhost/", "http://localhost/") { ExecuteNavigateTo = true };
        _ctx.Services.AddSingleton<INavigationPlatformAdapter>(_adapter);

        var early = Assert.Throws<Core.Generation.RouteUrlGenerationException>(() => Navigation.GetRouteUrl("customer-details", new Dictionary<string, object?> { ["id"] = 1 }))!;
        Assert.That(early.Message, Does.EndWith(RouteTableHolder.NotFinalHint));

        var cut = _ctx.Render<Components.ModRouter>(p => p.Add(r => r.Routes, CustomersMarkup()));
        await EventuallyAsync(() => All(cut, "#home").Count, Is.EqualTo(1));

        Assert.That(Navigation.GetRouteUrl("customer-details", new Dictionary<string, object?> { ["id"] = 1 }), Is.EqualTo("/customers/1"));
        var late = Assert.Throws<Core.Generation.RouteUrlGenerationException>(() => Navigation.GetRouteUrl("nope"))!;
        Assert.That(late.Message, Does.Not.Contain(RouteTableHolder.NotFinalHint));
    }
}
