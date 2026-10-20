using ModRouter.Core.Compilation;
using ModRouter.Core.Guards;
using ModRouter.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace ModRouter.Test;

[TestFixture]
public sealed class StateTitleAndOptionsTests
{
    const string Base = "http://localhost/";

    static CompiledRouteTable TitledTable() => RouteTestKit.Compile(map => map
        .Route("/", typeof(HomePage), name: "home", title: "Home")
        .Route("/untitled", typeof(OtherPage), name: "untitled")
        .Route("/secret", typeof(OtherPage), name: "secret", title: "Secret").Guard<AuthGuard>()
        .Route("/old", typeof(OtherPage), name: "old").Guard<AuditGuard>()
        .Route("/customers", typeof(CustomersPage), name: "customers", title: "Customers", children: customers => customers
            .Index(typeof(CustomerList), name: "customer-list", title: "All customers")
            .Route("{id:int}", typeof(CustomerDetails), p => $"Customer {p["id"]}", name: "customer")));

    static async Task<Pipeline> StartAsync(Action<ModRouterOptions>? configure = null, string uri = Base, IEnumerable<INavigationStage>? stages = null)
    {
        var pipeline = Pipeline.Create(TitledTable(), uri: uri, configure: configure, stages: stages);
        await pipeline.Coordinator.NavigateInitialAsync();

        return pipeline;
    }

    // ---- state title -------------------------------------------------------------------------------------------

    [Test]
    public async Task The_state_title_is_the_leaf_route_title_through_the_format()
    {
        using var p = await StartAsync(o => o.Title.Format = "{0} - Acme");

        Assert.That(p.Current.Title, Is.EqualTo("Home - Acme"));

        await p.GoAsync("customers/42");

        Assert.That(p.Current.Title, Is.EqualTo("Customer 42 - Acme"));
    }

