using ModRouter.Core.Compilation;
using ModRouter.Core.Guards;
using ModRouter.Navigation;

namespace ModRouter.Test;

[TestFixture]
public sealed class GuardPipelineTests
{
    const string Base = "http://localhost/";

    static CompiledRouteTable GuardedTable() => Pipeline.Table(map => map
        .Route("/customers", typeof(CustomersPage), name: "customers", children: customers => customers
            .Index(typeof(CustomerList), name: "customer-list")
            .Route("{id:int}", typeof(CustomerDetails), name: "customer", children: customer => customer
                .Route("orders", typeof(OrderDetails), name: "orders").Guard<AdminGuard>()))
        .Guard<AuthGuard>().Guard<AuditGuard>());

    static async Task<Pipeline> StartAsync(string uri = Base, bool initial = true)
    {
        var pipeline = Pipeline.Create(GuardedTable(), uri: uri);

        if (initial)
        {
            await pipeline.Coordinator.NavigateInitialAsync();
        }

        return pipeline;
    }

    [Test]
    public async Task Guards_run_parent_first_in_declaration_order()
    {
        using var p = await StartAsync();

        Assert.That(await p.GoAsync("customers/5/orders"), Is.True);

        Assert.That(p.Guards.Calls, Is.EqualTo(new[] { "auth@customers", "audit@customers", "admin@orders" }));
        Assert.That(p.Current.Path, Is.EqualTo("/customers/5/orders"));
        Assert.That(p.Current.Error, Is.Null);
    }

    [Test]
    public async Task A_level_without_guards_costs_nothing()
    {
        using var p = await StartAsync();

        await p.GoAsync("/");

        Assert.That(p.Guards.Calls, Is.Empty);
    }

    [Test]
    public async Task Guard_context_describes_the_navigation()
    {
        using var p = await StartAsync();

        await p.GoAsync("customers/5?tab=a");

        var context = p.Guards.Contexts.First();
        Assert.That(context.To, Is.EqualTo(Base + "customers/5?tab=a"));
        Assert.That(context.From, Is.EqualTo(Base));
        Assert.That(context.Query, Is.EqualTo("?tab=a"));
        Assert.That(context.IsInitialLoad, Is.False);
        Assert.That(context.Depth, Is.Zero);
        Assert.That(context.Level.RouteName, Is.EqualTo("customers"));
        Assert.That(context.Chain.Leaf.RouteName, Is.EqualTo("customer"));
        Assert.That(context.Services, Is.Not.Null);
    }

    [Test]
    public async Task First_navigation_is_flagged_as_the_initial_load()
    {
        using var p = await StartAsync(Base + "customers", initial: false);

        await p.Coordinator.NavigateInitialAsync();

        Assert.That(p.Guards.Contexts.First().IsInitialLoad, Is.True);
        Assert.That(p.Guards.Contexts.First().From, Is.Null);
    }

