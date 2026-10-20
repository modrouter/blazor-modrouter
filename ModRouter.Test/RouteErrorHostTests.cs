using Bunit;
using ModRouter.Components;
using ModRouter.Core.Definitions;
using ModRouter.Core.Guards;
using ModRouter.Navigation;
using Microsoft.Extensions.DependencyInjection;
using static ModRouter.Test.ComponentTestKit;

namespace ModRouter.Test;

/// <summary>
/// Every place <c>ModRouter</c> renders error content wraps it in <c>&lt;div data-mod-route-error-host style="display:contents"&gt;</c>:
/// the browser module's focus fallback looks for an alert only inside that host, so a danger message in a layout is never
/// chosen. The outlet's own fragments, the router's fragments, the built-in alert, chain-level and router-level errors,
/// and a failed first navigation all go through the same wrapper.
/// </summary>
[TestFixture]
public sealed class RouteErrorHostTests
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

    static void CustomerTree(RouteMap map) => map
        .Route("/", typeof(HomeProbe), name: "home")
        .Route("/customers", typeof(CustomersProbe), name: "customers", children: c => c
            .Route("{id:int}", typeof(DetailsProbe), name: "customer").Guard<AdminGuard>());

    // ---- chain-level errors (the outlet of the failed level) ---------------------------------------------------

    [Test]
    public async Task The_default_alert_of_a_chain_level_error_is_inside_the_host_inside_the_parent()
    {
        var cut = Mount(CustomerTree);
        _guards.Set("admin", RouteGuardResult.NotAuthorized("Admins only"));

        await GoAsync("customers/5");

        var alert = One(cut, "#customers [role=alert]");
        Assert.That(alert.TextContent, Is.EqualTo("Admins only"));
        AssertInErrorHost(alert);
        Assert.That(All(cut, "[data-mod-route-error-host]"), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task The_routers_NotAuthorized_fragment_is_inside_the_host_and_keeps_its_own_alert()
    {
        var cut = Mount(CustomerTree, parameters: p => p.Add(r => r.NotAuthorized, error => b => b.AddMarkupContent(0, $"<p id=\"router-na\" role=\"alert\">{error.Message}</p>")));
        _guards.Set("admin", RouteGuardResult.NotAuthorized("Admins only"));

        await GoAsync("customers/5");

        var alert = One(cut, "#customers #router-na");
        Assert.That(alert.GetAttribute("role"), Is.EqualTo("alert"), "the application's markup is not changed, only wrapped");
        AssertInErrorHost(alert);
    }

    [Test]
    public async Task The_routers_Error_fragment_for_a_failed_loader_is_inside_the_host()
    {
        var cut = Mount(
            map => map
                .Route("/", typeof(HomeProbe), name: "home")
                .Route("/customers", typeof(CustomersProbe), name: "customers", children: c => c
                    .Route("{id:int}", typeof(DetailsProbe), name: "customer").Loader<CustomerLoader>()),
            parameters: p => p.Add(r => r.Error, error => b => b.AddMarkupContent(0, $"<p id=\"router-error\" role=\"alert\">{error.Kind}</p>")));
        _loaders.Set("customer", _ => throw new InvalidOperationException("boom"));

        await GoAsync("customers/5");

        AssertInErrorHost(One(cut, "#customers #router-error"));
    }

    [Test]
    public async Task An_outlets_own_fragments_are_inside_the_host()
    {
        var cut = Mount(map => map
            .Route("/", typeof(HomeProbe), name: "home")
            .Route("/customers", typeof(CustomersWithOwnErrorsProbe), name: "customers", children: c => c
                .Route("{id:int}", typeof(DetailsProbe), name: "customer").Guard<AdminGuard>()
                .Route("{id:int}/edit", typeof(ShowProbe), name: "edit").Guard<AuditGuard>()));
        _guards.Set("admin", RouteGuardResult.NotAuthorized("nope"));
        _guards.Set("audit", _ => throw new InvalidOperationException("audit down"));

        await GoAsync("customers/5");
        AssertInErrorHost(One(cut, "#customers #own-na"));

        await GoAsync("customers/5/edit");
        AssertInErrorHost(One(cut, "#customers #own-error"));
        Assert.That(All(cut, "[data-mod-route-error-host]"), Has.Count.EqualTo(1), "one error at a time, one host");
    }

    // ---- router-level errors (inside the layout) ---------------------------------------------------------------

    static void LoopRoutes(RouteMap map) => map
        .Route("/", typeof(HomeProbe), name: "home")
        .Route("/a", typeof(ShowProbe), name: "a").Guard<AuthGuard>()
        .Route("/b", typeof(ShowProbe), name: "b").Guard<AuditGuard>();

    void LoopGuards()
    {
        _guards.Set("auth", RouteGuardResult.Redirect("b"));
        _guards.Set("audit", RouteGuardResult.Redirect("a"));
    }

    [Test]
    public async Task The_default_alert_of_a_router_level_error_is_inside_the_host_inside_the_layout()
    {
        var cut = Mount(LoopRoutes);
        LoopGuards();

        await GoAsync("a");

        var alert = One(cut, "#layout [role=alert]");
        Assert.That(alert.GetAttribute("data-mod-route-error"), Is.EqualTo(nameof(RouteErrorKind.RedirectLoop)));
        AssertInErrorHost(alert);
    }

    [Test]
    public async Task The_routers_Error_fragment_for_a_router_level_error_is_inside_the_host()
    {
        var cut = Mount(LoopRoutes, parameters: p => p.Add(r => r.Error, error => b => b.AddMarkupContent(0, $"<p id=\"router-error\" role=\"alert\">{error.Kind}</p>")));
        LoopGuards();

        await GoAsync("a");

        AssertInErrorHost(One(cut, "#layout #router-error"));
    }

    [Test]
    public void A_failed_first_navigation_is_inside_the_host()
    {
        _guards.Set("auth", _ => throw new InvalidOperationException("auth down"));

        var cut = Mount(map => map.Route("/", typeof(HomeProbe), name: "home").Guard<AuthGuard>());

        var alert = One(cut, "#layout [role=alert]");
        Assert.That(alert.TextContent, Does.Contain("auth down"));
        AssertInErrorHost(alert);
    }

    [Test]
    public async Task The_host_leaves_with_the_error()
    {
        var cut = Mount(LoopRoutes);
        LoopGuards();
        await GoAsync("a");
        Assert.That(All(cut, "[data-mod-route-error-host]"), Has.Count.EqualTo(1));

        await GoAsync("/");

        Assert.That(All(cut, "[data-mod-route-error-host]"), Is.Empty);
        Assert.That(All(cut, "[role=alert]"), Is.Empty);
    }
}
