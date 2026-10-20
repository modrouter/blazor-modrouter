using System.Runtime.CompilerServices;
using ModRouter.Core.Compilation;
using ModRouter.Core.Guards;
using ModRouter.Core.Loading;
using ModRouter.Navigation;

namespace ModRouter.Test;

[TestFixture]
public sealed class LoaderPipelineTests
{
    sealed class DelegateStage(Func<RouteNavigationContext, StageResult> run, bool runsOnFailure = false) : INavigationStage
    {
        public bool RunsOnFailure => runsOnFailure;

        public ValueTask<StageResult> RunAsync(RouteNavigationContext context) => ValueTask.FromResult(run(context));
    }

    static CompiledRouteTable LoaderTable(RouteReloadPolicy customerPolicy = RouteReloadPolicy.WhenParametersChange) => Pipeline.Table(map => map
        .Route("/customers", typeof(CustomersPage), name: "customers", children: customers => customers
            .Index(typeof(CustomerList), name: "customer-list")
            .Route("{id:int}", typeof(CustomerDetails), name: "customer", children: customer => customer
                .Route("orders", typeof(OrderDetails), name: "orders").Loader<OrdersLoader>())
            .Loader<CustomerLoader>(customerPolicy))
        .Loader<CustomersLoader>());

    static async Task<Pipeline> StartAsync(RouteReloadPolicy policy = RouteReloadPolicy.WhenParametersChange, IEnumerable<INavigationStage>? stages = null)
    {
        var pipeline = Pipeline.Create(LoaderTable(policy), stages: stages);
        await pipeline.Coordinator.NavigateInitialAsync();

        return pipeline;
    }

