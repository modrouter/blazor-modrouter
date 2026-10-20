using Bunit;
using static ModRouter.Test.ComponentTestKit;
using ModRouter.Components;
using ModRouter.Core.Definitions;
using ModRouter.Core.Guards;
using ModRouter.Core.Loading;
using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace ModRouter.Test;

[TestFixture]
public sealed class RouterFeatureComponentTests
{
    BunitContext _ctx = default!;
    FakeNavigationAdapter _adapter = default!;
    GuardScript _guards = default!;
    LoaderScript _loaders = default!;

    [SetUp]
    public void SetUp()
    {
        Probes.Reset();
        _ctx = new BunitContext();
        _guards = new GuardScript();
        _loaders = new LoaderScript();
        _ctx.Services.AddSingleton(_guards);
        _ctx.Services.AddSingleton(_loaders);
    }

    [TearDown]
    public async Task TearDown()
    {
        Assert.That(_ctx.Renderer.UnhandledException.IsCompleted, Is.False, () => "a render exception was swallowed: " + _ctx.Renderer.UnhandledException.Result);

        await _ctx.DisposeAsync();
    }

    RouteStateStore Store => _ctx.Services.GetRequiredService<RouteStateStore>();

    static void Routes(RouteMap map) => map
        .Route("/", typeof(HomeProbe), name: "home")
        .Route("/login", typeof(ShowProbe), name: "login")
        .Route("/customers", typeof(CustomersProbe), name: "customers", children: customers => customers
            .Index(typeof(ListProbe), name: "customer-list")
            .Route("{id:int}", typeof(DetailsProbe), name: "customer"))
        .Loader<CustomersLoader>();

    IRenderedComponent<Components.ModRouter> Mount(
        Action<RouteMap> routes,
        string uri = "http://localhost/",
        Action<ComponentParameterCollectionBuilder<Components.ModRouter>>? parameters = null)
    {
        _ctx.Services.AddModRouter(routes);
        _adapter = new FakeNavigationAdapter("http://localhost/", uri) { ExecuteNavigateTo = true };
        _ctx.Services.AddSingleton<INavigationPlatformAdapter>(_adapter);

        return _ctx.Render<Components.ModRouter>(p =>
        {
            p.Add(r => r.DefaultLayout, typeof(MainLayoutProbe));
            parameters?.Invoke(p);
        });
    }

    async Task GoAsync(string uri)
    {
        await _adapter.NavigateAsync(uri, intercepted: true);
        await _adapter.SettledAsync();
    }

    // ---- loading -----------------------------------------------------------------------------------------------

    [Test]
    public async Task Loading_renders_inside_the_layout_until_the_first_navigation_commits()
    {
        var gate = new TaskCompletionSource();
        _guards.Set("auth", async _ =>
        {
            await gate.Task;

            return RouteGuardResult.Allow();
        });
        var cut = Mount(
            map => map.Route("/", typeof(HomeProbe), name: "home").Guard<AuthGuard>(),
            parameters: p => p.Add(r => r.Loading, b => b.AddMarkupContent(0, "<p id=\"loading\">please wait</p>")));

        Assert.That(cut.Find("#layout #loading").TextContent, Is.EqualTo("please wait"));
        Assert.That(cut.FindAll("#home"), Is.Empty);

        gate.SetResult();
        await EventuallyAsync(() => All(cut, "#home").Count, Is.EqualTo(1));

        Assert.That(cut.FindAll("#loading"), Is.Empty);
        Assert.That(Probes.Count("ctor:MainLayoutProbe"), Is.EqualTo(1), "the layout survives the move from loading to the page");
    }

    // ---- chain-level errors ------------------------------------------------------------------------------------

    [Test]
    public async Task A_not_authorized_child_shows_its_error_while_the_parent_stays_rendered()
    {
        var cut = Mount(
            map => map
                .Route("/", typeof(HomeProbe), name: "home")
                .Route("/customers", typeof(CustomersProbe), name: "customers", children: c => c
                    .Route("{id:int}", typeof(DetailsProbe), name: "customer").Guard<AdminGuard>()));
        _guards.Set("admin", RouteGuardResult.NotAuthorized("Admins only"));

        await GoAsync("customers/5");

        Assert.That(cut.Find("#customers [role=alert]").TextContent, Is.EqualTo("Admins only"));
        Assert.That(cut.Find("#customers [role=alert]").GetAttribute("data-mod-route-error"), Is.EqualTo("NotAuthorized"));
        Assert.That(cut.FindAll("#details"), Is.Empty);
        Assert.That(Store.Current!.Path, Is.EqualTo("/customers/5"));
    }

