using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Guards;
using ModRouter.Navigation;

namespace ModRouter.Test;

[TestFixture]
public sealed class RedirectPipelineTests
{
    const string Base = "http://localhost/";

    static CompiledRouteTable RuleTable(bool preserveQuery = true, bool preserveFragment = true) => RouteTestKit.Compile(map => map
        .Route("/", typeof(HomePage), name: "home")
        .Route("/customers/{id:int}", typeof(CustomerDetails), name: "customer")
        .Redirect("/clients/{id:int}", "customer", preserveQuery, preserveFragment));

    static CompiledRouteTable AliasTable(bool canonical) => RouteTestKit.Compile(map =>
    {
        map.Route("/", typeof(HomePage), name: "home");
        map.Route("/customers/{id:int}", typeof(CustomerDetails), name: "customer", aliases: ["/clients/{id:int}"]);

        if (canonical)
        {
            map.CanonicalAlias();
        }
    });

    static async Task<Pipeline> StartAsync(CompiledRouteTable table, Action<ModRouterOptions>? configure = null)
    {
        var pipeline = Pipeline.Create(table, configure: configure);
        await pipeline.Coordinator.NavigateInitialAsync();

        return pipeline;
    }

    [Test]
    public async Task A_redirect_rule_forwards_parameters_query_and_fragment()
    {
        using var p = await StartAsync(RuleTable());

        await p.GoAsync("clients/7?tab=a#top");

        Assert.That(p.Current.Path, Is.EqualTo("/customers/7"));
        Assert.That(p.Current.Query, Is.EqualTo("?tab=a"));
        Assert.That(p.Current.Fragment, Is.EqualTo("top"));
        Assert.That(p.Current.Trigger, Is.EqualTo(NavigationTrigger.Redirect));
        Assert.That(p.Current.RedirectedFrom, Is.EqualTo(new[] { Base + "clients/7?tab=a" }));
        Assert.That(p.Adapter.History, Is.EqualTo(new[] { Base, Base + "customers/7?tab=a#top" }));
    }

    [Test]
    public async Task A_navigation_exception_from_the_platform_redirect_reaches_the_framework_instead_of_failing_the_navigation()
    {
        using var p = await StartAsync(RuleTable());
        p.Adapter.NavigateToFailure = new Microsoft.AspNetCore.Components.NavigationException(Base + "customers/7");

        // A static server render ends the request by throwing from NavigateTo; the framework turns that into the HTTP redirect.
        Assert.ThrowsAsync<Microsoft.AspNetCore.Components.NavigationException>(() => p.GoAsync("clients/7"));
        Assert.That(p.Adapter.NavigateCalls.Select(call => call.Uri), Has.Some.EndsWith("customers/7"));
    }

    [Test]
    public async Task A_redirect_rule_can_drop_the_query_and_fragment()
    {
        using var p = await StartAsync(RuleTable(preserveQuery: false, preserveFragment: false));

        await p.GoAsync("clients/7?tab=a#top");

        Assert.That(p.Current.Path, Is.EqualTo("/customers/7"));
        Assert.That(p.Current.Query, Is.Empty);
        Assert.That(p.Current.Fragment, Is.Null);
    }

    [Test]
    public async Task The_redirecting_url_never_enters_history_and_never_commits()
    {
        using var p = await StartAsync(RuleTable());

        await p.GoAsync("clients/7");

        Assert.That(p.Commits.Select(c => c.Path), Is.EqualTo(new[] { "/", "/customers/7" }));
        Assert.That(p.Adapter.History, Has.None.Contain("clients"));
    }

    [Test]
    public async Task A_redirect_rule_on_the_initial_load_replaces_the_entry()
    {
        var pipeline = Pipeline.Create(RuleTable(), uri: Base + "clients/3");
        using var p = pipeline;

        var outcome = await p.Coordinator.NavigateInitialAsync();
        await p.Adapter.SettledAsync();

        Assert.That(outcome, Is.EqualTo(NavigationOutcome.Redirected));
        Assert.That(p.Current.Path, Is.EqualTo("/customers/3"));
        Assert.That(p.Adapter.History, Is.EqualTo(new[] { Base + "customers/3" }));
    }