    static async IAsyncEnumerable<int> Counting(Action onPull, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 1; i <= 3; i++)
        {
            onPull();
            await Task.Delay(1, cancellationToken);

            yield return i;
        }
    }

    [Test]
    public async Task Critical_data_is_on_the_committed_state_per_level()
    {
        using var p = await StartAsync();

        await p.GoAsync("customers/5");

        Assert.That(p.Current.LevelData, Has.Count.EqualTo(2));
        Assert.That(p.Current.LevelData[0]!.Data, Is.EqualTo("customers"));
        Assert.That(p.Current.LevelData[1]!.Data, Is.EqualTo("customer"));
        Assert.That(p.Loaders.Calls, Is.EquivalentTo(new[] { "customers@customers", "customer@customer" }));
    }

    [Test]
    public async Task A_level_without_a_loader_has_no_data_and_costs_nothing()
    {
        using var p = await StartAsync();

        await p.GoAsync("customers/5/orders");

        Assert.That(p.Current.LevelData, Has.Count.EqualTo(3));
        Assert.That(p.Current.LevelData[2]!.Data, Is.EqualTo("orders"));

        await p.GoAsync("login");

        Assert.That(p.Current.LevelData, Is.Empty.Or.All.Null);
    }

    [Test]
    public async Task Loaders_start_together_so_a_slow_parent_does_not_delay_a_child()
    {
        using var p = await StartAsync();
        var gate = new TaskCompletionSource();
        p.Loaders.Set("customers", async _ =>
        {
            await gate.Task;

            return RouteLoadResult.Create("customers");
        });

        var navigation = p.GoAsync("customers/5");
        await WaitUntilAsync(() => p.Loaders.Calls.Contains("customer@customer"));

        Assert.That(p.Loaders.Calls, Does.Contain("customers@customers"));
        Assert.That(navigation.IsCompleted, Is.False);

        gate.SetResult();
        await navigation;

        Assert.That(p.Current.Path, Is.EqualTo("/customers/5"));
    }

    [Test]
    public async Task A_loader_can_use_its_parents_data()
    {
        using var p = await StartAsync();
        p.Loaders.Returns("customer", "Ada");
        p.Loaders.Set("orders", async context =>
        {
            var customer = await context.GetParentDataAsync<string>();

            return RouteLoadResult.Create($"orders of {customer}");
        });

        await p.GoAsync("customers/5/orders");

        Assert.That(p.Current.LevelData[2]!.Data, Is.EqualTo("orders of Ada"));
    }

    [Test]
    public async Task Parent_data_is_default_when_no_ancestor_loads_that_type()
    {
        using var p = await StartAsync();
        int? seen = -1;
        p.Loaders.Set("orders", async context =>
        {
            seen = await context.GetParentDataAsync<int?>();

            return RouteLoadResult.Create();
        });

        await p.GoAsync("customers/5/orders");

        Assert.That(seen, Is.Null);
    }

    [Test]
    public async Task Load_context_describes_the_level()
    {
        using var p = await StartAsync();

        await p.GoAsync("customers/5?tab=a");

        var context = p.Loaders.Contexts.Single(c => c.Level.RouteName == "customer");
        Assert.That(context.Parameters.Get<int>("id"), Is.EqualTo(5));
        Assert.That(context.Query, Is.EqualTo("?tab=a"));
        Assert.That(context.IsReload, Is.False);
        Assert.That(context.Services, Is.Not.Null);
        Assert.That(context.LifetimeToken.IsCancellationRequested, Is.False);
    }

    [Test]
    public async Task Deferred_values_and_streams_are_reachable_from_the_level_data()
    {
        using var p = await StartAsync();
        var slow = new TaskCompletionSource<int>();
        p.Loaders.Set("customer", _ => ValueTask.FromResult(RouteLoadResult.Create("c").Defer("orders", slow.Task).Stream("feed", Counting(() => { }))));

        await p.GoAsync("customers/5");
        var level = p.Current.LevelData[1]!;

        Assert.That(level.Deferred<int>("orders").Status, Is.EqualTo(DeferredStatus.Pending));
        slow.SetResult(7);
        await WaitUntilAsync(() => level.Deferred<int>("orders").Status == DeferredStatus.Succeeded);
        Assert.That(level.Deferred<int>("orders").Value, Is.EqualTo(7));

        await WaitUntilAsync(() => level.Stream<int>("feed").IsComplete);
        Assert.That(level.Stream<int>("feed").Items, Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.Throws<KeyNotFoundException>(() => level.Deferred<int>("missing"));
        Assert.Throws<InvalidCastException>(() => level.Deferred<string>("orders"));
    }

    [Test]
    public async Task A_failing_loader_commits_an_error_at_its_level_and_keeps_the_levels_above()
    {
        using var p = await StartAsync();
        p.Loaders.Set("customer", _ => throw new InvalidOperationException("db down"));

        var allowed = await p.GoAsync("customers/5/orders");

        Assert.That(allowed, Is.True);
        Assert.That(p.Current.Path, Is.EqualTo("/customers/5/orders"));
        Assert.That(p.Current.Chain, Is.Not.Null);
        Assert.That(p.Current.Error!.Kind, Is.EqualTo(RouteErrorKind.LoadFailed));
        Assert.That(p.Current.FailedLevel, Is.EqualTo(1));
        Assert.That(p.Current.Error.Exception, Is.TypeOf<InvalidOperationException>());
        Assert.That(p.Current.LevelData[0]!.Data, Is.EqualTo("customers"));
        Assert.That(p.Current.LevelData[1], Is.Null);
        Assert.That(p.Current.LevelData[2], Is.Null);
    }

    [Test]
    public async Task A_failure_cancels_the_loaders_of_deeper_levels()
    {
        using var p = await StartAsync();
        CancellationToken orders = default;
        var ordersStarted = new TaskCompletionSource();
        p.Loaders.Set("customer", async _ =>
        {
            await ordersStarted.Task;

            throw new InvalidOperationException("boom");
        });
        p.Loaders.Set("orders", async context =>
        {
            orders = context.CancellationToken;
            ordersStarted.SetResult();
            await Task.Delay(Timeout.Infinite, context.CancellationToken);

            return RouteLoadResult.Create();
        });

        await p.GoAsync("customers/5/orders");

        Assert.That(p.Current.FailedLevel, Is.EqualTo(1));
        Assert.That(orders.IsCancellationRequested, Is.True);
    }

    [Test]
    public async Task Data_a_deeper_loader_produces_after_the_failure_is_discarded_without_delaying_the_error()
    {
        using var p = await StartAsync();
        var release = new TaskCompletionSource();
        CancellationToken lifetime = default;
        p.Loaders.Set("customer", _ => throw new InvalidOperationException("boom"));
        p.Loaders.Set("orders", async context =>
        {
            lifetime = context.LifetimeToken;
            await release.Task;

            return RouteLoadResult.Create("late");
        });

        await p.GoAsync("customers/5/orders");

        Assert.That(p.Current.FailedLevel, Is.EqualTo(1), "the error committed without waiting for the ignoring loader");
        Assert.That(lifetime.IsCancellationRequested, Is.False);

        release.SetResult();
        await WaitUntilAsync(() => lifetime.IsCancellationRequested);
    }

    [Test]
    public async Task A_failing_parent_is_the_error_even_when_the_child_also_fails()
    {
        using var p = await StartAsync();
        p.Loaders.Set("customer", _ => throw new InvalidOperationException("parent"));
        p.Loaders.Set("orders", async context =>
        {
            await context.GetParentDataAsync<string>();

            return RouteLoadResult.Create();
        });

        await p.GoAsync("customers/5/orders");

        Assert.That(p.Current.FailedLevel, Is.EqualTo(1));
        Assert.That(p.Current.Error!.Message, Does.Contain("parent"));
    }

    [Test]
    public async Task A_failure_at_the_first_level_leaves_no_data()
    {
        using var p = await StartAsync();
        p.Loaders.Set("customers", _ => throw new InvalidOperationException("root"));

        await p.GoAsync("customers/5");

        Assert.That(p.Current.FailedLevel, Is.Zero);
        Assert.That(p.Current.LevelData, Is.All.Null);
    }

    [Test]
    public async Task NotAuthorized_loads_only_the_levels_above_the_denied_one()
    {
        var table = Pipeline.Table(map => map
            .Route("/customers", typeof(CustomersPage), name: "customers", children: customers => customers
                .Route("{id:int}", typeof(CustomerDetails), name: "customer").Guard<AdminGuard>().Loader<CustomerLoader>())
            .Loader<CustomersLoader>());
        using var p = Pipeline.Create(table);
        await p.Coordinator.NavigateInitialAsync();
        p.Guards.Set("admin", RouteGuardResult.NotAuthorized());

        await p.GoAsync("customers/5");

        Assert.That(p.Current.FailedLevel, Is.EqualTo(1));
        Assert.That(p.Loaders.Calls, Is.EqualTo(new[] { "customers@customers" }));
        Assert.That(p.Current.LevelData[0]!.Data, Is.EqualTo("customers"));
    }

    [TestCase(RouteReloadPolicy.WhenParametersChange, false, true)]
    [TestCase(RouteReloadPolicy.WhenParametersOrQueryChange, true, true)]
    [TestCase(RouteReloadPolicy.EveryNavigation, true, true)]
    [TestCase(RouteReloadPolicy.Never, false, false)]
    public async Task Reload_policy_decides_when_a_kept_level_loads_again(RouteReloadPolicy policy, bool queryChangeReloads, bool parameterChangeReloads)
    {
        using var p = await StartAsync(policy);
        await p.GoAsync("customers/1");
        var first = p.Current.LevelData[1];

        await p.GoAsync("customers/1?x=2");
        Assert.That(ReferenceEquals(p.Current.LevelData[1], first), Is.EqualTo(!queryChangeReloads), "query change");

        var afterQuery = p.Current.LevelData[1];
        await p.GoAsync("customers/2");
        Assert.That(ReferenceEquals(p.Current.LevelData[1], afterQuery), Is.EqualTo(!parameterChangeReloads), "parameter change");
    }

    [Test]
    public async Task Navigating_to_another_route_at_the_same_level_always_loads()
    {
        using var p = await StartAsync(RouteReloadPolicy.Never);
        await p.GoAsync("customers/1");
        var calls = p.Loaders.Calls.Count(c => c == "customer@customer");

        await p.GoAsync("customers");
        await p.GoAsync("customers/1");

        Assert.That(p.Loaders.Calls.Count(c => c == "customer@customer"), Is.EqualTo(calls + 1));
    }

    [Test]
    public async Task A_reused_level_keeps_its_deferred_values_and_streams()
    {
        using var p = await StartAsync();
        var slow = new TaskCompletionSource<int>();
        p.Loaders.Set("customer", _ => ValueTask.FromResult(RouteLoadResult.Create("c").Defer("orders", slow.Task)));
        await p.GoAsync("customers/1");
        var deferred = p.Current.LevelData[1]!.Deferred<int>("orders");

        await p.GoAsync("customers/1?x=2");

        Assert.That(p.Current.LevelData[1]!.Deferred<int>("orders"), Is.SameAs(deferred));
        Assert.That(p.Current.LevelData[1]!.Accessor, Is.Not.Null);
    }

    [Test]
    public async Task A_reused_level_sees_the_data_of_a_parent_that_loaded_again()
    {
        // customer reloads on a query change; orders (default policy) is reused and must not keep pointing at the
        // discarded customer data through its accessor.
        using var p = await StartAsync(RouteReloadPolicy.WhenParametersOrQueryChange);
        var loads = 0;
        p.Loaders.Set("customer", _ => ValueTask.FromResult(RouteLoadResult.Create($"customer {++loads}")));
        await p.GoAsync("customers/5/orders");
        var orders = p.Current.LevelData[2]!;
        Assert.That(orders.Accessor.GetFromAncestor<string>(), Is.EqualTo("customer 1"));

        await p.GoAsync("customers/5/orders?x=1");

        Assert.That(p.Current.LevelData[2], Is.SameAs(orders), "orders is reused");
        Assert.That(p.Current.LevelData[1]!.Data, Is.EqualTo("customer 2"));
        Assert.That(orders.Accessor.GetFromAncestor<string>(), Is.EqualTo("customer 2"));
    }

    [Test]
    public async Task Streams_start_at_commit_and_never_while_only_prepared()
    {
        var pulled = 0;
        var cancelAfterLoaders = false;
        var stage = new DelegateStage(_ => cancelAfterLoaders ? StageResult.Cancel : StageResult.Continue);
        using var p = await StartAsync(stages: [stage]);
        p.Loaders.Set("customer", _ => ValueTask.FromResult(RouteLoadResult.Create("c").Stream("feed", Counting(() => Interlocked.Increment(ref pulled)))));

        cancelAfterLoaders = true;
        Assert.That(await p.GoAsync("customers/1"), Is.False);
        await Task.Delay(50);

        Assert.That(pulled, Is.Zero, "a navigation that never committed must not enumerate");

        cancelAfterLoaders = false;
        await p.GoAsync("customers/1");
        await WaitUntilAsync(() => pulled > 0);

        Assert.That(pulled, Is.GreaterThan(0));
    }

    [Test]
    public async Task A_discarded_level_cancels_its_lifetime_and_a_reused_one_does_not()
    {
        using var p = await StartAsync();
        var lifetimes = new Dictionary<string, CancellationToken>();
        p.Loaders.Set("customers", context =>
        {
            lifetimes["customers"] = context.LifetimeToken;

            return ValueTask.FromResult(RouteLoadResult.Create("customers"));
        });
        p.Loaders.Set("customer", context =>
        {
            lifetimes["customer"] = context.LifetimeToken;

            return ValueTask.FromResult(RouteLoadResult.Create("customer"));
        });

        await p.GoAsync("customers/1");
        await p.GoAsync("customers/2");

        Assert.That(lifetimes["customer"].IsCancellationRequested, Is.False, "the second customer's lifetime is live");
        Assert.That(p.Loaders.Contexts.Where(c => c.Level.RouteName == "customer").First().LifetimeToken.IsCancellationRequested, Is.True, "the first customer's data was replaced");
        Assert.That(lifetimes["customers"].IsCancellationRequested, Is.False, "the parent level was reused");

        await p.GoAsync("login");

        Assert.That(lifetimes["customers"].IsCancellationRequested, Is.True);
        Assert.That(lifetimes["customer"].IsCancellationRequested, Is.True);
    }

    [Test]
    public async Task A_superseded_navigation_discards_what_it_loaded()
    {
        using var p = await StartAsync();
        var slowGate = new TaskCompletionSource();
        CancellationToken lifetime = default;
        CancellationToken critical = default;
        p.Loaders.Set("customer", async context =>
        {
            lifetime = context.LifetimeToken;
            critical = context.CancellationToken;
            await slowGate.Task;

            return RouteLoadResult.Create("slow");
        });

        var slow = p.Adapter.NavigateAsync("customers/1", intercepted: true);
        await WaitUntilAsync(() => p.Loaders.Calls.Contains("customer@customer"));
        p.Loaders.Set("customer", _ => ValueTask.FromResult(RouteLoadResult.Create("fast")));
        await p.Adapter.NavigateAsync("customers/2", intercepted: true);
        slowGate.SetResult();

        Assert.That(await slow, Is.False);
        await WaitUntilAsync(() => lifetime.IsCancellationRequested);
        Assert.That(critical.IsCancellationRequested, Is.True);
        Assert.That(lifetime.IsCancellationRequested, Is.True);
        Assert.That(p.Current.Path, Is.EqualTo("/customers/2"));
        Assert.That(p.Current.LevelData[1]!.Data, Is.EqualTo("fast"));
    }

    [Test]
    public async Task Retry_reloads_only_the_failed_level_and_reuses_the_others()
    {
        using var p = await StartAsync();
        var failing = true;
        p.Loaders.Set("customer", _ => failing ? throw new InvalidOperationException("flaky") : ValueTask.FromResult(RouteLoadResult.Create("customer ok")));
        await p.GoAsync("customers/5");
        var parent = p.Current.LevelData[0];
        var error = p.Current.Error!;
        var commits = p.Commits.Count;
        var parentLoads = p.Loaders.Calls.Count(c => c == "customers@customers");

        failing = false;
        await error.Retry();

        Assert.That(p.Commits, Has.Count.EqualTo(commits + 1));
        Assert.That(p.Current.Error, Is.Null);
        Assert.That(p.Current.Trigger, Is.EqualTo(NavigationTrigger.Reload));
        Assert.That(p.Current.Path, Is.EqualTo("/customers/5"));
        Assert.That(p.Current.LevelData[1]!.Data, Is.EqualTo("customer ok"));
        Assert.That(ReferenceEquals(p.Current.LevelData[0], parent), Is.True, "the parent level was reused");
        Assert.That(p.Loaders.Calls.Count(c => c == "customers@customers"), Is.EqualTo(parentLoads));
        Assert.That(p.Loaders.Contexts.Last(c => c.Level.RouteName == "customer").IsReload, Is.True);
        Assert.That(p.Adapter.History, Has.Count.EqualTo(2), "a retry does not add a history entry");
    }

    [Test]
    public async Task Retry_of_a_not_authorized_error_runs_the_guards_again()
    {
        var table = Pipeline.Table(map => map.Route("/secret", typeof(OtherPage), name: "secret").Guard<AuthGuard>());
        using var p = Pipeline.Create(table);
        await p.Coordinator.NavigateInitialAsync();
        p.Guards.Set("auth", RouteGuardResult.NotAuthorized());
        await p.GoAsync("secret");
        var error = p.Current.Error!;

        p.Guards.Set("auth", RouteGuardResult.Allow());
        await error.Retry();

        Assert.That(p.Current.Error, Is.Null);
        Assert.That(p.Current.Path, Is.EqualTo("/secret"));
    }

    [Test]
    public async Task Reload_all_loads_every_level_again()
    {
        using var p = await StartAsync();
        await p.GoAsync("customers/5");
        var before = p.Current.LevelData.ToArray();

        var outcome = await p.Coordinator.ReloadAsync(all: true);

        Assert.That(outcome, Is.EqualTo(NavigationOutcome.Prepared));
        Assert.That(p.Current.Trigger, Is.EqualTo(NavigationTrigger.Reload));
        Assert.That(p.Current.LevelData[0], Is.Not.SameAs(before[0]));
        Assert.That(p.Current.LevelData[1], Is.Not.SameAs(before[1]));
    }

    [Test]
    public async Task The_navigation_service_exposes_reload_navigating_and_the_current_error()
    {
        var table = LoaderTable();
        using var p = Pipeline.Create(table);
        using var service = new NavigationService(p.Adapter, RouteTestKit.Frozen(table), p.Store);
        await p.Coordinator.NavigateInitialAsync();
        p.Loaders.Set("customer", _ => throw new InvalidOperationException("boom"));
        await p.GoAsync("customers/5");

        Assert.That(service.CurrentError!.Kind, Is.EqualTo(RouteErrorKind.LoadFailed));
        Assert.That(service.IsNavigating, Is.False);

        var navigating = new List<bool>();
        service.NavigatingChanged += () => navigating.Add(service.IsNavigating);
        p.Loaders.Returns("customer", "fixed");
        var loadsBefore = p.Loaders.Calls.Count;

        await service.ReloadAsync();

        Assert.That(service.CurrentError, Is.Null);
        Assert.That(p.Current.LevelData[1]!.Data, Is.EqualTo("fixed"));
        Assert.That(p.Loaders.Calls.Count - loadsBefore, Is.EqualTo(2), "every loader of the current route ran again");
        Assert.That(navigating, Is.EqualTo(new[] { true, false }));
    }

    [Test]
    public async Task The_navigation_service_reload_does_nothing_when_no_router_is_mounted()
    {
        var table = LoaderTable();
        using var p = Pipeline.Create(table, attach: false);
        using var service = new NavigationService(p.Adapter, RouteTestKit.Frozen(table), p.Store);

        await service.ReloadAsync();

        Assert.That(p.Store.Current, Is.Null);
    }

    [Test]
    public async Task Retry_and_reload_do_nothing_after_the_router_detached()
    {
        var table = LoaderTable();
        using var p = Pipeline.Create(table);
        using var service = new NavigationService(p.Adapter, RouteTestKit.Frozen(table), p.Store);
        await p.Coordinator.NavigateInitialAsync();
        p.Loaders.Set("customer", _ => throw new InvalidOperationException("boom"));
        await p.GoAsync("customers/5");
        var error = p.Current.Error!;
        p.Loaders.Returns("customer", "fixed");
        p.Lease!.Dispose();
        var commits = p.Commits.Count;
        var loads = p.Loaders.Calls.Count;

        await error.Retry();
        var outcome = await p.Coordinator.ReloadAsync(all: true);
        await service.ReloadAsync();

        Assert.That(outcome, Is.EqualTo(NavigationOutcome.Cancelled));
        Assert.That(p.Commits, Has.Count.EqualTo(commits), "a disposed router must not commit");
        Assert.That(p.Loaders.Calls, Has.Count.EqualTo(loads), "a disposed router must not run loaders");
        Assert.That(p.Current.Error, Is.SameAs(error));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task A_remounted_router_whose_first_navigation_ends_without_a_page_commits_a_failure(bool throws)
    {
        // The scoped store outlives the previous ModRouter; the new router must not keep showing that router's state.
        var armed = false;
        var stage = new DelegateStage(_ => armed ? (throws ? throw new InvalidOperationException("boom") : StageResult.Cancel) : StageResult.Continue);
        using var p = await StartAsync(stages: [stage]);
        await p.GoAsync("customers/5");
        p.Lease!.Dispose();
        using var again = p.Coordinator.Attach();
        armed = true;

        var outcome = await p.Coordinator.NavigateInitialAsync();

        Assert.That(outcome, Is.EqualTo(throws ? NavigationOutcome.Failed : NavigationOutcome.Cancelled));
        Assert.That(p.Current.Failure, Is.EqualTo(RouteFailure.NavigationFailed));
        Assert.That(p.Current.Error!.Kind, Is.EqualTo(RouteErrorKind.NavigationFailed));
    }

    [Test]
    public async Task A_remounted_router_does_not_treat_the_previous_routers_state_as_the_one_it_came_from()
    {
        var sawFrom = new List<(string? From, RouteState? FromState)>();
        var stage = new DelegateStage(context =>
        {
            sawFrom.Add((context.From, context.FromState));

            return StageResult.Continue;
        });
        using var p = await StartAsync(stages: [stage]);
        await p.GoAsync("customers/5");
        var stale = p.Current.LevelData[0]!;
        p.Lease!.Dispose();
        using var again = p.Coordinator.Attach();

        await p.GoAsync("customers/6");

        Assert.That(sawFrom[^1].From, Is.Null);
        Assert.That(sawFrom[^1].FromState, Is.Null);
        Assert.That(p.Current.LevelData[0], Is.Not.SameAs(stale), "the discarded level data of the previous router is never reused");
    }

    [Test]
    public async Task Reload_without_a_committed_state_does_nothing()
    {
        using var p = Pipeline.Create(LoaderTable());

        Assert.That(await p.Coordinator.ReloadAsync(), Is.EqualTo(NavigationOutcome.Cancelled));
    }

    [Test]
    public async Task Router_state_reports_navigation_in_progress()
    {
        using var p = await StartAsync();
        var gate = new TaskCompletionSource();
        var changes = new List<bool>();
        p.Store.NavigatingChanged += () => changes.Add(p.Store.IsNavigating);
        p.Loaders.Set("customer", async _ =>
        {
            await gate.Task;

            return RouteLoadResult.Create();
        });

        var navigation = p.GoAsync("customers/5");
        await WaitUntilAsync(() => p.Store.IsNavigating);

        Assert.That(p.Store.IsNavigating, Is.True);

        gate.SetResult();
        await navigation;

        Assert.That(p.Store.IsNavigating, Is.False);
        Assert.That(changes, Is.EqualTo(new[] { true, false }));
    }

    [Test]
    public async Task Navigating_stays_true_across_a_redirect()
    {
        var table = Pipeline.Table(map => map.Route("/old", typeof(OtherPage), name: "old").Guard<AuthGuard>());
        using var p = Pipeline.Create(table);
        await p.Coordinator.NavigateInitialAsync();
        var changes = new List<bool>();
        p.Store.NavigatingChanged += () => changes.Add(p.Store.IsNavigating);
        p.Guards.Set("auth", RouteGuardResult.RedirectToRoute("login"));

        await p.GoAsync("old");

        Assert.That(changes, Is.EqualTo(new[] { true, false }));
    }

    [Test]
    public async Task Stages_skip_failure_states_unless_they_opt_in()
    {
        var skipped = 0;
        var opted = 0;
        using var p = await StartAsync(stages:
        [
            new DelegateStage(_ =>
            {
                skipped++;

                return StageResult.Continue;
            }),
            new DelegateStage(
                _ =>
                {
                    opted++;

                    return StageResult.Continue;
                },
                runsOnFailure: true),
        ]);
        (skipped, opted) = (0, 0);

        await p.GoAsync("nope");

        Assert.That(p.Current.Failure, Is.EqualTo(RouteFailure.NotFound));
        Assert.That(skipped, Is.Zero);
        Assert.That(opted, Is.EqualTo(1));
    }

    [Test]
    public async Task Extra_stages_see_a_chain_level_error_only_when_they_opt_in()
    {
        var seen = 0;
        using var p = await StartAsync(stages:
        [
            new DelegateStage(_ =>
            {
                seen++;

                return StageResult.Continue;
            }),
        ]);
        p.Loaders.Set("customer", _ => throw new InvalidOperationException("boom"));
        seen = 0;

        await p.GoAsync("customers/5");

        Assert.That(p.Current.Error, Is.Not.Null);
        Assert.That(seen, Is.Zero);
    }

    [Test]
    public async Task A_throwing_store_subscriber_does_not_stop_the_others_or_the_commit()
    {
        using var p = await StartAsync();
        var received = new List<string>();
        p.Store.Changed += _ => throw new InvalidOperationException("bad subscriber");
        p.Store.Changed += state => received.Add(state.Path);

        await p.GoAsync("customers/5");

        Assert.That(received, Is.EqualTo(new[] { "/customers/5" }));
        Assert.That(p.Current.Path, Is.EqualTo("/customers/5"));
    }

    [Test]
    public async Task The_previous_state_provides_from_not_the_already_changed_platform_uri()
    {
        string? from = null;
        var stage = new DelegateStage(context =>
        {
            from = context.From;

            return StageResult.Continue;
        });
        using var p = Pipeline.Create(LoaderTable(), stages: [stage]);
        await p.Coordinator.NavigateInitialAsync();
        Assert.That(from, Is.Null, "nothing was committed before the first navigation");

        await p.GoAsync("customers/1");

        Assert.That(from, Is.EqualTo("http://localhost/"));
    }

    static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.That(condition(), Is.True, "timed out waiting for a condition");
    }
}