    [Test]
    public async Task The_parent_never_remounts_when_a_child_errors_and_is_retried()
    {
        var failing = true;
        var cut = Mount(map => map
            .Route("/", typeof(HomeProbe), name: "home")
            .Route("/customers", typeof(CustomersProbe), name: "customers", children: c => c
                .Route("{id:int}", typeof(DetailsProbe), name: "customer").Loader<CustomerLoader>()));
        _loaders.Set("customer", _ => failing ? throw new InvalidOperationException("db down") : ValueTask.FromResult(RouteLoadResult.Create("ok")));
        await GoAsync("customers/5");

        Assert.That(cut.Find("#customers [role=alert]").TextContent, Does.Contain("db down"));

        failing = false;
        await Store.Current!.Error!.Retry();

        Assert.That(cut.Find("#customers #details").TextContent, Does.Contain("Customer 5"));
        Assert.That(cut.FindAll("[role=alert]"), Is.Empty);
        Assert.That(Probes.Count("ctor:CustomersProbe"), Is.EqualTo(1));
        Assert.That(Probes.Count("dispose:CustomersProbe"), Is.EqualTo(0));
    }

    [Test]
    public async Task Router_level_fragments_receive_the_error_and_can_retry()
    {
        var failing = true;
        var cut = Mount(
            map => map
                .Route("/", typeof(HomeProbe), name: "home")
                .Route("/customers", typeof(CustomersProbe), name: "customers", children: c => c
                    .Route("{id:int}", typeof(DetailsProbe), name: "customer").Loader<CustomerLoader>()),
            parameters: p =>
            {
                p.Add(r => r.Error, error => b => b.AddMarkupContent(0, $"<p id=\"router-error\">{error.Kind} at {error.Level}</p>"));
                p.Add(r => r.NotAuthorized, error => b => b.AddMarkupContent(0, "<p id=\"router-na\">no</p>"));
            });
        _loaders.Set("customer", _ => failing ? throw new InvalidOperationException("boom") : ValueTask.FromResult(RouteLoadResult.Create("ok")));

        await GoAsync("customers/5");

        Assert.That(cut.Find("#customers #router-error").TextContent, Is.EqualTo("LoadFailed at 1"));

        failing = false;
        await Store.Current!.Error!.Retry();

        Assert.That(cut.FindAll("#router-error"), Is.Empty);
        Assert.That(cut.Find("#details"), Is.Not.Null);
    }

    [Test]
    public async Task An_outlets_own_fragments_beat_the_routers()
    {
        var cut = Mount(
            map => map
                .Route("/", typeof(HomeProbe), name: "home")
                .Route("/customers", typeof(CustomersWithOwnErrorsProbe), name: "customers", children: c => c
                    .Route("{id:int}", typeof(DetailsProbe), name: "customer").Guard<AdminGuard>()
                    .Route("{id:int}/edit", typeof(ShowProbe), name: "edit").Guard<AuditGuard>()),
            parameters: p =>
            {
                p.Add(r => r.NotAuthorized, error => b => b.AddMarkupContent(0, "<p id=\"router-na\">router</p>"));
                p.Add(r => r.Error, error => b => b.AddMarkupContent(0, "<p id=\"router-error\">router</p>"));
            });
        _guards.Set("admin", RouteGuardResult.NotAuthorized("nope"));
        _guards.Set("audit", _ => throw new InvalidOperationException("audit down"));

        await GoAsync("customers/5");

        Assert.That(cut.Find("#customers #own-na").TextContent, Is.EqualTo("own: nope"));
        Assert.That(cut.FindAll("#router-na"), Is.Empty);

        await GoAsync("customers/5/edit");

        Assert.That(cut.Find("#customers #own-error").TextContent, Is.EqualTo("own error: GuardFailed"));
        Assert.That(cut.FindAll("#router-error"), Is.Empty);
    }

    [Test]
    public async Task Without_any_fragment_the_default_is_an_alert_with_the_message()
    {
        var cut = Mount(map => map
            .Route("/", typeof(HomeProbe), name: "home")
            .Route("/secret", typeof(ShowProbe), name: "secret").Guard<AuthGuard>());
        _guards.Set("auth", RouteGuardResult.NotAuthorized());

        await GoAsync("secret");

        var alert = cut.Find("#layout [role=alert]");
        Assert.That(alert.TextContent, Is.Not.Empty);
        Assert.That(cut.FindAll("#show"), Is.Empty);
    }

