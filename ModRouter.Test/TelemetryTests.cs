using System.Collections.Concurrent;
using System.Diagnostics;
using ModRouter.Authorization;
using ModRouter.Components;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Diagnostics;
using ModRouter.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModRouter.Test;

/// <summary>
/// The coordinator's instrumentation: one <c>Navigation</c> activity per hop (request to commit) with a span per stage and
/// one for <c>OnNavigateAsync</c>, linked redirect hops, every activity stopped; one immutable <see cref="NavigationRecord"/>
/// per navigation, published when it ends. Nothing about navigation changes, also when a listener, a subscriber or a
/// logger throws.
/// </summary>
[TestFixture]
public sealed class TelemetryTests
{
    /// <summary>Collects the router's activities (and the test's own app source) while it lives.</summary>
    sealed class Collector : IDisposable
    {
        public const string AppSource = "ModRouter.Test.App";

        readonly ActivityListener _listener;

        public Collector()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name is RouterTelemetry.SourceName or AppSource,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = Started.Enqueue,
                ActivityStopped = Stopped.Enqueue,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public ConcurrentQueue<Activity> Started { get; } = new();

        public ConcurrentQueue<Activity> Stopped { get; } = new();

        public IReadOnlyList<Activity> Navigations => [.. Started.Where(a => a.OperationName == RouterTelemetry.NavigationActivityName)];

        public IReadOnlyList<string> Children(Activity parent) => [.. Started.Where(a => a.ParentSpanId == parent.SpanId).Select(a => a.OperationName)];

        public void Clear()
        {
            Started.Clear();
            Stopped.Clear();
        }

        public void Dispose() => _listener.Dispose();
    }

    sealed class DelegateStage(Func<RouteNavigationContext, ValueTask<StageResult>> run) : INavigationStage
    {
        public ValueTask<StageResult> RunAsync(RouteNavigationContext context) => run(context);
    }

    static readonly string[] BuiltInStages = ["normalize", "recognize", "bind", "leaveguard", "redirect", "guard", "loader"];

    static Pipeline Create(int history = 20, INavigationStage? stage = null, string? uri = null, Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory = null)
        => Pipeline.Create(uri: uri, configure: o => o.Diagnostics.HistorySize = history, stages: stage is null ? null : [stage], loggerFactory: loggerFactory);

    static object? Tag(Activity activity, string name) => activity.GetTagItem(name);

    // ---- one navigation ------------------------------------------------------------------------------------------

    [Test]
    public async Task A_link_navigation_is_one_activity_with_a_span_per_stage_its_tags_and_one_committed_record()
    {
        using var collector = new Collector();
        using var p = Create();
        await p.Coordinator.NavigateInitialAsync();
        collector.Clear();

        await p.GoAsync("customers/5");

        var navigation = collector.Navigations.Single();
        Assert.Multiple(() =>
        {
            Assert.That(collector.Children(navigation), Is.EqualTo(BuiltInStages));
            Assert.That(Tag(navigation, RouterTelemetry.NavigationIdTag), Is.EqualTo(2L));
            Assert.That(Tag(navigation, RouterTelemetry.TargetTag), Is.EqualTo("/customers/5"));
            Assert.That(Tag(navigation, RouterTelemetry.TriggerTag), Is.EqualTo("Link"));
            Assert.That(Tag(navigation, RouterTelemetry.CommittedAsTag), Is.EqualTo("Link"));
            Assert.That(Tag(navigation, RouterTelemetry.RedirectCountTag), Is.EqualTo(0));
            Assert.That(Tag(navigation, RouterTelemetry.OutcomeTag), Is.EqualTo("Committed"));
            Assert.That(Tag(navigation, RouterTelemetry.OutcomeDetailTag), Is.Null);
            Assert.That(collector.Started.Where(a => a.OperationName != RouterTelemetry.NavigationActivityName).Select(a => Tag(a, RouterTelemetry.StageOutcomeTag)), Is.All.EqualTo("Continued"));
            Assert.That(collector.Stopped, Has.Count.EqualTo(collector.Started.Count), "every activity stopped");
            Assert.That(Activity.Current, Is.Null, "nothing ambient is left in the caller's flow");
        });

        var record = p.Diagnostics.History[^1];
        Assert.Multiple(() =>
        {
            Assert.That(record.NavigationId, Is.EqualTo(2));
            Assert.That(record.Target, Is.EqualTo("/customers/5"));
            Assert.That(record.Trigger, Is.EqualTo(NavigationTrigger.Link));
            Assert.That(record.CommittedAs, Is.EqualTo(NavigationTrigger.Link));
            Assert.That(record.Outcome, Is.EqualTo(NavigationRecordOutcome.Committed));
            Assert.That(record.OutcomeDetail, Is.Null);
            Assert.That(record.RedirectCount, Is.Zero);
            Assert.That(record.RedirectOrigin, Is.Null);
            Assert.That(record.Stages.Select(s => s.Name), Is.EqualTo(BuiltInStages.Append(RouterTelemetry.LocationChangeStage)));
            Assert.That(record.Duration, Is.GreaterThanOrEqualTo(TimeSpan.FromTicks(record.Stages.Sum(s => s.Duration.Ticks))));
        });
    }

