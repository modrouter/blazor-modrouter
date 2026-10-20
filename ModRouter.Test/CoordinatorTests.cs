using ModRouter.Components;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModRouter.Test;

public sealed class GuidIdPage
{
    [Parameter]
    public Guid Id { get; set; }
}

[TestFixture]
public sealed class CoordinatorTests
{
    sealed class DelegateStage(Func<RouteNavigationContext, ValueTask<StageResult>> run) : INavigationStage
    {
        public ValueTask<StageResult> RunAsync(RouteNavigationContext context) => run(context);
    }

    sealed class Harness : IDisposable
    {
        public required FakeNavigationAdapter Adapter { get; init; }

        public required RouteStateStore Store { get; init; }

        public required NavigationCoordinator Coordinator { get; init; }

        public required CompiledRouteTable Table { get; init; }

        public List<RouteState> Commits { get; } = [];

        public IDisposable? Lease { get; set; }

        public void Dispose() => Lease?.Dispose();
    }

    static Harness Create(
        CompiledRouteTable? table = null,
        IEnumerable<INavigationStage>? stages = null,
        string baseUri = "http://localhost/",
        string? uri = null,
        bool attach = true,
        IServiceProvider? services = null)
    {
        table ??= RouteTestKit.CustomersTable();
        var adapter = new FakeNavigationAdapter(baseUri, uri);
        var store = new RouteStateStore();
        var coordinator = new NavigationCoordinator(adapter, store, RouteTestKit.Frozen(table), new RouteComponentBinder(), services ?? new ServiceCollection().BuildServiceProvider(), new ModRouterOptions(), stages ?? [], NullLoggerFactory.Instance);
        var harness = new Harness { Adapter = adapter, Store = store, Coordinator = coordinator, Table = table };
        store.Changed += harness.Commits.Add;

        if (attach)
        {
            harness.Lease = coordinator.Attach();
        }

        return harness;
    }

    // ---- initial load ------------------------------------------------------------------------------------------