    [Test]
    public async Task A_not_authorized_error_leaves_when_the_user_navigates_away()
    {
        var cut = Mount(map => map
            .Route("/", typeof(HomeProbe), name: "home")
            .Route("/secret", typeof(ShowProbe), name: "secret").Guard<AuthGuard>());
        _guards.Set("auth", RouteGuardResult.NotAuthorized());
        await GoAsync("secret");

        await GoAsync("/");

        Assert.That(cut.FindAll("[role=alert]"), Is.Empty);
        Assert.That(cut.Find("#home"), Is.Not.Null);
    }

    // ---- router-level errors -----------------------------------------------------------------------------------

    [Test]
    public async Task A_redirect_loop_renders_the_router_error_inside_the_layout()
    {
        var cut = Mount(
            map => map
                .Route("/", typeof(HomeProbe), name: "home")
                .Route("/a", typeof(ShowProbe), name: "a").Guard<AuthGuard>()
                .Route("/b", typeof(ShowProbe), name: "b").Guard<AuditGuard>(),
            parameters: p => p.Add(r => r.Error, error => b => b.AddMarkupContent(0, $"<p id=\"router-error\">{error.Kind}</p>")));
        _guards.Set("auth", RouteGuardResult.Redirect("b"));
        _guards.Set("audit", RouteGuardResult.Redirect("a"));

        await GoAsync("a");

        Assert.That(cut.Find("#layout #router-error").TextContent, Is.EqualTo("RedirectLoop"));
        Assert.That(cut.FindAll("#show"), Is.Empty);
    }

    [Test]
    public void A_guard_failure_on_the_first_navigation_renders_the_error_in_the_layout()
    {
        _guards.Set("auth", _ => throw new InvalidOperationException("auth down"));

        var cut = Mount(map => map.Route("/", typeof(HomeProbe), name: "home").Guard<AuthGuard>());

        Assert.That(cut.Find("#layout [role=alert]").TextContent, Does.Contain("auth down"));
        Assert.That(Store.Current!.Error!.Kind, Is.EqualTo(RouteErrorKind.GuardFailed));
        Assert.That(cut.FindAll("#home"), Is.Empty);
    }

    [Test]
    public void A_failed_first_navigation_renders_the_default_error_instead_of_nothing()
    {
        _ctx.Services.AddSingleton<INavigationStage>(new ThrowingStage());

        var cut = Mount(map => map.Route("/", typeof(HomeProbe), name: "home"));

        Assert.That(cut.Find("#layout [role=alert]").GetAttribute("data-mod-route-error"), Is.EqualTo("NavigationFailed"));
        Assert.That(Store.Current!.Failure, Is.EqualTo(RouteFailure.NavigationFailed));
        Assert.That(cut.FindAll("#home"), Is.Empty);
    }

    sealed class ThrowingStage : INavigationStage
    {
        public ValueTask<StageResult> RunAsync(RouteNavigationContext context) => throw new InvalidOperationException("stage broke");
    }

    [Test]
    public async Task A_guard_redirect_renders_the_login_page_and_keeps_history_clean()
    {
        var cut = Mount(map => map
            .Route("/", typeof(HomeProbe), name: "home")
            .Route("/login", typeof(ShowProbe), name: "login")
            .Route("/secret", typeof(OrderProbe), name: "secret").Guard<AuthGuard>());
        _guards.Set("auth", RouteGuardResult.RedirectToRoute("login"));

        await GoAsync("secret");

        Assert.That(cut.Find("#layout #show"), Is.Not.Null);
        Assert.That(_adapter.History, Is.EqualTo(new[] { "http://localhost/", "http://localhost/login" }));
    }

    // ---- route data --------------------------------------------------------------------------------------------

    [Test]
    public async Task Route_data_gives_a_component_its_own_and_its_ancestors_data()
    {
        var cut = Mount(map => map
            .Route("/", typeof(HomeProbe), name: "home")
            .Route("/customers", typeof(DataProbe), name: "customers", children: c => c
                .Route("{id:int}", typeof(DataProbe), name: "customer").Loader<CustomerLoader>())
            .Loader<CustomersLoader>());
        _loaders.Returns("customers", "all customers");
        _loaders.Returns("customer", "customer 5");

        await GoAsync("customers/5");

        var data = cut.FindAll("#data").Select(e => e.TextContent).ToArray();
        var ancestor = cut.FindAll("#ancestor").Select(e => e.TextContent).ToArray();
        Assert.That(data, Is.EqualTo(new[] { "all customers", "customer 5" }));
        Assert.That(ancestor, Is.EqualTo(new[] { string.Empty, "all customers" }));
    }