    [Test]
    public async Task The_initial_load_and_a_reload_have_no_location_change_stage()
    {
        using var collector = new Collector();
        using var p = Create(uri: "http://localhost/customers/5");

        await p.Coordinator.NavigateInitialAsync();
        await p.Coordinator.ReloadAsync();

        var (initial, reload) = (p.Diagnostics.History[0], p.Diagnostics.History[1]);
        Assert.Multiple(() =>
        {
            Assert.That((initial.Trigger, initial.CommittedAs, initial.Outcome), Is.EqualTo((NavigationTrigger.InitialLoad, (NavigationTrigger?)NavigationTrigger.InitialLoad, NavigationRecordOutcome.Committed)));
            Assert.That((reload.Trigger, reload.CommittedAs, reload.Outcome), Is.EqualTo((NavigationTrigger.Reload, (NavigationTrigger?)NavigationTrigger.Reload, NavigationRecordOutcome.Committed)));
            Assert.That(initial.Stages.Select(s => s.Name), Is.EqualTo(BuiltInStages));
            Assert.That(reload.Stages.Select(s => s.Name), Is.EqualTo(BuiltInStages));
            Assert.That(collector.Navigations.Select(a => Tag(a, RouterTelemetry.TriggerTag)), Is.EqualTo(new[] { "InitialLoad", "Reload" }));
            Assert.That(collector.Stopped, Has.Count.EqualTo(collector.Started.Count));
        });
    }

    [Test]
    public async Task A_not_found_navigation_commits_with_the_NotFound_detail()
    {
        using var collector = new Collector();
        using var p = Create();
        await p.Coordinator.NavigateInitialAsync();

        await p.GoAsync("nope");

        Assert.That(p.Diagnostics.History[^1].OutcomeDetail, Is.EqualTo("NotFound"));
        Assert.That(Tag(collector.Navigations[^1], RouterTelemetry.OutcomeDetailTag), Is.EqualTo("NotFound"));
    }

    [Test]
    public async Task A_loader_failure_commits_with_the_error_kind_at_its_level()
    {
        using var p = Pipeline.Create(
            Pipeline.Table(map => map.Route("/customers", typeof(CustomersPage), name: "customers", children: c => c
                .Index(typeof(CustomerList), name: "customer-list")
                .Route("{id:int}", typeof(CustomerDetails), name: "customer").Loader<CustomerLoader>())),
            configure: o => o.Diagnostics.HistorySize = 5);
        await p.Coordinator.NavigateInitialAsync();
        p.Loaders.Set("customer", _ => throw new InvalidOperationException("no customer"));

        await p.GoAsync("customers/6");

        Assert.That(p.Diagnostics.History[^1].Outcome, Is.EqualTo(NavigationRecordOutcome.Committed));
        Assert.That(p.Diagnostics.History[^1].OutcomeDetail, Is.EqualTo("LoadFailed@1"));
    }

    // ---- redirects ----------------------------------------------------------------------------------------------