    [Test]
    public async Task An_alias_renders_in_place_by_default()
    {
        using var p = await StartAsync(AliasTable(canonical: false));

        await p.GoAsync("clients/7");

        Assert.That(p.Current.Path, Is.EqualTo("/clients/7"));
        Assert.That(p.Current.Chain!.MatchedViaAlias, Is.True);
        Assert.That(p.Current.RedirectedFrom, Is.Empty);
    }

    [Test]
    public async Task A_canonical_alias_redirects_to_the_canonical_url()
    {
        using var p = await StartAsync(AliasTable(canonical: true));

        await p.GoAsync("clients/7?x=1");

        Assert.That(p.Current.Path, Is.EqualTo("/customers/7"));
        Assert.That(p.Current.Query, Is.EqualTo("?x=1"));
        Assert.That(p.Current.Chain!.MatchedViaAlias, Is.False);
        Assert.That(p.Current.Trigger, Is.EqualTo(NavigationTrigger.Redirect));
    }

    [Test]
    public async Task The_router_wide_alias_option_redirects_every_alias()
    {
        using var p = await StartAsync(AliasTable(canonical: false), options => options.AliasBehavior = AliasBehavior.RedirectToCanonical);

        await p.GoAsync("clients/7");

        Assert.That(p.Current.Path, Is.EqualTo("/customers/7"));
    }

    [Test]
    public async Task The_router_wide_alias_option_applies_to_routes_that_declare_no_behavior()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/customers/{id:int}", typeof(CustomerDetails), aliases: ["/clients/{id:int}"]));
        using var p = await StartAsync(table, options => options.AliasBehavior = AliasBehavior.RedirectToCanonical);

        await p.GoAsync("clients/7");