    [Test]
    public async Task Deferred_values_and_streams_fill_their_slots_after_the_page_rendered()
    {
        var slow = new TaskCompletionSource<int>();
        var feed = new TaskCompletionSource();
        _loaders.Set("customers", _ => ValueTask.FromResult(RouteLoadResult.Create("c")
            .Defer("slow", slow.Task)
            .Stream("feed", Feed(feed))));
        var cut = Mount(map => map
            .Route("/", typeof(HomeProbe), name: "home")
            .Route("/page", typeof(SlotsProbe), name: "page").Loader<CustomersLoader>());

        await GoAsync("page");

        Assert.That(One(cut, "#slow-fallback").TextContent, Is.EqualTo("waiting"));
        await EventuallyAsync(() => All(cut, ".feed-item").Count, Is.EqualTo(2));
        Assert.That(All(cut, "#feed-fallback"), Is.Empty);

        slow.SetResult(42);
        await EventuallyAsync(() => All(cut, "#slow-value").Count, Is.EqualTo(1));

        Assert.That(One(cut, "#slow-value").TextContent, Is.EqualTo("42"));
        Assert.That(All(cut, "#slow-fallback"), Is.Empty);

        feed.SetResult();
    }

    [Test]
    public async Task Leaving_a_page_with_named_slots_does_not_throw_while_it_is_being_replaced()
    {
        // A cascading value reaches a component that is about to be removed before it is removed: the slots page sees
        // the next route's data, which has no "slow" or "feed". Found by the browser run (an unhandled render exception).
        _loaders.Set("customers", _ => ValueTask.FromResult(RouteLoadResult.Create("c").Defer("slow", Task.FromResult(1))));
        var cut = Mount(map => map
            .Route("/", typeof(HomeProbe), name: "home")
            .Route("/page", typeof(SlotsProbe), name: "page").Loader<CustomersLoader>()
            .Route("/other", typeof(DataProbe), name: "other").Loader<CustomerLoader>());
        await GoAsync("page");
        await EventuallyAsync(() => All(cut, "#slow-value").Count, Is.EqualTo(1));

        await GoAsync("other");
        await GoAsync("/");

        Assert.That(All(cut, "#home"), Has.Count.EqualTo(1));
        Assert.That(All(cut, "#slow-value"), Is.Empty);
    }

    [Test]
    public async Task A_slot_name_the_route_does_not_provide_renders_the_fallback()
    {
        var cut = Mount(map => map
            .Route("/", typeof(HomeProbe), name: "home")
            .Route("/page", typeof(SlotsProbe), name: "page").Loader<CustomersLoader>());

        await GoAsync("page");

        Assert.That(One(cut, "#slow-fallback").TextContent, Is.EqualTo("waiting"));
        Assert.That(One(cut, "#feed-fallback").TextContent, Is.EqualTo("none yet"));
    }

    static async IAsyncEnumerable<int> Feed(TaskCompletionSource end)
    {
        yield return 1;
        yield return 2;
        await end.Task;
    }

    [Test]
    public async Task Disposing_the_router_cancels_the_levels_it_loaded()
    {
        var lifetimes = new List<CancellationToken>();
        _loaders.Set("customers", context =>
        {
            lifetimes.Add(context.LifetimeToken);

            return ValueTask.FromResult(RouteLoadResult.Create("c"));
        });
        var cut = Mount(Routes);
        await GoAsync("customers");

        Assert.That(lifetimes.Single().IsCancellationRequested, Is.False);

        await _ctx.DisposeComponentsAsync();

        Assert.That(lifetimes.Single().IsCancellationRequested, Is.True);
    }

    [Test]
    public async Task A_throwing_subscriber_does_not_stop_the_router_rendering()
    {
        var cut = Mount(Routes);
        Store.Changed += _ => throw new InvalidOperationException("bad subscriber");

        await GoAsync("customers/5");

        Assert.That(cut.Find("#customers #details"), Is.Not.Null);
    }
}