    [Test]
    public async Task Each_redirect_hop_is_its_own_activity_linked_to_the_previous_hop_and_parented_to_the_origins_parent()
    {
        using var collector = new Collector();
        using var app = new ActivitySource(Collector.AppSource);
        using var p = Create(stage: new DelegateStage(context => ValueTask.FromResult(
            context.To.EndsWith("/customers/1", StringComparison.Ordinal) ? StageResult.Redirect("customers/2")
            : context.To.EndsWith("/customers/2", StringComparison.Ordinal) ? StageResult.Redirect("customers/3")
            : StageResult.Continue)));
        await p.Coordinator.NavigateInitialAsync();
        collector.Clear();

        ActivitySpanId clickSpan;

        using (var click = app.StartActivity("click"))
        {
            clickSpan = click!.SpanId;
            await p.GoAsync("customers/1");
        }

        var hops = collector.Navigations;
        Assert.That(hops, Has.Count.EqualTo(3));
        Assert.Multiple(() =>
        {
            Assert.That(hops.Select(h => Tag(h, RouterTelemetry.OutcomeTag)), Is.EqualTo(new[] { "Redirected", "Redirected", "Committed" }));
            Assert.That(hops.Select(h => Tag(h, RouterTelemetry.RedirectCountTag)), Is.EqualTo(new object[] { 0, 1, 2 }));
            Assert.That(hops.Select(h => Tag(h, RouterTelemetry.TriggerTag)), Is.EqualTo(new[] { "Link", "Redirect", "Redirect" }));
            Assert.That(hops.Select(h => h.ParentSpanId), Is.All.EqualTo(clickSpan), "all hops share the origin's parent");
            Assert.That(hops[0].Links, Is.Empty);
            Assert.That(hops[1].Links.Single().Context.SpanId, Is.EqualTo(hops[0].SpanId));
            Assert.That(hops[2].Links.Single().Context.SpanId, Is.EqualTo(hops[1].SpanId));
            Assert.That(Tag(hops[0], RouterTelemetry.OutcomeDetailTag), Is.EqualTo("/customers/2"));
            Assert.That(collector.Stopped, Has.Count.EqualTo(collector.Started.Count));
        });

        var records = p.Diagnostics.History.Skip(1).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(records.Select(r => r.Outcome), Is.EqualTo(new[] { NavigationRecordOutcome.Redirected, NavigationRecordOutcome.Redirected, NavigationRecordOutcome.Committed }));
            Assert.That(records.Select(r => r.RedirectCount), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(records.Select(r => r.RedirectOrigin), Is.EqualTo(new[] { null, "/customers/1", "/customers/1" }));
            Assert.That(records.Select(r => r.OutcomeDetail), Is.EqualTo(new[] { "/customers/2", "/customers/3", null }));
            Assert.That(records.Select(r => r.CommittedAs), Is.EqualTo(new NavigationTrigger?[] { null, null, NavigationTrigger.Redirect }));
        });
    }

    // ---- privacy: paths only -------------------------------------------------------------------------------------

    [Test]
    public async Task Tags_and_records_carry_the_path_only_never_the_query_the_fragment_or_the_host()
    {
        using var collector = new Collector();
        using var p = Pipeline.Create(
            baseUri: "http://localhost/myapp/",
            uri: "http://localhost/myapp/",
            configure: o => o.Diagnostics.HistorySize = 20,
            stages: [new DelegateStage(context => ValueTask.FromResult(
                context.To.Contains("/customers/1?", StringComparison.Ordinal) ? StageResult.Redirect("customers/2?email=a@b.c#code=hop")
                : context.To.Contains("/customers/8", StringComparison.Ordinal) ? StageResult.Redirect("https://user:pw@login.example.com/authorize?state=s#x")
                : StageResult.Continue))]);
        await p.Coordinator.NavigateInitialAsync();
        collector.Clear();

        await p.GoAsync("customers/5?token=secret#code=abc");
        await p.GoAsync("customers/1?returnUrl=%2Fprivate");
        await p.GoAsync("customers/8?ticket=t");

        var records = p.Diagnostics.History.Skip(1).ToList();
        var everything = string.Join(" ", records.SelectMany(r => new[] { r.Target, r.RedirectOrigin, r.OutcomeDetail })
            .Concat(collector.Navigations.SelectMany(a => new[] { Tag(a, RouterTelemetry.TargetTag) as string, Tag(a, RouterTelemetry.OutcomeDetailTag) as string })));
        Assert.Multiple(() =>
        {
            Assert.That(records.Select(r => r.Target), Is.EqualTo(new[] { "/myapp/customers/5", "/myapp/customers/1", "/myapp/customers/2", "/myapp/customers/8" }));
            Assert.That(records.Select(r => r.OutcomeDetail), Is.EqualTo(new[] { null, "/myapp/customers/2", null, "external https://login.example.com/authorize" }));
            Assert.That(records[2].RedirectOrigin, Is.EqualTo("/myapp/customers/1"));
            Assert.That(collector.Navigations.Select(a => Tag(a, RouterTelemetry.TargetTag)), Is.EqualTo(records.Select(r => r.Target)));
            Assert.That(everything, Does.Not.Contain("?").And.Not.Contain("#").And.Not.Contain("secret").And.Not.Contain("pw@").And.Not.Contain("http://localhost"));
            Assert.That(p.Current.AbsoluteUri, Is.EqualTo("http://localhost/myapp/customers/2?email=a@b.c#code=hop"), "the app's own state keeps the full URI");
        });
    }

    // ---- navigations that do not commit ---------------------------------------------------------------------------

    [Test]
    public async Task Canceled_superseded_prevented_and_failed_navigations_end_their_activity_and_record_why()
    {
        using var collector = new Collector();
        var gate = new TaskCompletionSource();
        using var p = Create(stage: new DelegateStage(async context =>
        {
            if (context.To.EndsWith("/customers/9", StringComparison.Ordinal))
            {
                return StageResult.Cancel;
            }

            if (context.To.EndsWith("/customers/6", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("stage failed");
            }

            if (context.To.EndsWith("/customers/7", StringComparison.Ordinal))
            {
                await gate.Task;
            }

            return StageResult.Continue;
        }));
        await p.Coordinator.NavigateInitialAsync();

        await p.GoAsync("customers/9");
        await p.GoAsync("customers/6");

        var superseded = p.Adapter.NavigateAsync("customers/7", intercepted: true);
        await p.GoAsync("customers/8");
        gate.SetResult();
        await superseded;

        gate = new TaskCompletionSource();
        using var platform = new CancellationTokenSource();
        var prevented = p.Adapter.NavigateAsync("customers/7", intercepted: true, cancellationToken: platform.Token);
        platform.Cancel();
        gate.SetResult();
        Assert.That(await prevented, Is.False, "the platform's own cancellation prevents the navigation");

        var byId = p.Diagnostics.History.ToDictionary(r => r.NavigationId);
        Assert.Multiple(() =>
        {
            Assert.That((byId[2].Outcome, byId[2].OutcomeDetail), Is.EqualTo((NavigationRecordOutcome.Canceled, "canceled by delegate")));
            Assert.That((byId[3].Outcome, byId[3].OutcomeDetail), Is.EqualTo((NavigationRecordOutcome.Failed, "InvalidOperationException")));
            Assert.That(byId[3].Stages[^1], Is.EqualTo(byId[3].Stages[^1] with { Name = "delegate", Outcome = StageOutcome.Failed }));
            Assert.That(byId[4].Outcome, Is.EqualTo(NavigationRecordOutcome.Superseded));
            Assert.That(byId[5].Outcome, Is.EqualTo(NavigationRecordOutcome.Committed));
            Assert.That(byId[6].Outcome, Is.EqualTo(NavigationRecordOutcome.Canceled));
            Assert.That(byId.Values.Where(r => r.Outcome != NavigationRecordOutcome.Committed).Select(r => r.CommittedAs), Is.All.Null);
        });

        var failed = collector.Navigations.Single(a => Equals(Tag(a, RouterTelemetry.NavigationIdTag), 3L));
        Assert.Multiple(() =>
        {
            Assert.That(failed.Status, Is.EqualTo(ActivityStatusCode.Error));
            Assert.That(failed.Events.Select(e => e.Name), Does.Contain("exception"));
            Assert.That(collector.Navigations.Select(a => Tag(a, RouterTelemetry.OutcomeTag)), Is.EqualTo(new[] { "Committed", "Canceled", "Failed", "Superseded", "Committed", "Canceled" }));
            Assert.That(collector.Stopped, Has.Count.EqualTo(collector.Started.Count), "started equals stopped");
        });
    }

    [Test]
    public async Task A_history_traversal_is_tagged_and_recorded_with_its_final_trigger()
    {
        using var collector = new Collector();
        using var p = Create();
        await p.Coordinator.NavigateInitialAsync();

        await p.Adapter.NavigateAsync("customers/4", intercepted: false, historyTraversal: true);

        var record = p.Diagnostics.History[^1];
        Assert.That((record.Trigger, record.CommittedAs), Is.EqualTo((NavigationTrigger.Programmatic, (NavigationTrigger?)NavigationTrigger.HistoryRestore)));
        Assert.That(Tag(collector.Navigations[^1], RouterTelemetry.TriggerTag), Is.EqualTo("Programmatic"));
        Assert.That(Tag(collector.Navigations[^1], RouterTelemetry.CommittedAsTag), Is.EqualTo("HistoryRestore"));
    }

    // ---- OnNavigateAsync ----------------------------------------------------------------------------------------

    [Test]
    public async Task OnNavigateAsync_runs_inside_its_own_child_span_while_the_navigation_activity_stays_open()
    {
        using var collector = new Collector();
        using var p = Create();
        await p.Coordinator.NavigateInitialAsync();
        collector.Clear();
        Activity? seen = null;
        bool? navigationOpen = null;
        p.Coordinator.OnNavigate = (_, _) =>
        {
            seen = Activity.Current;
            navigationOpen = collector.Navigations.Single() is { IsStopped: false };

            return Task.CompletedTask;
        };

        await p.GoAsync("customers/5");

        var navigation = collector.Navigations.Single();
        Assert.Multiple(() =>
        {
            Assert.That(seen?.OperationName, Is.EqualTo(RouterTelemetry.OnNavigateActivityName));
            Assert.That(seen?.ParentSpanId, Is.EqualTo(navigation.SpanId));
            Assert.That(navigationOpen, Is.True, "the Navigation activity spans request to commit");
            Assert.That(collector.Children(navigation), Is.EqualTo(BuiltInStages.Append(RouterTelemetry.OnNavigateActivityName)));
            Assert.That(p.Diagnostics.History[^1].Stages.Select(s => s.Name), Is.EqualTo(BuiltInStages.Append(RouterTelemetry.LocationChangeStage).Append(RouterTelemetry.OnNavigateActivityName)));
            Assert.That(collector.Stopped, Has.Count.EqualTo(collector.Started.Count));
        });
    }

    [Test]
    public async Task A_reload_that_replaces_a_navigation_waiting_on_its_hook_ends_that_one_as_superseded_and_commits_as_it()
    {
        using var collector = new Collector();
        using var p = Create();
        await p.Coordinator.NavigateInitialAsync();
        var hook = new TaskCompletionSource();
        p.Coordinator.OnNavigate = async (_, token) => await hook.Task.WaitAsync(token);

        await p.Adapter.NavigateAsync("customers/5", intercepted: false);
        await p.Coordinator.ReloadAsync();
        hook.TrySetResult();

        var byId = p.Diagnostics.History.ToDictionary(r => r.NavigationId);
        Assert.Multiple(() =>
        {
            Assert.That(byId[2].Outcome, Is.EqualTo(NavigationRecordOutcome.Superseded));
            Assert.That(byId[2].Stages[^1], Is.EqualTo(byId[2].Stages[^1] with { Name = RouterTelemetry.OnNavigateActivityName, Outcome = StageOutcome.Canceled }));
            Assert.That((byId[3].Trigger, byId[3].CommittedAs, byId[3].Outcome), Is.EqualTo((NavigationTrigger.Reload, (NavigationTrigger?)NavigationTrigger.Programmatic, NavigationRecordOutcome.Committed)));
            Assert.That(collector.Stopped, Has.Count.EqualTo(collector.Started.Count));
        });
    }

    // ---- the authentication re-check -----------------------------------------------------------------------------

    [Test]
    public async Task The_authentication_recheck_reload_is_traced_and_recorded_as_a_reload()
    {
        using var collector = new Collector();
        var provider = new FakeAuthenticationStateProvider(Users.WithRoles("admin"));
        using var services = new ServiceCollection().AddTestAuthorization(provider).BuildServiceProvider();
        var map = RouteMap.Create().Route("/", typeof(HomePage), name: "home").Route("/admin", typeof(OtherPage), name: "admin").Authorize(roles: "admin");
        var table = RouteCompiler.Compile(AuthorizationRouteContributor.Apply(map.Build()));
        var options = new ModRouterOptions();
        options.Diagnostics.HistorySize = 5;
        var diagnostics = new RouterDiagnostics(options);
        var adapter = new FakeNavigationAdapter("http://localhost/", "http://localhost/admin") { ExecuteNavigateTo = true };
        var coordinator = new NavigationCoordinator(adapter, new RouteStateStore(), RouteTestKit.Frozen(table), new RouteComponentBinder(), services, options, [], NullLoggerFactory.Instance, diagnostics);
        using var lease = coordinator.Attach();
        await coordinator.NavigateInitialAsync();

        provider.User = Users.Anonymous;
        provider.Notify();
        await coordinator.AuthorizationRecheck;
        await adapter.SettledAsync();

        var reload = diagnostics.History[^1];
        Assert.That((reload.Trigger, reload.Outcome, reload.OutcomeDetail), Is.EqualTo((NavigationTrigger.Reload, NavigationRecordOutcome.Committed, "NotAuthorized@0")));
        Assert.That(collector.Navigations.Select(a => Tag(a, RouterTelemetry.TriggerTag)), Is.EqualTo(new[] { "InitialLoad", "Reload" }));
        Assert.That(collector.Stopped, Has.Count.EqualTo(collector.Started.Count));
    }

    // ---- what the records promise --------------------------------------------------------------------------------

    [Test]
    public async Task A_record_is_published_once_when_its_navigation_ends_and_is_never_in_progress()
    {
        var gate = new TaskCompletionSource();
        using var p = Create(stage: new DelegateStage(async context =>
        {
            if (context.Trigger != NavigationTrigger.InitialLoad)
            {
                await gate.Task;
            }

            return StageResult.Continue;
        }));
        var published = new List<NavigationRecord>();
        p.Diagnostics.Changed += () => published.Add(p.Diagnostics.History[^1]);
        await p.Coordinator.NavigateInitialAsync();

        var navigation = p.GoAsync("customers/5");
        Assert.That(published, Has.Count.EqualTo(1), "nothing is published while the navigation runs");
        gate.SetResult();
        await navigation;

        Assert.That(published.Select(r => r.NavigationId), Is.EqualTo(new long[] { 1, 2 }));
        Assert.That(published.Select(r => r.Outcome), Has.None.EqualTo(NavigationRecordOutcome.InProgress));
        var held = published[1].Stages.Single(s => s.Name == "delegate");
        Assert.That(held.Duration, Is.GreaterThan(TimeSpan.Zero), "the held stage's time is measured");
        Assert.That(published[1].Duration, Is.GreaterThanOrEqualTo(held.Duration));
    }

    [Test]
    public async Task Rapid_superseded_navigations_and_a_disposal_mid_navigation_leave_one_record_each_and_no_activity_open()
    {
        using var collector = new Collector();
        using var p = Create(history: 100, stage: new DelegateStage(async context =>
        {
            if (context.Trigger != NavigationTrigger.InitialLoad)
            {
                // Held until a newer navigation or the disposal cancels it.
                await new TaskCompletionSource().Task.WaitAsync(context.CancellationToken);
            }

            return StageResult.Continue;
        }));
        await p.Coordinator.NavigateInitialAsync();

        var started = Enumerable.Range(1, 10).Select(i => p.Adapter.NavigateAsync($"customers/{i}", intercepted: true)).ToList();
        p.Lease!.Dispose();
        var results = await Task.WhenAll(started);

        var history = p.Diagnostics.History;
        Assert.Multiple(() =>
        {
            Assert.That(results, Is.All.False, "none of them committed");
            Assert.That(history.Select(r => r.NavigationId).Order(), Is.EqualTo(Enumerable.Range(1, 11).Select(i => (long)i)), "one record per navigation, none missing, none twice");
            Assert.That(history.Count(r => r.Outcome == NavigationRecordOutcome.Superseded), Is.EqualTo(9));
            Assert.That(history.Single(r => r.NavigationId == 11).Outcome, Is.EqualTo(NavigationRecordOutcome.Canceled), "the disposal cancels the last one");
            Assert.That(history, Has.None.Matches<NavigationRecord>(r => r.Outcome == NavigationRecordOutcome.InProgress));
            Assert.That(collector.Stopped, Has.Count.EqualTo(collector.Started.Count), "no activity left started");
        });
    }

    [Test]
    public async Task Disposing_while_a_navigation_waits_for_its_location_change_ends_it_as_canceled_and_a_newer_one_supersedes()
    {
        using var collector = new Collector();
        using var p = Create();
        await p.Coordinator.NavigateInitialAsync();
        var platform = new TaskCompletionSource<LocationChangingDecision>();

        // Another location-changing handler (after the router's) holds the platform's decision.
        using var other = p.Adapter.RegisterLocationChanging(_ => new ValueTask<LocationChangingDecision>(platform.Task));
        var released = p.Adapter.NavigateAsync("customers/5", intercepted: true);
        var disposed = p.Adapter.NavigateAsync("customers/6", intercepted: true);
        p.Lease!.Dispose();
        platform.SetResult(LocationChangingDecision.Prevent);
        await Task.WhenAll(released, disposed);

        var byId = p.Diagnostics.History.ToDictionary(r => r.NavigationId);
        Assert.Multiple(() =>
        {
            Assert.That((byId[2].Outcome, byId[2].OutcomeDetail), Is.EqualTo((NavigationRecordOutcome.Superseded, "released before its location change")), "a newer navigation released it");
            Assert.That((byId[3].Outcome, byId[3].OutcomeDetail), Is.EqualTo((NavigationRecordOutcome.Canceled, "the router was disposed")));
            Assert.That(collector.Navigations.Select(a => Tag(a, RouterTelemetry.OutcomeTag)), Is.EqualTo(new[] { "Committed", "Superseded", "Canceled" }));
            Assert.That(collector.Stopped, Has.Count.EqualTo(collector.Started.Count));
        });
    }

    [Test]
    public async Task Disposing_while_OnNavigateAsync_runs_ends_the_navigation_as_canceled()
    {
        using var collector = new Collector();
        using var p = Create();
        await p.Coordinator.NavigateInitialAsync();
        var hook = new TaskCompletionSource();
        p.Coordinator.OnNavigate = async (_, token) => await hook.Task.WaitAsync(token);

        await p.Adapter.NavigateAsync("customers/5", intercepted: false);
        p.Lease!.Dispose();
        hook.TrySetResult();

        var record = p.Diagnostics.History[^1];
        Assert.Multiple(() =>
        {
            Assert.That((record.NavigationId, record.Outcome, record.OutcomeDetail), Is.EqualTo((2L, NavigationRecordOutcome.Canceled, "the router was disposed")));
            Assert.That(record.Stages[^1], Is.EqualTo(record.Stages[^1] with { Name = RouterTelemetry.OnNavigateActivityName, Outcome = StageOutcome.Canceled }));
            Assert.That(Tag(collector.Navigations[^1], RouterTelemetry.OutcomeTag), Is.EqualTo("Canceled"));
            Assert.That(collector.Stopped, Has.Count.EqualTo(collector.Started.Count));
        });
    }

    [Test]
    public async Task A_small_history_keeps_the_last_navigations_under_rapid_navigation()
    {
        using var p = Create(history: 3);
        await p.Coordinator.NavigateInitialAsync();

        for (var i = 1; i <= 10; i++)
        {
            await p.GoAsync($"customers/{i}");
        }

        Assert.That(p.Diagnostics.History.Select(r => r.NavigationId), Is.EqualTo(new long[] { 9, 10, 11 }));
    }

    // ---- failures in the instrumentation never change navigation ----------------------------------------------

    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task A_throwing_activity_listener_does_not_break_navigation(bool throwOnStart, bool throwOnStop)
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == RouterTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = _ =>
            {
                if (throwOnStart)
                {
                    throw new InvalidOperationException("listener");
                }
            },
            ActivityStopped = _ =>
            {
                if (throwOnStop)
                {
                    throw new InvalidOperationException("listener");
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        using var p = Create();
        await p.Coordinator.NavigateInitialAsync();

        await p.GoAsync("customers/5");

        Assert.That(p.Current.Path, Is.EqualTo("/customers/5"));
        Assert.That(p.Diagnostics.History[^1].Outcome, Is.EqualTo(NavigationRecordOutcome.Committed));
        Assert.That(Activity.Current, Is.Null);
    }

#if NET9_0_OR_GREATER
    [Test]
    public async Task A_throwing_exception_recorder_does_not_break_a_failed_navigation_and_its_activity_still_stops()
    {
        var started = new ConcurrentQueue<Activity>();
        var stopped = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == RouterTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = started.Enqueue,
            ActivityStopped = stopped.Enqueue,
            ExceptionRecorder = (Activity activity, Exception exception, ref TagList tags) => throw new InvalidOperationException("recorder"),
        };
        ActivitySource.AddActivityListener(listener);
        using var p = Create(stage: new DelegateStage(context => context.To.EndsWith("/customers/6", StringComparison.Ordinal)
            ? throw new InvalidOperationException("stage failed")
            : ValueTask.FromResult(StageResult.Continue)));
        await p.Coordinator.NavigateInitialAsync();

        await p.GoAsync("customers/6");
        await p.GoAsync("customers/5");

        var mine = started.Where(a => a.GetTagItem(RouterTelemetry.NavigationIdTag) is long).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(p.Current.Path, Is.EqualTo("/customers/5"));
            Assert.That(p.Diagnostics.History.Select(r => r.Outcome), Is.EqualTo(new[] { NavigationRecordOutcome.Committed, NavigationRecordOutcome.Failed, NavigationRecordOutcome.Committed }));
            Assert.That(mine.Where(a => Equals(a.GetTagItem(RouterTelemetry.NavigationIdTag), 2L)).Select(a => a.IsStopped), Is.EqualTo(new[] { true }), "the failed navigation's activity is stopped");
            Assert.That(stopped.Count(a => a.Source.Name == RouterTelemetry.SourceName), Is.EqualTo(started.Count(a => a.Source.Name == RouterTelemetry.SourceName)));
            Assert.That(Activity.Current, Is.Null);
        });
    }