        Assert.That(p.Current.Path, Is.EqualTo("/customers/7"));
    }

    [Test]
    public async Task Redirects_that_never_settle_are_reported_as_a_loop()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/", typeof(HomePage), name: "home")
            .Route("/a", typeof(OtherPage), name: "a").Guard<AuthGuard>()
            .Route("/b", typeof(OtherPage), name: "b").Guard<AuditGuard>());
        using var p = await StartAsync(table);
        p.Guards.Set("auth", RouteGuardResult.Redirect("b"));
        p.Guards.Set("audit", RouteGuardResult.Redirect("a"));

        await p.GoAsync("a");

        Assert.That(p.Current.Failure, Is.EqualTo(RouteFailure.RedirectLoop));
        Assert.That(p.Current.Chain, Is.Null);
        Assert.That(p.Current.Error!.Kind, Is.EqualTo(RouteErrorKind.RedirectLoop));
        Assert.That(p.Current.Error.Level, Is.Null);
        Assert.That(p.Current.Error.Message, Does.Contain(Base + "a").And.Contain(Base + "b"));
        Assert.That(p.Guards.Calls, Is.EqualTo(new[] { "auth@a", "audit@b" }));
    }

    [Test]
    public async Task A_long_chain_of_distinct_redirects_stops_at_the_limit()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/", typeof(HomePage), name: "home")
            .Route("/hop/{n:int}", typeof(OtherPage), name: "hop").Guard<AuthGuard>());
        using var p = await StartAsync(table);
        p.Guards.Set("auth", context => ValueTask.FromResult(RouteGuardResult.RedirectToRoute("hop", new { n = context.Level.Parameters.Get<int>("n") + 1 })));

        await p.GoAsync("hop/0");

        Assert.That(p.Current.Failure, Is.EqualTo(RouteFailure.RedirectLoop));
        Assert.That(p.Guards.Calls, Has.Count.EqualTo(NavigationCoordinator.MaxRedirects + 1));
    }

    [Test]
    public async Task A_redirect_chain_reports_every_hop_in_order()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/", typeof(HomePage), name: "home")
            .Route("/a", typeof(OtherPage), name: "a").Guard<AuthGuard>()
            .Route("/b", typeof(OtherPage), name: "b").Guard<AuditGuard>()
            .Route("/c", typeof(OtherPage), name: "c"));
        using var p = await StartAsync(table);
        p.Guards.Set("auth", RouteGuardResult.Redirect("b"));
        p.Guards.Set("audit", RouteGuardResult.Redirect("c"));

        await p.GoAsync("a");

        Assert.That(p.Current.Path, Is.EqualTo("/c"));
        Assert.That(p.Current.RedirectedFrom, Is.EqualTo(new[] { Base + "a", Base + "b" }));
        Assert.That(p.Adapter.History, Is.EqualTo(new[] { Base, Base + "c" }));
    }

    [Test]
    public async Task A_redirect_chain_carries_the_trigger_of_the_navigation_that_started_it()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/", typeof(HomePage), name: "home")
            .Route("/a", typeof(OtherPage), name: "a").Guard<AuthGuard>()
            .Route("/b", typeof(OtherPage), name: "b").Guard<AuditGuard>()
            .Route("/c", typeof(OtherPage), name: "c"));
        using var p = await StartAsync(table);
        p.Guards.Set("auth", RouteGuardResult.Redirect("b"));
        p.Guards.Set("audit", RouteGuardResult.Redirect("c"));
        Assert.That(p.Current.RedirectOrigin, Is.Null, "a state that was not reached by a redirect has no origin");

        await p.GoAsync("a");

        Assert.That(p.Current.Path, Is.EqualTo("/c"));
        Assert.That(p.Current.Trigger, Is.EqualTo(NavigationTrigger.Redirect));
        Assert.That(p.Current.RedirectOrigin, Is.EqualTo(NavigationTrigger.Link), "the origin survives every hop of the chain");
    }

    [Test]
    public async Task A_redirect_chain_that_starts_at_the_initial_load_carries_that_origin()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/", typeof(HomePage), name: "home")
            .Route("/a", typeof(OtherPage), name: "a").Guard<AuthGuard>()
            .Route("/b", typeof(OtherPage), name: "b").Guard<AuditGuard>()
            .Route("/c", typeof(OtherPage), name: "c"));
        using var p = Pipeline.Create(table, uri: Base + "a");
        p.Guards.Set("auth", RouteGuardResult.Redirect("b"));
        p.Guards.Set("audit", RouteGuardResult.Redirect("c"));

        await p.Coordinator.NavigateInitialAsync();
        await p.Adapter.SettledAsync();

        Assert.That(p.Current.Path, Is.EqualTo("/c"));
        Assert.That(p.Current.Trigger, Is.EqualTo(NavigationTrigger.Redirect));
        Assert.That(p.Current.RedirectOrigin, Is.EqualTo(NavigationTrigger.InitialLoad));
        Assert.That(p.Adapter.History, Is.EqualTo(new[] { Base + "c" }), "the chain replaced the entry of the first load");
    }

    [Test]
    public async Task A_redirect_loop_state_carries_the_origin_too()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/", typeof(HomePage), name: "home")
            .Route("/a", typeof(OtherPage), name: "a").Guard<AuthGuard>()
            .Route("/b", typeof(OtherPage), name: "b").Guard<AuditGuard>());
        using var p = await StartAsync(table);
        p.Guards.Set("auth", RouteGuardResult.Redirect("b"));
        p.Guards.Set("audit", RouteGuardResult.Redirect("a"));

        await p.GoAsync("a");

        Assert.That(p.Current.Failure, Is.EqualTo(RouteFailure.RedirectLoop));
        Assert.That(p.Current.RedirectOrigin, Is.EqualTo(NavigationTrigger.Link));
    }

    [Test]
    public async Task A_redirect_is_pushed_on_top_of_the_existing_history()
    {
        using var p = await StartAsync(RuleTable());
        await p.GoAsync("customers/1");

        var allowed = await p.GoAsync("clients/2");

        Assert.That(allowed, Is.False);
        Assert.That(p.Adapter.History, Is.EqualTo(new[] { Base, Base + "customers/1", Base + "customers/2" }));
    }
}
