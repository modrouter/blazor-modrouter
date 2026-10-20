using System.Reflection;
using ModRouter.Compatibility;
using ModRouter.Components;
using ModRouter.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NavigationContext = Microsoft.AspNetCore.Components.Routing.NavigationContext;

namespace ModRouter.Test;

/// <summary>
/// The coordinator's <c>OnNavigate</c> hook (the compatibility Router's <c>OnNavigateAsync</c>): it runs after the
/// location changed and before the commit, with the new absolute URI and a token the next location change cancels; a
/// superseded navigation never commits. Plus the framework <c>NavigationContext</c> factory and the assembly snapshot.
/// </summary>
[TestFixture]
public sealed class OnNavigateHookTests
{
    sealed class ListLogger : ILoggerProvider, ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));

        public void Dispose()
        {
        }
    }

    sealed record Call(string Uri, CancellationToken Token, string PlatformUriAtCall, string? CommittedPathAtCall, bool NavigatingAtCall);

    FakeNavigationAdapter _adapter = default!;
    RouteStateStore _store = default!;
    NavigationCoordinator _coordinator = default!;
    ListLogger _log = default!;
    IDisposable? _lease;
    readonly List<Call> _calls = [];
    readonly List<RouteState> _commits = [];
    readonly Dictionary<string, TaskCompletionSource> _gates = [];

    /// <summary>The targets whose gated hook returned normally (not canceled).</summary>
    readonly List<string> _returned = [];

    [SetUp]
    public void SetUp()
    {
        _calls.Clear();
        _returned.Clear();
        _commits.Clear();
        _gates.Clear();
        _log = new ListLogger();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var gate in _gates.Values)
        {
            gate.TrySetResult();
        }

        _lease?.Dispose();
        _log.Dispose();
    }

    async Task StartAsync(string uri = "http://localhost/", bool gated = false, bool throws = false, INavigationStage? stage = null)
    {
        _adapter = new FakeNavigationAdapter("http://localhost/", uri) { ExecuteNavigateTo = true };
        _store = new RouteStateStore();
        _store.Changed += _commits.Add;
        var loggerFactory = LoggerFactory.Create(b => b.AddProvider(_log).SetMinimumLevel(LogLevel.Trace));
        _coordinator = new NavigationCoordinator(_adapter, _store, RouteTestKit.Frozen(RouteTestKit.CustomersTable()), new RouteComponentBinder(), new ServiceCollection().BuildServiceProvider(), new ModRouterOptions(), stage is null ? [] : [stage], loggerFactory);
        _coordinator.OnNavigate = async (target, token) =>
        {
            _calls.Add(new Call(target, token, _adapter.Uri, _store.Current?.Path, _store.IsNavigating));

            if (throws)
            {
                throw new InvalidOperationException("the app's OnNavigate failed");
            }

            if (gated)
            {
                var gate = _gates[target] = new TaskCompletionSource();
                await gate.Task.WaitAsync(token);
                _returned.Add(target);
            }
        };
        _lease = _coordinator.Attach();
        await _coordinator.NavigateInitialAsync();
    }

    [Test]
    public async Task The_hook_runs_after_the_location_changed_and_before_the_commit_then_the_state_commits()
    {
        await StartAsync(gated: true);

        Assert.That(_calls, Is.Empty, "the first navigation does not run the hook: the compatibility Router runs it itself, before the table exists");
        Assert.That(await _adapter.NavigateAsync("customers/42", intercepted: true), Is.True, "the location changed");

        var call = _calls.Single();
        Assert.That(call.Uri, Is.EqualTo("http://localhost/customers/42"), "the absolute target");
        Assert.That(call.PlatformUriAtCall, Is.EqualTo("http://localhost/customers/42"), "the platform already shows the new URI (NavigationManager.Uri readers rely on it)");
        Assert.That(call.CommittedPathAtCall, Is.EqualTo("/"), "the old page is still the committed state");
        Assert.That(call.NavigatingAtCall, Is.True);
        Assert.That(_store.IsNavigating, Is.True, "and stays so while the hook runs");
        Assert.That(_commits, Has.Count.EqualTo(1));

        _gates["http://localhost/customers/42"].SetResult();
        await ComponentTestKit.EventuallyAsync(() => _commits.Count, Is.EqualTo(2));

        Assert.That(_store.Current!.Path, Is.EqualTo("/customers/42"));
        Assert.That(_store.Current.Trigger, Is.EqualTo(NavigationTrigger.Link));
        Assert.That(_store.IsNavigating, Is.False);
        Assert.That(call.Token.IsCancellationRequested, Is.False);
    }

    [Test]
    public async Task A_newer_navigation_cancels_the_token_and_the_superseded_navigation_never_commits()
    {
        await StartAsync(gated: true);

        Assert.That(await _adapter.NavigateAsync("customers/1", intercepted: true), Is.True);
        Assert.That(await _adapter.NavigateAsync("customers/2", intercepted: true), Is.True);

        var older = _calls.Single(c => c.Uri.EndsWith("/1", StringComparison.Ordinal));
        var newer = _calls.Single(c => c.Uri.EndsWith("/2", StringComparison.Ordinal));
        Assert.That(older.Token.IsCancellationRequested, Is.True, "canceled when the next navigation's location changed");
        Assert.That(newer.Token.IsCancellationRequested, Is.False);
        Assert.That(_commits, Has.Count.EqualTo(1));

        _gates["http://localhost/customers/2"].SetResult();
        await ComponentTestKit.EventuallyAsync(() => _commits.Count, Is.EqualTo(2));

        Assert.That(_store.Current!.Path, Is.EqualTo("/customers/2"));
        Assert.That(_commits.Select(c => c.Path), Is.EqualTo(new[] { "/", "/customers/2" }), "the superseded navigation never committed");
        Assert.That(_log.Entries.Select(e => e.Message), Has.One.Contains("OnNavigateAsync was canceled by a newer navigation"));
    }

    [Test]
    public async Task The_token_of_a_hook_that_has_returned_is_never_canceled_by_a_later_navigation_a_reload_or_detaching()
    {
        await StartAsync(gated: true);
        Assert.That(await _adapter.NavigateAsync("customers/1", intercepted: true), Is.True);
        _gates["http://localhost/customers/1"].SetResult();
        await ComponentTestKit.EventuallyAsync(() => _commits.Count, Is.EqualTo(2));
        var returned = _calls.Single().Token;

        Assert.That(await _adapter.NavigateAsync("customers/2", intercepted: true), Is.True);
        _gates["http://localhost/customers/2"].SetResult();
        await ComponentTestKit.EventuallyAsync(() => _commits.Count, Is.EqualTo(3));
        await _coordinator.ReloadAsync();
        _lease!.Dispose();
        _lease = null;

        Assert.That(returned.IsCancellationRequested, Is.False, "the token is canceled only while its hook is running");
    }

    [Test]
    public async Task A_navigation_prevented_after_the_router_allowed_it_leaves_the_running_hook_to_finish_and_commit()
    {
        await StartAsync(gated: true);
        Assert.That(await _adapter.NavigateAsync("customers/1", intercepted: true), Is.True);
        var first = _calls.Single();

        // A NavigationLock after the router's handler prevents the next navigation once the router has prepared it.
        using (_adapter.RegisterLocationChanging(_ => ValueTask.FromResult(LocationChangingDecision.Prevent)))
        {
            Assert.That(await _adapter.NavigateAsync("customers/2", intercepted: true), Is.False);
        }

        Assert.That(first.Token.IsCancellationRequested, Is.False, "only a location change supersedes the hook");
        Assert.That(_store.IsNavigating, Is.True, "the first navigation still waits on its hook");

        _gates["http://localhost/customers/1"].SetResult();
        await ComponentTestKit.EventuallyAsync(() => _commits.Count, Is.EqualTo(2));

        Assert.That(_adapter.Uri, Is.EqualTo("http://localhost/customers/1"));
        Assert.That(_commits.Select(c => c.Path), Is.EqualTo(new[] { "/", "/customers/1" }), "the page of the location the platform shows");
        Assert.That(_store.IsNavigating, Is.False);
    }

    [Test]
    public async Task A_navigation_canceled_in_the_pipeline_leaves_the_running_hook_to_finish_and_commit()
    {
        await StartAsync(gated: true);
        Assert.That(await _adapter.NavigateAsync("customers/1", intercepted: true), Is.True);
        var first = _calls.Single();

        Assert.That(await _adapter.NavigateAsync("customers/2", intercepted: true, cancellationToken: new CancellationToken(canceled: true)), Is.False);

        Assert.That(first.Token.IsCancellationRequested, Is.False);
        _gates["http://localhost/customers/1"].SetResult();
        await ComponentTestKit.EventuallyAsync(() => _commits.Count, Is.EqualTo(2));

        Assert.That(_commits.Select(c => c.Path), Is.EqualTo(new[] { "/", "/customers/1" }));
        Assert.That(_store.IsNavigating, Is.False);
    }

    [Test]
    public async Task Reloads_do_not_run_the_hook_and_back_forward_keeps_its_trigger()
    {
        await StartAsync();

        await _coordinator.ReloadAsync();
        Assert.That(_calls, Is.Empty, "a reload is not a location change");

        await _adapter.NavigateAsync("customers/42", intercepted: true);
        await ComponentTestKit.EventuallyAsync(() => _store.Current!.Path, Is.EqualTo("/customers/42"));
        await _adapter.NavigateAsync("/", historyTraversal: true);
        await ComponentTestKit.EventuallyAsync(() => _store.Current!.Path, Is.EqualTo("/"));

        Assert.That(_calls.Select(c => c.Uri), Is.EqualTo(new[] { "http://localhost/customers/42", "http://localhost/" }));
        Assert.That(_store.Current!.Trigger, Is.EqualTo(NavigationTrigger.HistoryRestore), "the trigger decided before the hook survives it");
    }

    [Test]
    public async Task A_reload_while_the_hook_runs_reloads_the_location_the_platform_shows()
    {
        await StartAsync(gated: true);
        Assert.That(await _adapter.NavigateAsync("customers/42", intercepted: true), Is.True);
        var call = _calls.Single();

        var outcome = await _coordinator.ReloadAsync(all: true);

        Assert.That(outcome, Is.EqualTo(NavigationOutcome.Prepared));
        Assert.That(_adapter.Uri, Is.EqualTo("http://localhost/customers/42"));
        Assert.That(_commits.Select(c => (c.Path, c.Trigger)), Is.EqualTo(new[] { ("/", NavigationTrigger.InitialLoad), ("/customers/42", NavigationTrigger.Link) }), "never the old page under the new URL, and the page is new to the user: the navigation's trigger, not Reload");
        Assert.That(NavigationEffectsPlanner.Plan(_store.Current!, new ModRouterOptions()), Has.Property(nameof(NavigationEffects.IsEmpty)).False.And.Property(nameof(NavigationEffects.Announcement)).Not.Null, "so the title, announcement, focus and scroll of the new page run");
        Assert.That(call.Token.IsCancellationRequested, Is.True, "the reload replaced the navigation the hook was running for");
        Assert.That(_store.IsNavigating, Is.False);

        _gates["http://localhost/customers/42"].TrySetResult();
        Assert.That(_commits, Has.Count.EqualTo(2), "the canceled hook commits nothing");
    }

    [Test]
    public async Task A_reload_during_the_hook_keeps_a_back_forward_arrival_a_history_restore()
    {
        await StartAsync(gated: true);
        Assert.That(await _adapter.NavigateAsync("/customers/42", historyTraversal: true), Is.True);

        await _coordinator.ReloadAsync();

        Assert.That(_commits.Select(c => (c.Path, c.Trigger)), Is.EqualTo(new[] { ("/", NavigationTrigger.InitialLoad), ("/customers/42", NavigationTrigger.HistoryRestore) }), "a back/forward arrival stays one: it restores the scroll position");
    }

    [Test]
    public async Task A_reload_that_is_preparing_when_the_hook_returns_commits_once_with_the_navigations_trigger()
    {
        var hold = new HoldStage(NavigationTrigger.Reload);
        await StartAsync(gated: true, stage: hold);
        Assert.That(await _adapter.NavigateAsync("customers/42", intercepted: true), Is.True);

        var reload = _coordinator.ReloadAsync(all: true);
        Assert.That(hold.Entered, Is.EqualTo(1), "the reload is preparing");

        _gates["http://localhost/customers/42"].SetResult();
        await ComponentTestKit.EventuallyAsync(() => _returned.Count, Is.EqualTo(1));
        Assert.That(_commits, Has.Count.EqualTo(1), "the returned navigation waits: the reload that replaces it is still preparing");

        hold.Gate.SetResult();
        var outcome = await reload;

        Assert.That(outcome, Is.EqualTo(NavigationOutcome.Prepared));
        Assert.That(_commits.Select(c => (c.Path, c.Trigger)), Is.EqualTo(new[] { ("/", NavigationTrigger.InitialLoad), ("/customers/42", NavigationTrigger.Link) }), "one commit for the location, not the arrived state and then the reload");
        Assert.That(_store.IsNavigating, Is.False);
    }

    [Test]
    public async Task A_reload_that_is_canceled_while_the_hook_returned_lets_the_arrived_navigation_commit()
    {
        var hold = new HoldStage(NavigationTrigger.Reload);
        await StartAsync(gated: true, stage: hold);
        Assert.That(await _adapter.NavigateAsync("customers/42", intercepted: true), Is.True);
        using var cancellation = new CancellationTokenSource();

        var reload = _coordinator.ReloadAsync(cancellationToken: cancellation.Token);
        _gates["http://localhost/customers/42"].SetResult();
        await ComponentTestKit.EventuallyAsync(() => _returned.Count, Is.EqualTo(1));
        cancellation.Cancel();

        Assert.That(await reload, Is.EqualTo(NavigationOutcome.Cancelled));
        Assert.That(_commits.Select(c => (c.Path, c.Trigger)), Is.EqualTo(new[] { ("/", NavigationTrigger.InitialLoad), ("/customers/42", NavigationTrigger.Link) }), "the platform shows that location: its page commits once the reload is gone");
        Assert.That(_store.IsNavigating, Is.False);
    }

    [Test]
    public async Task A_throwing_hook_is_logged_and_the_navigation_commits_anyway()
    {
        await StartAsync(throws: true);

        await _adapter.NavigateAsync("customers/42", intercepted: true);
        await ComponentTestKit.EventuallyAsync(() => _store.Current!.Path, Is.EqualTo("/customers/42"));

        Assert.That(_log.Entries.Where(e => e.Level == LogLevel.Error).Select(e => e.Message), Has.One.Contains("OnNavigateAsync threw; the navigation commits anyway"));
        Assert.That(_store.IsNavigating, Is.False);
    }

    [Test]
    public async Task Detaching_cancels_a_running_hook_and_drops_its_commit()
    {
        await StartAsync(gated: true);
        await _adapter.NavigateAsync("customers/42", intercepted: true);
        var call = _calls.Single();

        _lease!.Dispose();
        _lease = null;

        Assert.That(call.Token.IsCancellationRequested, Is.True);
        _gates["http://localhost/customers/42"].TrySetResult();
        await Task.Delay(50);
        Assert.That(_commits, Has.Count.EqualTo(1), "a detached coordinator commits nothing");
        Assert.That(_coordinator.OnNavigate, Is.Null, "the hook is dropped with the lease");
    }

    // ---- the framework NavigationContext --------------------------------------------------------------------------------

    [Test]
    public void The_factory_creates_the_framework_NavigationContext_through_its_internal_constructor()
    {
        using var cancellation = new CancellationTokenSource();

        var context = NavigationContextFactory.Create("customers/42?tab=a", cancellation.Token);

        Assert.That(context, Is.InstanceOf<NavigationContext>());
        Assert.That(context.Path, Is.EqualTo("customers/42?tab=a"));
        Assert.That(context.CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(typeof(NavigationContext).GetConstructors(), Is.Empty, "the built-in type has no public constructor");
        Assert.That(typeof(NavigationContext).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, [typeof(string), typeof(CancellationToken)]), Is.Not.Null, "the internal one the accessor binds to");
    }

    [TestCase("http://localhost/", "http://localhost/", "")]
    [TestCase("http://localhost/", "http://localhost", "")]
    [TestCase("http://localhost/", "http://localhost/?mode=compat", "?mode=compat")]
    [TestCase("http://localhost/", "http://localhost/customers/42?tab=a#frag", "customers/42?tab=a#frag")]
    [TestCase("http://localhost/myapp/", "http://localhost/myapp/customers/42", "customers/42")]
    [TestCase("http://localhost/myapp/", "http://localhost/myapp", "")]
    [TestCase("http://localhost/myapp/", "http://localhost/myapp?x=1", "?x=1")]
    public void The_path_is_base_relative_without_a_leading_slash_with_query_and_fragment_like_NavigationManager(string baseUri, string uri, string expected)
        => Assert.That(NavigationContextFactory.ToBaseRelativePath(baseUri, uri), Is.EqualTo(expected));

    [Test]
    public void A_uri_outside_the_base_is_rejected_like_NavigationManager()
        => Assert.That(() => NavigationContextFactory.ToBaseRelativePath("http://localhost/myapp/", "http://localhost/other"), Throws.ArgumentException.With.Message.Contains("is not contained by the base URI"));

    // ---- the assembly snapshot --------------------------------------------------------------------------------------------

    [Test]
    public void The_snapshot_keeps_the_app_assembly_first_once_and_detects_a_later_change()
    {
        var app = typeof(OnNavigateHookTests).Assembly;
        var router = typeof(Components.ModRouter).Assembly;
        var additional = new List<Assembly> { router, app, router };

        var snapshot = AssemblySnapshot.Take(app, additional);

        Assert.That(snapshot.Assemblies, Is.EqualTo(new[] { app, router }));
        Assert.That(snapshot.Matches(app, additional), Is.True);
        Assert.That(snapshot.Matches(app, [router]), Is.True, "the same list, duplicates aside");
        Assert.That(snapshot.Matches(app, null), Is.False);
        Assert.That(snapshot.Matches(app, [router, typeof(Microsoft.AspNetCore.Components.RouteData).Assembly]), Is.False);
        Assert.That(snapshot.Describe(), Is.EqualTo("ModRouter.Test, ModRouter"));
        Assert.That(AssemblySnapshot.Take(app, null).Assemblies, Is.EqualTo(new[] { app }));
    }
}