    [Test]
    public async Task Cancel_keeps_the_old_page_and_url()
    {
        using var p = await StartAsync();
        p.Guards.Set("audit", RouteGuardResult.Cancel());

        var allowed = await p.GoAsync("customers/5");

        Assert.That(allowed, Is.False);
        Assert.That(p.Current.Path, Is.EqualTo("/"));
        Assert.That(p.Adapter.Uri, Is.EqualTo(Base));
        Assert.That(p.Guards.Calls, Is.EqualTo(new[] { "auth@customers", "audit@customers" }));
        Assert.That(p.Commits, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Redirect_to_a_route_pushes_history_and_records_where_it_came_from()
    {
        using var p = await StartAsync();
        p.Guards.Set("auth", RouteGuardResult.RedirectToRoute("login", query: new Dictionary<string, object?> { ["returnUrl"] = "/customers/5" }));

        var allowed = await p.GoAsync("customers/5");

        Assert.That(allowed, Is.False);
        Assert.That(p.Current.Path, Is.EqualTo("/login"));
        Assert.That(p.Current.Query, Is.EqualTo("?returnUrl=%2Fcustomers%2F5"));
        Assert.That(p.Current.Trigger, Is.EqualTo(NavigationTrigger.Redirect));
        Assert.That(p.Current.RedirectedFrom, Is.EqualTo(new[] { Base + "customers/5" }));
        Assert.That(p.Adapter.History, Is.EqualTo(new[] { Base, Base + "login?returnUrl=%2Fcustomers%2F5" }));
        Assert.That(p.Commits, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Redirect_to_a_url_can_replace_history_explicitly()
    {
        using var p = await StartAsync();
        p.Guards.Set("auth", RouteGuardResult.Redirect("login", replaceHistory: true));

        await p.GoAsync("customers/5");

        Assert.That(p.Adapter.History, Is.EqualTo(new[] { Base + "login" }));
    }

    [Test]
    public async Task Redirect_during_the_initial_load_replaces_the_redirecting_entry()
    {
        using var p = await StartAsync(Base + "customers/5", initial: false);
        p.Guards.Set("auth", RouteGuardResult.RedirectToRoute("login"));

        var outcome = await p.Coordinator.NavigateInitialAsync();
        await p.Adapter.SettledAsync();

        Assert.That(outcome, Is.EqualTo(NavigationOutcome.Redirected));
        Assert.That(p.Current.Path, Is.EqualTo("/login"));
        Assert.That(p.Current.Trigger, Is.EqualTo(NavigationTrigger.Redirect));
        Assert.That(p.Current.RedirectedFrom, Is.EqualTo(new[] { Base + "customers/5" }));
        Assert.That(p.Adapter.History, Is.EqualTo(new[] { Base + "login" }));
        Assert.That(p.Commits, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task A_guard_that_cancels_after_an_initial_redirect_commits_a_failure_so_the_router_does_not_wait_forever()
    {
        using var p = await StartAsync(Base + "customers", initial: false);
        p.Guards.Set("auth", context => ValueTask.FromResult(context.To.EndsWith("/customers") ? RouteGuardResult.Redirect("customers/9") : RouteGuardResult.Allow()));
        p.Guards.Set("audit", RouteGuardResult.Cancel());

        var outcome = await p.Coordinator.NavigateInitialAsync();
        await p.Adapter.SettledAsync();

        Assert.That(outcome, Is.EqualTo(NavigationOutcome.Redirected));
        Assert.That(p.Current.Failure, Is.EqualTo(RouteFailure.NavigationFailed));
        Assert.That(p.Current.Error!.Kind, Is.EqualTo(RouteErrorKind.NavigationFailed));
    }

    [Test]
    public async Task Redirect_to_an_external_url_does_a_full_page_load()
    {
        using var p = await StartAsync();
        p.Guards.Set("auth", RouteGuardResult.Redirect("https://login.example.com/start"));

        var allowed = await p.GoAsync("customers");

        Assert.That(allowed, Is.False);
        Assert.That(p.Adapter.NavigateCalls.Last(), Is.EqualTo(("https://login.example.com/start", false, true, (string?)null)));
        Assert.That(p.Commits, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Redirect_to_an_unknown_route_is_a_guard_failure_at_that_level()
    {
        using var p = await StartAsync();
        p.Guards.Set("audit", RouteGuardResult.RedirectToRoute("nope"));

        await p.GoAsync("customers/5");

        Assert.That(p.Current.Path, Is.EqualTo("/customers/5"));
        Assert.That(p.Current.Error!.Kind, Is.EqualTo(RouteErrorKind.GuardFailed));
        Assert.That(p.Current.FailedLevel, Is.Zero);
        Assert.That(p.Current.Error.Message, Does.Contain("nope"));
    }

    [Test]
    public async Task NotAuthorized_commits_with_an_error_at_that_level_and_skips_the_rest()
    {
        using var p = await StartAsync();
        p.Guards.Set("admin", RouteGuardResult.NotAuthorized("Admins only"));

        var allowed = await p.GoAsync("customers/5/orders");

        Assert.That(allowed, Is.True);
        Assert.That(p.Current.Path, Is.EqualTo("/customers/5/orders"));
        Assert.That(p.Current.Chain, Is.Not.Null);
        Assert.That(p.Current.Failure, Is.EqualTo(RouteFailure.None));
        Assert.That(p.Current.Error!.Kind, Is.EqualTo(RouteErrorKind.NotAuthorized));
        Assert.That(p.Current.Error.Message, Is.EqualTo("Admins only"));
        Assert.That(p.Current.FailedLevel, Is.EqualTo(2));
    }

    [Test]
    public async Task NotAuthorized_at_a_parent_stops_deeper_guards()
    {
        using var p = await StartAsync();
        p.Guards.Set("auth", RouteGuardResult.NotAuthorized());

        await p.GoAsync("customers/5/orders");

        Assert.That(p.Guards.Calls, Is.EqualTo(new[] { "auth@customers" }));
        Assert.That(p.Current.FailedLevel, Is.Zero);
        Assert.That(p.Current.Error!.Message, Is.Not.Empty);
    }

    [Test]
    public async Task A_guard_that_throws_is_a_guard_failure_at_its_level()
    {
        using var p = await StartAsync();
        p.Guards.Set("admin", _ => throw new InvalidOperationException("auth service down"));

        await p.GoAsync("customers/5/orders");

        Assert.That(p.Current.Error!.Kind, Is.EqualTo(RouteErrorKind.GuardFailed));
        Assert.That(p.Current.FailedLevel, Is.EqualTo(2));
        Assert.That(p.Current.Error.Exception, Is.TypeOf<InvalidOperationException>());
        Assert.That(p.Current.Error.Message, Does.Contain("auth service down"));
    }

    [Test]
    public async Task A_newer_navigation_cancels_a_pending_guard()
    {
        using var p = await StartAsync();
        var started = new TaskCompletionSource();
        CancellationToken seen = default;
        p.Guards.Set("auth", async context =>
        {
            seen = context.CancellationToken;
            started.SetResult();
            await Task.Delay(Timeout.Infinite, context.CancellationToken);

            return RouteGuardResult.Allow();
        });

        var slow = p.Adapter.NavigateAsync("customers", intercepted: true);
        await started.Task;
        p.Guards.Set("auth", RouteGuardResult.Allow());
        var fast = await p.Adapter.NavigateAsync("login", intercepted: true);

        Assert.That(fast, Is.True);
        Assert.That(seen.IsCancellationRequested, Is.True);
        Assert.That(await slow, Is.False);
        Assert.That(p.Current.Path, Is.EqualTo("/login"));
    }

    [Test]
    public async Task A_superseded_navigation_does_not_carry_out_its_redirect()
    {
        using var p = await StartAsync();
        var gate = new TaskCompletionSource();
        p.Guards.Set("auth", async context =>
        {
            if (!context.To.EndsWith("/customers", StringComparison.Ordinal))
            {
                return RouteGuardResult.Allow();
            }

            // A guard that awaits something ignoring its token, then redirects.
            await gate.Task;

            return RouteGuardResult.RedirectToRoute("login");
        });

        var stale = p.Adapter.NavigateAsync("customers", intercepted: true);
        Assert.That(await p.GoAsync("customers/5"), Is.True);
        Assert.That(p.Current.Path, Is.EqualTo("/customers/5"));

        gate.SetResult();
        Assert.That(await stale, Is.False);
        await p.Adapter.SettledAsync();

        Assert.That(p.Adapter.NavigateCalls, Is.Empty, "the stale redirect must never be issued");
        Assert.That(p.Current.Path, Is.EqualTo("/customers/5"));
        Assert.That(p.Commits.Select(c => c.Path), Is.EqualTo(new[] { "/", "/customers/5" }));
    }

    [Test]
    public async Task Redirect_rules_and_guards_do_not_run_for_an_unknown_url()
    {
        using var p = await StartAsync();

        await p.GoAsync("nope");

        Assert.That(p.Current.Failure, Is.EqualTo(RouteFailure.NotFound));
        Assert.That(p.Guards.Calls, Is.Empty);
    }
}
