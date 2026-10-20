using ModRouter.Core.Guards;
using ModRouter.Diagnostics;
using ModRouter.Navigation;
using Microsoft.Extensions.Logging;

namespace ModRouter.Test;

/// <summary>
/// A first load and a reload commit their prepared state directly, with no location change. The state they commit must
/// be their own: a <c>NavigatingChanged</c> subscriber that navigates when the flag drops runs before that commit unless
/// the flag stays on until it, and anything that runs between the preparation and the commit (a newer navigation, a
/// location change, the router's disposal) leaves a pending state that belongs to someone else, or none.
/// </summary>
[TestFixture]
public sealed class CommitOwnershipTests
{
    const string Base = "http://localhost/";

    /// <summary>A stage that holds the navigation while <see cref="Gate"/> is set, and lets every other one through.</summary>
    sealed class HoldStage : INavigationStage
    {
        public TaskCompletionSource<StageResult>? Gate { get; set; }

        public ValueTask<StageResult> RunAsync(RouteNavigationContext context)
            => Gate is { } gate ? new ValueTask<StageResult>(gate.Task) : ValueTask.FromResult(StageResult.Continue);
    }

    /// <summary>
    /// A synchronization context that only queues: the test runs the queued work on its own thread with no context set,
    /// so every await that captured this context resumes through the queue instead of inline. That opens the gap
    /// between a direct navigation's preparation and its caller's commit, which the test then fills.
    /// </summary>
    sealed class QueueContext : SynchronizationContext
    {
        readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

        /// <summary>Runs queued work until the next item resumes the async method <paramref name="method"/>; false when the queue ran dry first.</summary>
        public bool RunUntilContinuationOf(string method)
        {
            while (_queue.TryPeek(out var item))
            {
                if (Resumes(item.State, method))
                {
                    return true;
                }

                _queue.Dequeue();
                item.Callback(item.State);
            }

            return false;
        }

        public void RunAll()
        {
            while (_queue.TryDequeue(out var item))
            {
                item.Callback(item.State);
            }
        }

        // An await continuation is posted as the state machine box's MoveNext action; the box's type names the method.
        static bool Resumes(object? state, string method)
            => state is Delegate { Target: { } box } && box.GetType().GetGenericArguments().Any(type => type.Name.StartsWith($"<{method}>", StringComparison.Ordinal));
    }

    /// <summary>Starts <paramref name="start"/> under a <see cref="QueueContext"/>, releases <paramref name="stage"/>, and runs the queue up to the caller's continuation.</summary>
    static (Task<NavigationOutcome> Outcome, QueueContext Queue) PrepareAndStopBeforeTheCommit(HoldStage stage, Func<Task<NavigationOutcome>> start, string caller)
    {
        var gate = new TaskCompletionSource<StageResult>();
        stage.Gate = gate;
        var queue = new QueueContext();
        Task<NavigationOutcome> outcome;
        SynchronizationContext.SetSynchronizationContext(queue);

        try
        {
            outcome = start();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(null);
        }

        stage.Gate = null;
        gate.SetResult(StageResult.Continue);
        Assert.That(queue.RunUntilContinuationOf(caller), Is.True, $"the queue holds {caller}'s continuation: the navigation is prepared and not yet committed");
        Assert.That(outcome.IsCompleted, Is.False);

        return (outcome, queue);
    }

    /// <summary>A second location-changing handler, standing in for a NavigationLock: it holds every navigation the router allowed until the test decides.</summary>
    static TaskCompletionSource<LocationChangingDecision> Lock(Pipeline p)
    {
        var gate = new TaskCompletionSource<LocationChangingDecision>();
        p.Adapter.RegisterLocationChanging(_ => new ValueTask<LocationChangingDecision>(gate.Task));

        return gate;
    }

    /// <summary>Navigates to <c>login</c> the first time the flag drops after <paramref name="armed"/> returns true.</summary>
    static void NavigateWhenTheFlagDrops(Pipeline p, Func<bool> armed)
    {
        var fired = false;
        p.Store.NavigatingChanged += () =>
        {
            if (!p.Store.IsNavigating && armed() && !fired)
            {
                fired = true;
                p.Adapter.NavigateTo("login");
            }
        };
    }