    [Test]
    public async Task An_untitled_index_route_takes_the_title_of_its_parent()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/customers", typeof(CustomersPage), name: "customers", title: "Customers", children: customers => customers
                .Index(typeof(CustomerList), name: "customer-list")));
        using var p = Pipeline.Create(table);
        await p.Coordinator.NavigateInitialAsync();

        await p.GoAsync("customers");

        Assert.That(p.Current.Title, Is.EqualTo("Customers"));
    }

    [Test]
    public async Task An_index_route_uses_its_own_title()
    {
        using var p = await StartAsync();

        await p.GoAsync("customers");

        Assert.That(p.Current.Title, Is.EqualTo("All customers"));
    }

    [Test]
    public async Task An_untitled_route_uses_the_default_title_unformatted_or_leaves_the_title_alone()
    {
        using var without = await StartAsync();
        await without.GoAsync("untitled");

        Assert.That(without.Current.Title, Is.Null);

        using var with = await StartAsync(o =>
        {
            o.Title.Format = "{0} - Acme";
            o.Title.Default = "Acme";
        });
        await with.GoAsync("untitled");

        Assert.That(with.Current.Title, Is.EqualTo("Acme"));
    }

    [Test]
    public async Task A_url_that_matches_nothing_gets_the_not_found_title()
    {
        using var p = await StartAsync(o => o.Title.Format = "{0} - Acme");

        await p.GoAsync("nope");

        Assert.That(p.Current.Failure, Is.EqualTo(RouteFailure.NotFound));
        Assert.That(p.Current.Title, Is.EqualTo("Not found"), "the not-found title is complete: the format is not applied");
    }

    [Test]
    public async Task The_not_found_title_is_configurable()
    {
        using var p = await StartAsync(o => o.Title.NotFound = "Nothing here");

        await p.GoAsync("nope");

        Assert.That(p.Current.Title, Is.EqualTo("Nothing here"));
    }

    [Test]
    public async Task A_chain_level_error_keeps_the_destination_title()
    {
        using var p = await StartAsync();
        p.Guards.Set("auth", RouteGuardResult.NotAuthorized());

        await p.GoAsync("secret");

        Assert.That(p.Current.Error!.Kind, Is.EqualTo(RouteErrorKind.NotAuthorized));
        Assert.That(p.Current.Title, Is.EqualTo("Secret"));
    }

    [Test]
    public async Task A_redirect_loop_uses_the_default_title_or_none()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/", typeof(HomePage), name: "home", title: "Home")
            .Route("/a", typeof(OtherPage), name: "a", title: "A").Guard<AuthGuard>()
            .Route("/b", typeof(OtherPage), name: "b", title: "B").Guard<AuditGuard>());
        using var p = Pipeline.Create(table, configure: o => o.Title.Default = "Acme");
        await p.Coordinator.NavigateInitialAsync();
        p.Guards.Set("auth", RouteGuardResult.Redirect("b"));
        p.Guards.Set("audit", RouteGuardResult.Redirect("a"));

        await p.GoAsync("a");

        Assert.That(p.Current.Failure, Is.EqualTo(RouteFailure.RedirectLoop));
        Assert.That(p.Current.Title, Is.EqualTo("Acme"));
    }

    [Test]
    public async Task A_failed_first_navigation_uses_the_default_title_or_none()
    {
        var stage = new FailingStage();
        using var p = Pipeline.Create(TitledTable(), configure: o => o.Title.Default = "Acme", stages: [stage]);

        await p.Coordinator.NavigateInitialAsync();

        Assert.That(p.Current.Failure, Is.EqualTo(RouteFailure.NavigationFailed));
        Assert.That(p.Current.Title, Is.EqualTo("Acme"));
    }

    sealed class FailingStage : INavigationStage
    {
        public ValueTask<StageResult> RunAsync(RouteNavigationContext context) => throw new InvalidOperationException("stage broke");
    }

    [Test]
    public async Task The_title_survives_a_reload()
    {
        using var p = await StartAsync();
        await p.GoAsync("customers/5");

        await p.Coordinator.ReloadAsync();

        Assert.That(p.Current.Title, Is.EqualTo("Customer 5"));
        Assert.That(p.Current.Trigger, Is.EqualTo(NavigationTrigger.Reload));
    }

    [Test]
    public async Task The_navigation_service_exposes_the_current_title()
    {
        var table = TitledTable();
        using var p = Pipeline.Create(table);
        using var service = new NavigationService(p.Adapter, RouteTestKit.Frozen(table), p.Store);

        Assert.That(service.Title, Is.Null, "nothing committed yet");

        await p.Coordinator.NavigateInitialAsync();
        await p.GoAsync("customers/9");

        Assert.That(service.Title, Is.EqualTo("Customer 9"));
    }

    [Test]
    public void The_navigation_service_exposes_the_base_uri_of_the_platform()
    {
        var table = TitledTable();
        using var p = Pipeline.Create(table, baseUri: "http://localhost/myapp/", attach: false);
        using var service = new NavigationService(p.Adapter, RouteTestKit.Frozen(table), p.Store);

        Assert.That(service.BaseUri, Is.EqualTo("http://localhost/myapp/"));
    }

    // ---- per-navigation options --------------------------------------------------------------------------------

    [Test]
    public async Task Options_given_to_NavigateTo_reach_the_committed_state()
    {
        var table = TitledTable();
        using var p = Pipeline.Create(table);
        using var service = new NavigationService(p.Adapter, RouteTestKit.Frozen(table), p.Store);
        await p.Coordinator.NavigateInitialAsync();
        var options = new ModNavigationOptions { Focus = false, Announce = false };

        service.NavigateTo("customers/3", options);
        await p.Adapter.SettledAsync();

        Assert.That(p.Current.Path, Is.EqualTo("/customers/3"));
        Assert.That(p.Current.Options, Is.SameAs(options));
    }

    [Test]
    public async Task Options_are_used_once_and_do_not_leak_to_later_navigations()
    {
        var table = TitledTable();
        using var p = Pipeline.Create(table);
        using var service = new NavigationService(p.Adapter, RouteTestKit.Frozen(table), p.Store);
        await p.Coordinator.NavigateInitialAsync();

        service.NavigateTo("customers/3", new ModNavigationOptions { Scroll = false });
        await p.Adapter.SettledAsync();
        await p.GoAsync("customers/4");

        Assert.That(p.Current.Options, Is.Null);
    }

    [Test]
    public async Task Options_for_another_url_are_dropped_not_applied_to_whatever_navigates_next()
    {
        var table = TitledTable();
        using var p = Pipeline.Create(table);
        using var service = new NavigationService(p.Adapter, RouteTestKit.Frozen(table), p.Store);
        await p.Coordinator.NavigateInitialAsync();
        p.Guards.Set("auth", RouteGuardResult.Cancel());

        service.NavigateTo("secret", new ModNavigationOptions { Focus = false });
        await p.Adapter.SettledAsync();
        await p.GoAsync("customers/4");

        Assert.That(p.Current.Path, Is.EqualTo("/customers/4"));
        Assert.That(p.Current.Options, Is.Null);
    }

    [Test]
    public async Task The_options_carry_history_and_force_load_choices_to_the_platform()
    {
        var table = TitledTable();
        using var p = Pipeline.Create(table);
        using var service = new NavigationService(p.Adapter, RouteTestKit.Frozen(table), p.Store);
        await p.Coordinator.NavigateInitialAsync();

        service.NavigateTo("customers/3", new ModNavigationOptions { ReplaceHistory = true });
        await p.Adapter.SettledAsync();

        Assert.That(p.Adapter.History, Is.EqualTo(new[] { Base + "customers/3" }));

        service.NavigateTo("https://elsewhere.example/", new ModNavigationOptions { ForceLoad = true });

        Assert.That(p.Adapter.NavigateCalls.Last().ForceLoad, Is.True);
    }

    [Test]
    public async Task Options_armed_for_the_current_url_are_used_up_by_that_navigation_even_though_it_goes_nowhere()
    {
        var table = TitledTable();
        using var p = Pipeline.Create(table);
        using var service = new NavigationService(p.Adapter, RouteTestKit.Frozen(table), p.Store);
        await p.Coordinator.NavigateInitialAsync();
        await p.GoAsync("old");

        // The current URL: the handler lets it through without a navigation, so nothing commits these options.
        service.NavigateTo("old", new ModNavigationOptions { Focus = false });
        await p.Adapter.SettledAsync();
        Assert.That(p.Current.Options, Is.Null);

        // A retry whose guard now redirects reaches the handler as a redirect, which never takes options itself.
        p.Guards.Set("audit", RouteGuardResult.Redirect("customers/8"));
        await p.Coordinator.ReloadAsync();
        await p.Adapter.SettledAsync();
        Assert.That(p.Current.Path, Is.EqualTo("/customers/8"));
        p.Guards.Set("audit", RouteGuardResult.Allow());

        await p.GoAsync("old");

        Assert.That(p.Current.Path, Is.EqualTo("/old"));
        Assert.That(p.Current.Options, Is.Null, "the options of a navigation that went nowhere must not reach a later navigation to the same URL");
    }

    [Test]
    public async Task A_fragment_in_the_url_does_not_stop_the_options_from_matching()
    {
        var table = TitledTable();
        using var p = Pipeline.Create(table);
        using var service = new NavigationService(p.Adapter, RouteTestKit.Frozen(table), p.Store);
        await p.Coordinator.NavigateInitialAsync();
        var options = new ModNavigationOptions { Scroll = false };

        service.NavigateTo("customers/3#notes", options);
        await p.Adapter.SettledAsync();

        Assert.That(p.Current.Options, Is.SameAs(options));
        Assert.That(p.Current.Fragment, Is.EqualTo("notes"));
    }

    [Test]
    public async Task A_redirect_does_not_carry_the_options_of_the_navigation_that_was_redirected()
    {
        var table = TitledTable();
        using var p = Pipeline.Create(table);
        using var service = new NavigationService(p.Adapter, RouteTestKit.Frozen(table), p.Store);
        await p.Coordinator.NavigateInitialAsync();
        p.Guards.Set("audit", RouteGuardResult.Redirect("customers/8"));

        service.NavigateTo("old", new ModNavigationOptions { Focus = false });
        await p.Adapter.SettledAsync();

        Assert.That(p.Current.Path, Is.EqualTo("/customers/8"));
        Assert.That(p.Current.Trigger, Is.EqualTo(NavigationTrigger.Redirect));
        Assert.That(p.Current.Options, Is.Null);
    }

    [Test]
    public void NavigateTo_with_options_rejects_null_options()
    {
        var table = TitledTable();
        using var p = Pipeline.Create(table, attach: false);
        using var service = new NavigationService(p.Adapter, RouteTestKit.Frozen(table), p.Store);

        Assert.Throws<ArgumentNullException>(() => service.NavigateTo("x", (ModNavigationOptions)null!));
    }

    // ---- options validation ------------------------------------------------------------------------------------

    [Test]
    public void An_invalid_title_format_fails_at_startup()
    {
        var exception = Assert.Throws<ArgumentException>(() => new ServiceCollection().AddModRouter(
            map => map.Route("/", typeof(HomePage)),
            o => o.Title.Format = "{0"))!;

        Assert.That(exception.Message, Does.Contain("{0"));
    }

    [Test]
    public void A_negative_focus_timeout_fails_at_startup()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddModRouter(
            map => map.Route("/", typeof(HomePage)),
            o => o.Focus.Timeout = TimeSpan.FromSeconds(-1)));
    }

    [Test]
    public void The_defaults_are_the_documented_ones()
    {
        var options = new ModRouterOptions();

        Assert.That(options.Title.Format, Is.EqualTo("{0}"));
        Assert.That(options.Title.Default, Is.Null);
        Assert.That(options.Title.NotFound, Is.EqualTo("Not found"));
        Assert.That(options.Announce.Enabled, Is.True);
        Assert.That((options.Focus.Enabled, options.Focus.Selector, options.Focus.Timeout), Is.EqualTo((true, "h1", TimeSpan.FromSeconds(1))));
        Assert.That((options.Scroll.Enabled, options.Scroll.Container, options.Scroll.Restore), Is.EqualTo((true, (string?)null, true)));
    }
}