#else
    [Test]
    public async Task On_dotnet_8_a_failed_navigation_records_the_exception_as_an_event_on_its_activity()
    {
        var started = new ConcurrentQueue<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == RouterTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = started.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        using var p = Create(stage: new DelegateStage(context => context.To.EndsWith("/customers/6", StringComparison.Ordinal)
            ? throw new InvalidOperationException("stage failed")
            : ValueTask.FromResult(StageResult.Continue)));
        await p.Coordinator.NavigateInitialAsync();

        await p.GoAsync("customers/6");

        var failed = started.Single(a => Equals(a.GetTagItem(RouterTelemetry.NavigationIdTag), 2L));
        var recorded = failed.Events.Single(e => e.Name == "exception");

        Assert.That(recorded.Tags.ToDictionary(t => t.Key, t => t.Value), Does.ContainKey("exception.type").WithValue(typeof(InvalidOperationException).FullName));
        Assert.That(failed.IsStopped, Is.True);
    }

#endif
    [Test]
    public async Task A_throwing_sampler_does_not_break_navigation()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == RouterTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => throw new InvalidOperationException("sampler"),
        };
        ActivitySource.AddActivityListener(listener);
        using var p = Create();
        await p.Coordinator.NavigateInitialAsync();

        await p.GoAsync("customers/5");

        Assert.That(p.Current.Path, Is.EqualTo("/customers/5"));
        Assert.That(p.Diagnostics.History[^1].Outcome, Is.EqualTo(NavigationRecordOutcome.Committed));
        Assert.That(Activity.Current, Is.Null);
    }

    [Test]
    public async Task A_throwing_diagnostics_subscriber_does_not_break_navigation()
    {
        using var p = Create();
        p.Diagnostics.Changed += () => throw new InvalidOperationException("subscriber");
        await p.Coordinator.NavigateInitialAsync();

        await p.GoAsync("customers/5");

        Assert.That(p.Current.Path, Is.EqualTo("/customers/5"));
        Assert.That(p.Diagnostics.History, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task A_throwing_logger_does_not_break_navigation()
    {
        using var capture = new LogCapture { Throwing = true };
        using var p = Create(loggerFactory: capture);
        await p.Coordinator.NavigateInitialAsync();

        await p.GoAsync("customers/5");
        await p.GoAsync("nope");

        Assert.That(p.Current.Failure, Is.EqualTo(RouteFailure.NotFound));
        Assert.That(capture.WithId(1001), Has.Count.EqualTo(3), "the logger was called (and threw) for each commit: the initial load and the two navigations");
    }

    // ---- without a listener --------------------------------------------------------------------------------------

    [Test]
    public async Task Without_a_listener_no_activity_exists_even_with_the_history_on()
    {
        Assume.That(TelemetryModeSetup.Current, Is.Not.EqualTo(TelemetryMode.Listener), "the listener test mode listens to every router activity");
        Activity? seen = null;
        using var p = Create(stage: new DelegateStage(_ =>
        {
            seen = Activity.Current;

            return ValueTask.FromResult(StageResult.Continue);
        }));
        await p.Coordinator.NavigateInitialAsync();

        await p.GoAsync("customers/5");

        Assert.That(RouterTelemetry.Source.HasListeners(), Is.False);
        Assert.That(seen, Is.Null);
        Assert.That(p.Diagnostics.History, Has.Count.EqualTo(2));
    }
}