    // ---- a NavigatingChanged subscriber that navigates --------------------------------------------------------------

    [TestCase(LocationChangingDecision.Prevent, "/")]
    [TestCase(LocationChangingDecision.Allow, "/login")]
    public async Task A_subscriber_that_navigates_when_the_first_navigation_ends_does_not_commit_its_state_early(LocationChangingDecision decision, string shown)
    {
        using var p = Pipeline.Create(uri: Base);
        var gate = Lock(p);
        NavigateWhenTheFlagDrops(p, () => true);

        var outcome = await p.Coordinator.NavigateInitialAsync();

        Assert.That(outcome, Is.EqualTo(NavigationOutcome.Prepared));
        Assert.That(p.Commits.Select(c => c.Path), Is.EqualTo(new[] { "/" }), "the first navigation committed its own state");

        gate.SetResult(decision);
        await p.Adapter.SettledAsync();

        Assert.Multiple(() =>
        {
            Assert.That(p.Adapter.Uri, Is.EqualTo(Base + shown.TrimStart('/')));
            Assert.That(p.Current.Path, Is.EqualTo(shown), "the screen shows the location the platform shows");
            Assert.That(p.Store.IsNavigating, Is.False);
        });
    }

    [TestCase(LocationChangingDecision.Prevent, "/")]
    [TestCase(LocationChangingDecision.Allow, "/login")]
    public async Task A_subscriber_that_navigates_when_a_reload_ends_does_not_commit_its_state_early(LocationChangingDecision decision, string shown)
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/", typeof(HomePage), name: "home").Guard<AuthGuard>()
            .Route("/login", typeof(OtherPage), name: "login"));
        using var p = Pipeline.Create(table, uri: Base);
        p.Guards.Set("auth", RouteGuardResult.NotAuthorized("Sign in first."));
        await p.Coordinator.NavigateInitialAsync();
        var error = p.Current.Error!;
        p.Guards.Set("auth", RouteGuardResult.Allow());

        var gate = Lock(p);
        var armed = false;
        NavigateWhenTheFlagDrops(p, () => armed);
        armed = true;

        // RouteError.Retry reloads the current URL; the reload commits directly.
        await error.Retry();

        Assert.That(p.Commits.Select(c => (c.Path, c.Trigger, c.Error is null)), Is.EqualTo(new[] { ("/", NavigationTrigger.InitialLoad, false), ("/", NavigationTrigger.Reload, true) }), "the reload committed its own state");

        gate.SetResult(decision);
        await p.Adapter.SettledAsync();

        Assert.Multiple(() =>
        {
            Assert.That(p.Adapter.Uri, Is.EqualTo(Base + shown.TrimStart('/')));
            Assert.That(p.Current.Path, Is.EqualTo(shown), "the screen shows the location the platform shows");
            Assert.That(p.Store.IsNavigating, Is.False);
        });
    }

    [Test]
    public async Task A_subscriber_navigation_that_commits_inside_the_flag_drop_is_not_overwritten_by_the_first_load()
    {
        // No lock: the fake platform completes the subscriber's navigation synchronously, so it commits while the first
        // load's commit is still raising the flag drop. The first load must not then write its own state over it.
        var log = new LogCapture();
        using var p = Pipeline.Create(uri: Base, loggerFactory: log, configure: o => o.Diagnostics.HistorySize = 20);
        NavigateWhenTheFlagDrops(p, () => true);

        var outcome = await p.Coordinator.NavigateInitialAsync();
        await p.Adapter.SettledAsync();

        Assert.Multiple(() =>
        {
            Assert.That(p.Adapter.Uri, Is.EqualTo(Base + "login"));
            Assert.That(p.Current.Path, Is.EqualTo("/login"), "the screen shows the location the platform shows");
            Assert.That(p.Commits.Select(c => c.Path), Is.EqualTo(new[] { "/login" }), "the first load committed nothing over the newer state");
            Assert.That(outcome, Is.EqualTo(NavigationOutcome.Superseded));
            Assert.That(log.WithId(1002).Select(e => e["NavigationId"]), Is.EqualTo(new object[] { 1L }), "NavigationSuperseded for the first load");
            Assert.That(p.Diagnostics.History.Where(r => r.NavigationId == 1).Select(r => r.Outcome), Is.EqualTo(new[] { NavigationRecordOutcome.Superseded }), "the first load's record ends once, superseded");
            Assert.That(p.Store.IsNavigating, Is.False);
        });
    }

    [Test]
    public async Task A_subscriber_navigation_that_commits_inside_the_flag_drop_is_not_overwritten_by_a_reload()
    {
        using var p = Pipeline.Create(uri: Base, configure: o => o.Diagnostics.HistorySize = 20);
        await p.Coordinator.NavigateInitialAsync();
        var armed = false;
        NavigateWhenTheFlagDrops(p, () => armed);
        armed = true;

        var outcome = await p.Coordinator.ReloadAsync();
        await p.Adapter.SettledAsync();

        Assert.Multiple(() =>
        {
            Assert.That(p.Adapter.Uri, Is.EqualTo(Base + "login"));
            Assert.That(p.Current.Path, Is.EqualTo("/login"), "the screen shows the location the platform shows");
            Assert.That(p.Commits.Select(c => (c.Path, c.Trigger)), Is.EqualTo(new[] { ("/", NavigationTrigger.InitialLoad), ("/login", NavigationTrigger.Programmatic) }), "the reload committed nothing over the newer state");
            Assert.That(outcome, Is.EqualTo(NavigationOutcome.Superseded));
            Assert.That(p.Diagnostics.History.Where(r => r.Trigger == NavigationTrigger.Reload).Select(r => r.Outcome), Is.EqualTo(new[] { NavigationRecordOutcome.Superseded }));
            Assert.That(p.Store.IsNavigating, Is.False);
        });
    }

    [Test]
    public async Task A_subscriber_navigation_that_commits_inside_the_flag_drop_is_not_overwritten_by_a_navigation_whose_hook_returned()
    {
        var log = new LogCapture();
        using var p = Pipeline.Create(uri: Base, loggerFactory: log, configure: o => o.Diagnostics.HistorySize = 20);
        await p.Coordinator.NavigateInitialAsync();
        var hook = new TaskCompletionSource();
        p.Coordinator.OnNavigate = (target, _) => target.Contains("login", StringComparison.Ordinal) ? Task.CompletedTask : hook.Task;
        var armed = false;
        NavigateWhenTheFlagDrops(p, () => armed);

        var link = p.Adapter.NavigateAsync("customers/42", intercepted: true);
        await link;
        Assert.That(p.Store.IsNavigating, Is.True, "the link waits on its hook");
        armed = true;
        hook.SetResult();
        await p.Adapter.SettledAsync();

        Assert.Multiple(() =>
        {
            Assert.That(p.Adapter.Uri, Is.EqualTo(Base + "login"));
            Assert.That(p.Current.Path, Is.EqualTo("/login"), "the screen shows the location the platform shows");
            Assert.That(p.Commits.Select(c => c.Path), Is.EqualTo(new[] { "/", "/login" }), "the link committed nothing over the newer state");
            Assert.That(p.Diagnostics.History.Where(r => r.NavigationId == 2).Select(r => r.Outcome), Is.EqualTo(new[] { NavigationRecordOutcome.Superseded }));
            Assert.That(log.WithId(1002).Select(e => e["NavigationId"]), Is.EqualTo(new object[] { 2L }));
            Assert.That(p.Store.IsNavigating, Is.False);
        });
    }

    [Test]
    public async Task Subscribers_see_the_flag_drop_before_the_change_for_a_first_load_a_link_and_a_reload()
    {
        using var p = Pipeline.Create(uri: Base);
        var events = new List<string>();
        p.Store.NavigatingChanged += () => events.Add($"navigating:{p.Store.IsNavigating}");
        p.Store.Changed += state => events.Add($"changed:{state.Path}");

        await p.Coordinator.NavigateInitialAsync();
        Assert.That(events, Is.EqualTo(new[] { "navigating:True", "navigating:False", "changed:/" }), "first load");

        events.Clear();
        await p.GoAsync("login");
        Assert.That(events, Is.EqualTo(new[] { "navigating:True", "navigating:False", "changed:/login" }), "link");

        events.Clear();
        await p.Coordinator.ReloadAsync();
        Assert.That(events, Is.EqualTo(new[] { "navigating:True", "navigating:False", "changed:/login" }), "reload");
    }

    // ---- whatever runs between a direct navigation's preparation and its commit -------------------------------------

    [Test]
    public void CommitPending_ignores_a_pending_state_that_belongs_to_another_navigation()
    {
        var stage = new HoldStage();
        var log = new LogCapture();
        using var p = Pipeline.Create(uri: Base, stages: [stage], loggerFactory: log, configure: o => o.Diagnostics.HistorySize = 20);
        var (initial, queue) = PrepareAndStopBeforeTheCommit(stage, () => p.Coordinator.NavigateInitialAsync(), nameof(NavigationCoordinator.NavigateInitialAsync));

        // A newer navigation takes over the pending slot before the first one's caller commits; a lock holds its location change.
        var gate = Lock(p);
        var second = p.Adapter.NavigateAsync("login", intercepted: true);
        queue.RunAll();

        Assert.That(initial.IsCompleted, Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(initial.Result, Is.EqualTo(NavigationOutcome.Superseded));
            Assert.That(p.Commits, Is.Empty, "the second navigation's state was not committed by the first one's caller");
            Assert.That(log.WithId(1002).Select(e => e["NavigationId"]), Is.EqualTo(new object[] { 1L }), "NavigationSuperseded (Debug) for the first navigation");
            Assert.That(log.WithId(1002).Single().Level, Is.EqualTo(LogLevel.Debug));
            var first = p.Diagnostics.History.Where(r => r.NavigationId == 1).ToList();
            Assert.That(first.Select(r => (r.Outcome, r.OutcomeDetail)), Is.EqualTo(new[] { (NavigationRecordOutcome.Superseded, "released before its location change") }), "the first navigation's record ends once, superseded");
        });

        gate.SetResult(LocationChangingDecision.Allow);
        Assert.That(second.IsCompleted && second.Result, Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(p.Commits.Select(c => (c.Path, c.Trigger)), Is.EqualTo(new[] { ("/login", NavigationTrigger.Link) }), "the second navigation commits on its own location change");
            Assert.That(p.Store.IsNavigating, Is.False);
            Assert.That(p.Diagnostics.History.Count(r => r.NavigationId == 1), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task A_reload_whose_pending_state_a_location_change_released_before_its_commit_is_superseded_and_settles_the_flag()
    {
        var stage = new HoldStage();
        using var p = Pipeline.Create(uri: Base, stages: [stage], configure: o => o.Diagnostics.HistorySize = 20);
        await p.Coordinator.NavigateInitialAsync();
        var committed = p.Current;
        var (reload, queue) = PrepareAndStopBeforeTheCommit(stage, () => p.Coordinator.ReloadAsync(), nameof(NavigationCoordinator.ReloadAsync));

        // A location outside the base (no navigation runs for it) releases the reload's pending state.
        p.Adapter.RaiseLocationChanged("http://elsewhere.example/");
        queue.RunAll();

        Assert.Multiple(() =>
        {
            Assert.That(reload.Result, Is.EqualTo(NavigationOutcome.Superseded));
            Assert.That(p.Current, Is.SameAs(committed), "nothing new committed");
            Assert.That(p.Store.IsNavigating, Is.False, "the flag the reload kept on for its commit is turned off");
            Assert.That(p.Diagnostics.History.Where(r => r.Trigger == NavigationTrigger.Reload).Select(r => r.Outcome), Is.EqualTo(new[] { NavigationRecordOutcome.Superseded }));
        });
    }

    [Test]
    public async Task A_reload_whose_own_location_change_committed_it_before_its_caller_reports_it_as_prepared()
    {
        var stage = new HoldStage();
        using var p = Pipeline.Create(uri: Base, stages: [stage], configure: o => o.Diagnostics.HistorySize = 20);
        await p.Coordinator.NavigateInitialAsync();
        var (reload, queue) = PrepareAndStopBeforeTheCommit(stage, () => p.Coordinator.ReloadAsync(), nameof(NavigationCoordinator.ReloadAsync));

        // The platform reports the reload's own URL (a fragment jump on the page): its pending state commits there.
        p.Adapter.RaiseLocationChanged(Base + "#top");
        queue.RunAll();

        Assert.Multiple(() =>
        {
            Assert.That(reload.Result, Is.EqualTo(NavigationOutcome.Prepared), "it did commit");
            Assert.That(p.Commits, Has.Count.EqualTo(2));
            Assert.That(p.Store.IsNavigating, Is.False);
            Assert.That(p.Diagnostics.History.Where(r => r.Trigger == NavigationTrigger.Reload).Select(r => r.Outcome), Is.EqualTo(new[] { NavigationRecordOutcome.Committed }));
        });
    }

    [Test]
    public async Task A_reload_whose_own_location_change_handed_it_to_OnNavigateAsync_is_not_reported_as_superseded()
    {
        var stage = new HoldStage();
        var log = new LogCapture();
        using var p = Pipeline.Create(uri: Base, stages: [stage], loggerFactory: log, configure: o => o.Diagnostics.HistorySize = 20);
        await p.Coordinator.NavigateInitialAsync();
        var hook = new TaskCompletionSource();
        p.Coordinator.OnNavigate = (_, _) => hook.Task;
        var (reload, queue) = PrepareAndStopBeforeTheCommit(stage, () => p.Coordinator.ReloadAsync(), nameof(NavigationCoordinator.ReloadAsync));

        // The platform reports the reload's own URL: its pending state now waits on the hook, which commits it.
        p.Adapter.RaiseLocationChanged(Base + "#top");
        queue.RunAll();

        Assert.Multiple(() =>
        {
            Assert.That(reload.Result, Is.EqualTo(NavigationOutcome.Prepared), "it is still on its way to the commit");
            Assert.That(log.WithId(1002), Is.Empty, "nothing was superseded");
            Assert.That(p.Store.IsNavigating, Is.True, "the hook still runs");
        });

        hook.SetResult();

        Assert.Multiple(() =>
        {
            Assert.That(p.Commits, Has.Count.EqualTo(2));
            Assert.That(p.Store.IsNavigating, Is.False);
            Assert.That(p.Diagnostics.History.Where(r => r.Trigger == NavigationTrigger.Reload).Select(r => r.Outcome), Is.EqualTo(new[] { NavigationRecordOutcome.Committed }));
        });
    }

    [Test]
    public async Task A_reload_whose_router_was_disposed_before_its_commit_is_canceled()
    {
        var stage = new HoldStage();
        using var p = Pipeline.Create(uri: Base, stages: [stage], configure: o => o.Diagnostics.HistorySize = 20);
        await p.Coordinator.NavigateInitialAsync();
        var committed = p.Current;
        var (reload, queue) = PrepareAndStopBeforeTheCommit(stage, () => p.Coordinator.ReloadAsync(), nameof(NavigationCoordinator.ReloadAsync));

        p.Lease!.Dispose();
        queue.RunAll();

        Assert.Multiple(() =>
        {
            Assert.That(reload.Result, Is.EqualTo(NavigationOutcome.Cancelled));
            Assert.That(p.Current, Is.SameAs(committed));
            Assert.That(p.Store.IsNavigating, Is.False);
            Assert.That(p.Diagnostics.History.Where(r => r.Trigger == NavigationTrigger.Reload).Select(r => (r.Outcome, r.OutcomeDetail)), Is.EqualTo(new[] { (NavigationRecordOutcome.Canceled, "the router was disposed") }));
        });
    }
}