    [Test]
    public async Task Initial_load_commits_the_matched_chain()
    {
        using var h = Create(uri: "http://localhost/customers/42");

        var outcome = await h.Coordinator.NavigateInitialAsync();

        Assert.That(outcome, Is.EqualTo(NavigationOutcome.Prepared));
        Assert.That(h.Store.Current!.Trigger, Is.EqualTo(NavigationTrigger.InitialLoad));
        Assert.That(h.Store.Current.Path, Is.EqualTo("/customers/42"));
        Assert.That(RouteTestKit.Names(h.Store.Current.Chain!), Is.EqualTo(new[] { "customers", "customer-details" }));
        Assert.That(h.Commits, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Initial_load_of_an_unknown_url_commits_a_not_found_state()
    {
        using var h = Create(uri: "http://localhost/nope");

        var outcome = await h.Coordinator.NavigateInitialAsync();

        Assert.That(outcome, Is.EqualTo(NavigationOutcome.NotFound));
        Assert.That(h.Store.Current!.Failure, Is.EqualTo(RouteFailure.NotFound));
        Assert.That(h.Store.Current.Chain, Is.Null);
    }

    [Test]
    public async Task Initial_load_under_a_base_path_is_matched_base_relative()
    {
        using var h = Create(baseUri: "http://localhost/myapp/", uri: "http://localhost/myapp/customers/42?tab=a#top");

        await h.Coordinator.NavigateInitialAsync();

        Assert.That(h.Store.Current!.Path, Is.EqualTo("/customers/42"));
        Assert.That(h.Store.Current.Query, Is.EqualTo("?tab=a"));
        Assert.That(h.Store.Current.Fragment, Is.EqualTo("top"));
    }

    [Test]
    public async Task Initial_load_outside_the_base_is_not_found()
    {
        using var h = Create(baseUri: "http://localhost/myapp/", uri: "http://localhost/elsewhere");

        await h.Coordinator.NavigateInitialAsync();

        Assert.That(h.Store.Current!.Failure, Is.EqualTo(RouteFailure.NotFound));
    }

    // ---- two-phase commit --------------------------------------------------------------------------------------

    [Test]
    public async Task A_navigation_is_prepared_before_the_url_changes_and_committed_after()
    {
        // The spy reads the PLATFORM URI (not context.From, which is the previous committed state's URI and would be
        // the same on the after-the-fact fallback path): it must still be the old URL while the stage runs.
        var seenDuringStage = new List<(string? CurrentPath, string? AdapterUri)>();
        Harness? harness = null;
        var spy = new DelegateStage(context =>
        {
            seenDuringStage.Add((harness?.Store.Current?.Path, harness?.Adapter.Uri));

            return ValueTask.FromResult(StageResult.Continue);
        });
        using var h = Create(stages: [spy], uri: "http://localhost/customers");
        harness = h;
        await h.Coordinator.NavigateInitialAsync();
        var commitsBefore = h.Commits.Count;

        var allowed = await h.Adapter.NavigateAsync("customers/42", intercepted: true);

        Assert.That(allowed, Is.True);
        Assert.That(seenDuringStage[^1].AdapterUri, Is.EqualTo("http://localhost/customers"), "the stage runs while the old URL is still current");
        Assert.That(seenDuringStage[^1].CurrentPath, Is.EqualTo("/customers"), "the stage runs before the new state commits");
        Assert.That(h.Commits, Has.Count.EqualTo(commitsBefore + 1));
        Assert.That(h.Store.Current!.Path, Is.EqualTo("/customers/42"));
        Assert.That(h.Store.Current.Trigger, Is.EqualTo(NavigationTrigger.Link));
    }

    [Test]
    public async Task Navigation_that_is_not_intercepted_is_programmatic_and_keeps_history_state()
    {
        using var h = Create();
        await h.Coordinator.NavigateInitialAsync();

        await h.Adapter.NavigateAsync("customers", intercepted: false, historyState: "state-1");

        Assert.That(h.Store.Current!.Trigger, Is.EqualTo(NavigationTrigger.Programmatic));
        Assert.That(h.Store.Current.HistoryEntryState, Is.EqualTo("state-1"));
    }

    [Test]
    public async Task A_history_traversal_is_recognized_by_the_mismatch_between_handler_and_changed_flags()
    {
        using var h = Create();
        await h.Coordinator.NavigateInitialAsync();

        await h.Adapter.NavigateAsync("customers/42", historyTraversal: true);

        Assert.That(h.Store.Current!.Trigger, Is.EqualTo(NavigationTrigger.HistoryRestore));
    }

    [Test]
    public async Task An_unknown_url_is_allowed_and_committed_as_not_found()
    {
        using var h = Create();
        await h.Coordinator.NavigateInitialAsync();

        var allowed = await h.Adapter.NavigateAsync("nope");

        Assert.That(allowed, Is.True);
        Assert.That(h.Store.Current!.Failure, Is.EqualTo(RouteFailure.NotFound));
        Assert.That(h.Store.Current.Chain, Is.Null);
    }

    [Test]
    public async Task A_malformed_path_is_committed_as_a_malformed_failure()
    {
        using var h = Create(table: RouteTestKit.Flat(("/users/{name}", "user")));
        await h.Coordinator.NavigateInitialAsync();

        await h.Adapter.NavigateAsync("users/%FF");

        Assert.That(h.Store.Current!.Failure, Is.EqualTo(RouteFailure.MalformedPath));
    }

    [Test]
    public async Task Navigations_outside_the_base_are_allowed_and_ignored()
    {
        using var h = Create(baseUri: "http://localhost/myapp/", uri: "http://localhost/myapp/customers");
        await h.Coordinator.NavigateInitialAsync();
        var before = h.Store.Current;

        Assert.That(await h.Adapter.NavigateAsync("http://other.example/customers/42"), Is.True);
        Assert.That(await h.Adapter.NavigateAsync("http://localhost/outside"), Is.True);

        Assert.That(h.Store.Current, Is.SameAs(before));
        Assert.That(h.Commits, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task Base_relative_navigation_works_under_a_base_path()
    {
        using var h = Create(baseUri: "http://localhost/myapp/", uri: "http://localhost/myapp/");
        await h.Coordinator.NavigateInitialAsync();

        await h.Adapter.NavigateAsync("customers/42");

        Assert.That(h.Store.Current!.Path, Is.EqualTo("/customers/42"));
        Assert.That(RouteTestKit.Leaf(h.Table, h.Store.Current.Path), Is.EqualTo("customer-details"));
    }

    [TestCase("http://localhost/", "/customers/42")]
    [TestCase("http://localhost/myapp/", "/myapp/customers/42")]
    public async Task A_rooted_target_is_prepared_before_the_url_changes(string baseUri, string target)
    {
        // A rooted target (NavigateTo("/...") or a GetRouteUrl result) must go through the handler like any other
        // navigation; on the observe-after fallback the stage would instead see the NEW URI on the platform.
        var adapterUriSeen = new List<string?>();
        Harness? harness = null;
        var spy = new DelegateStage(_ =>
        {
            adapterUriSeen.Add(harness?.Adapter.Uri);

            return ValueTask.FromResult(StageResult.Continue);
        });
        using var h = Create(stages: [spy], baseUri: baseUri, uri: baseUri + "customers");
        harness = h;
        await h.Coordinator.NavigateInitialAsync();
        var commitsBefore = h.Commits.Count;

        var allowed = await h.Adapter.NavigateAsync(target);

        Assert.That(allowed, Is.True);
        Assert.That(adapterUriSeen[^1], Is.EqualTo(baseUri + "customers"), "the stage must run while the old URL is still current");
        Assert.That(h.Commits, Has.Count.EqualTo(commitsBefore + 1));
        Assert.That(h.Store.Current!.Path, Is.EqualTo("/customers/42"));
        Assert.That(h.Store.Current.Trigger, Is.EqualTo(NavigationTrigger.Programmatic));
        Assert.That(RouteTestKit.Names(h.Store.Current.Chain!), Is.EqualTo(new[] { "customers", "customer-details" }));
    }

    [Test]
    public async Task A_rooted_target_outside_the_base_path_is_allowed_and_ignored()
    {
        using var h = Create(baseUri: "http://localhost/myapp/", uri: "http://localhost/myapp/customers");
        await h.Coordinator.NavigateInitialAsync();

        Assert.That(await h.Adapter.NavigateAsync("/other"), Is.True);

        Assert.That(h.Commits, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task A_fragment_only_change_and_the_same_location_do_not_start_a_navigation()
    {
        using var h = Create(uri: "http://localhost/customers/42");
        await h.Coordinator.NavigateInitialAsync();

        await h.Adapter.NavigateAsync("customers/42#top");
        await h.Adapter.NavigateAsync("customers/42");

        Assert.That(h.Commits, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task A_query_only_change_commits_a_new_state_with_the_new_query()
    {
        using var h = Create(uri: "http://localhost/customers/42?tab=a");
        await h.Coordinator.NavigateInitialAsync();

        await h.Adapter.NavigateAsync("customers/42?tab=b");

        Assert.That(h.Commits, Has.Count.EqualTo(2));
        Assert.That(h.Store.Current!.Query, Is.EqualTo("?tab=b"));
        Assert.That(h.Store.Current.Path, Is.EqualTo("/customers/42"));
    }

    // ---- supersession and cancellation -------------------------------------------------------------------------

    [Test]
    public async Task A_newer_navigation_supersedes_an_unfinished_one_and_the_stale_result_never_commits()
    {
        var gate = new TaskCompletionSource();
        var gateStage = new DelegateStage(async context =>
        {
            if (context.To.EndsWith("/customers/1", StringComparison.Ordinal))
            {
                await gate.Task;
            }

            return StageResult.Continue;
        });
        using var h = Create(stages: [gateStage]);
        await h.Coordinator.NavigateInitialAsync();

        var first = h.Adapter.NavigateAsync("customers/1");
        var second = await h.Adapter.NavigateAsync("customers/2");
        gate.SetResult();
        var firstAllowed = await first;

        Assert.That(second, Is.True);
        Assert.That(firstAllowed, Is.False, "the superseded navigation is prevented");
        Assert.That(h.Store.Current!.Path, Is.EqualTo("/customers/2"));
        Assert.That(h.Commits.Select(c => c.Path), Is.EqualTo(new[] { "/", "/customers/2" }));
    }

    [Test]
    public async Task The_stage_token_is_cancelled_when_a_newer_navigation_starts()
    {
        CancellationToken captured = default;
        var gate = new TaskCompletionSource();
        var gateStage = new DelegateStage(async context =>
        {
            if (context.To.EndsWith("/customers/1", StringComparison.Ordinal))
            {
                captured = context.CancellationToken;
                await gate.Task;
            }

            return StageResult.Continue;
        });
        using var h = Create(stages: [gateStage]);
        await h.Coordinator.NavigateInitialAsync();

        var first = h.Adapter.NavigateAsync("customers/1");
        Assert.That(captured.IsCancellationRequested, Is.False);
        await h.Adapter.NavigateAsync("customers/2");

        Assert.That(captured.IsCancellationRequested, Is.True);
        gate.SetResult();
        await first;
    }

    [Test]
    public async Task A_stage_that_cancels_prevents_the_navigation()
    {
        using var h = Create(stages: [new DelegateStage(_ => ValueTask.FromResult(StageResult.Cancel))]);
        await h.Coordinator.NavigateInitialAsync();
        var commits = h.Commits.Count;

        var allowed = await h.Adapter.NavigateAsync("customers");

        Assert.That(allowed, Is.False);
        Assert.That(h.Commits, Has.Count.EqualTo(commits));
    }

    [Test]
    public async Task A_stage_that_throws_prevents_the_navigation()
    {
        using var h = Create(stages: [new DelegateStage(_ => throw new InvalidOperationException("boom"))]);
        var initial = await h.Coordinator.NavigateInitialAsync();

        Assert.That(initial, Is.EqualTo(NavigationOutcome.Failed));
        Assert.That(await h.Adapter.NavigateAsync("customers"), Is.False);

        // The failed first navigation commits an error state so the router never waits forever; nothing else commits.
        var committed = h.Commits.Single();
        Assert.That(committed.Failure, Is.EqualTo(RouteFailure.NavigationFailed));
        Assert.That(committed.Error!.Kind, Is.EqualTo(RouteErrorKind.NavigationFailed));
        Assert.That(committed.Error.Exception, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task An_outside_cancellation_prevents_the_navigation()
    {
        using var cts = new CancellationTokenSource();
        var cancelStage = new DelegateStage(_ =>
        {
            cts.Cancel();

            return ValueTask.FromResult(StageResult.Continue);
        });
        using var h = Create(stages: [cancelStage]);

        var allowed = await h.Adapter.NavigateAsync("customers", cancellationToken: cts.Token);

        Assert.That(allowed, Is.False);
        Assert.That(h.Commits, Is.Empty);
    }

    [Test]
    public async Task A_stage_that_throws_a_cancellation_the_navigation_did_not_ask_for_is_a_failure()
    {
        using var h = Create(stages: [new DelegateStage(_ => throw new OperationCanceledException())]);

        var initial = await h.Coordinator.NavigateInitialAsync();

        Assert.That(initial, Is.EqualTo(NavigationOutcome.Failed), "nobody canceled the navigation, so this is not a Cancelled outcome");
        Assert.That(h.Store.Current!.Error!.Kind, Is.EqualTo(RouteErrorKind.NavigationFailed));
    }

    [Test]
    public async Task A_throwing_cancellation_callback_of_the_previous_navigation_fails_only_the_new_one()
    {
        var firstGate = new TaskCompletionSource();
        var armed = false;
        var stage = new DelegateStage(async context =>
        {
            if (armed)
            {
                armed = false;
                context.CancellationToken.Register(() => throw new InvalidOperationException("bad callback"));
                await firstGate.Task;
            }

            return StageResult.Continue;
        });
        using var h = Create(stages: [stage]);
        await h.Coordinator.NavigateInitialAsync();
        armed = true;

        var first = h.Adapter.NavigateAsync("customers", intercepted: true);
        var second = await h.Adapter.NavigateAsync("customers/5", intercepted: true);
        firstGate.SetResult();

        Assert.That(second, Is.False, "canceling the previous navigation threw, which fails the new navigation instead of crashing the router");
        Assert.That(await first, Is.False);
        Assert.That(h.Store.Current!.Path, Is.EqualTo("/"));

        var third = await h.Adapter.NavigateAsync("customers/6", intercepted: true);

        Assert.That(third, Is.True, "the router keeps working after the failure");
        Assert.That(h.Store.Current.Path, Is.EqualTo("/customers/6"));
    }

    [Test]
    public void A_router_whose_registration_failed_can_attach_again()
    {
        var adapter = new FakeNavigationAdapter { RegisterFailure = new InvalidOperationException("cannot register") };
        var store = new RouteStateStore();
        var coordinator = new NavigationCoordinator(adapter, store, RouteTestKit.Frozen(RouteTestKit.CustomersTable()), new RouteComponentBinder(), new ServiceCollection().BuildServiceProvider(), new ModRouterOptions(), [], NullLoggerFactory.Instance);

        Assert.Throws<InvalidOperationException>(() => coordinator.Attach());

        adapter.RegisterFailure = null;
        using var lease = coordinator.Attach();

        Assert.That(adapter.HandlerCount, Is.EqualTo(1), "the failed attempt must not leave the router marked as attached");
    }

    // ---- fallback and attachment -------------------------------------------------------------------------------

    [Test]
    public async Task A_location_change_the_router_never_saw_is_run_through_the_pipeline_and_committed()
    {
        using var h = Create();
        await h.Coordinator.NavigateInitialAsync();

        h.Adapter.RaiseLocationChanged("customers/7");

        Assert.That(h.Store.Current!.Path, Is.EqualTo("/customers/7"));
        Assert.That(RouteTestKit.Names(h.Store.Current.Chain!), Is.EqualTo(new[] { "customers", "customer-details" }));
    }

    [Test]
    public void Only_one_router_can_attach_and_disposing_detaches_it()
    {
        using var h = Create();

        Assert.That(h.Adapter.HandlerCount, Is.EqualTo(1));
        Assert.Throws<InvalidOperationException>(() => h.Coordinator.Attach());

        h.Lease!.Dispose();
        h.Lease = null;

        Assert.That(h.Adapter.HandlerCount, Is.EqualTo(0));
        using var again = h.Coordinator.Attach();
        Assert.That(h.Adapter.HandlerCount, Is.EqualTo(1));
    }

    [Test]
    public async Task A_detached_coordinator_ignores_location_changes()
    {
        using var h = Create();
        await h.Coordinator.NavigateInitialAsync();
        h.Lease!.Dispose();
        h.Lease = null;
        var commits = h.Commits.Count;

        h.Adapter.RaiseLocationChanged("customers/7");

        Assert.That(h.Commits, Has.Count.EqualTo(commits));
    }

    [Test]
    public async Task Detaching_while_a_navigation_is_in_flight_prevents_it_and_commits_nothing()
    {
        var gate = new TaskCompletionSource();
        var gateStage = new DelegateStage(async context =>
        {
            if (context.Trigger != NavigationTrigger.InitialLoad)
            {
                await gate.Task;
            }

            return StageResult.Continue;
        });
        using var h = Create(stages: [gateStage]);
        await h.Coordinator.NavigateInitialAsync();

        var inFlight = h.Adapter.NavigateAsync("customers/1");
        h.Lease!.Dispose();
        h.Lease = null;
        gate.SetResult();

        Assert.That(await inFlight, Is.False, "the in-flight navigation is prevented when the router detaches");
        Assert.That(h.Commits, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task A_prepared_navigation_that_another_handler_prevented_never_commits_later()
    {
        using var h = Create();
        await h.Coordinator.NavigateInitialAsync();
        var blocker = h.Adapter.RegisterLocationChanging(_ => ValueTask.FromResult(LocationChangingDecision.Prevent));

        // The router prepared /customers/1 and allowed it, but the platform never changed the location.
        Assert.That(await h.Adapter.NavigateAsync("customers/1"), Is.False);
        blocker.Dispose();

        h.Adapter.RaiseLocationChanged("customers/7");

        Assert.That(h.Commits.Select(c => c.Path), Is.EqualTo(new[] { "/", "/customers/7" }), "the stale prepared state is dropped, not committed");
    }

    // ---- additional stages and binding -------------------------------------------------------------------------

    [Test]
    public async Task Additional_stages_run_after_the_built_in_ones_and_see_the_recognized_chain()
    {
        string? leaf = null;
        string? path = null;
        var stage = new DelegateStage(context =>
        {
            leaf = context.MatchChain?.Leaf.RouteName;
            path = context.Location.Path;

            return ValueTask.FromResult(StageResult.Continue);
        });
        using var h = Create(stages: [stage]);
        await h.Coordinator.NavigateInitialAsync();

        await h.Adapter.NavigateAsync("customers/42");

        Assert.That(leaf, Is.EqualTo("customer-details"));
        Assert.That(path, Is.EqualTo("/customers/42"));
    }

    [Test]
    public async Task A_route_value_that_cannot_convert_to_the_component_parameter_is_a_binding_failure()
    {
        var table = RouteTestKit.Compile(map => map.Route("/g/{id}", typeof(GuidIdPage), name: "g"));
        using var h = Create(table: table);
        await h.Coordinator.NavigateInitialAsync();

        await h.Adapter.NavigateAsync("g/abc");
        var failed = h.Store.Current!;
        await h.Adapter.NavigateAsync("g/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77");

        Assert.That(failed.Failure, Is.EqualTo(RouteFailure.ParameterBindingFailed));
        Assert.That(failed.Chain, Is.Null);
        Assert.That(h.Store.Current!.Failure, Is.EqualTo(RouteFailure.None));
        Assert.That(h.Store.Current.Chain!.Leaf.RouteName, Is.EqualTo("g"));
    }
}
