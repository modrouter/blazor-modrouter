using ModRouter.Navigation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModRouter.Test;

[TestFixture]
public sealed class RouteStateModelTests
{
    sealed class RecordingLogger : ILogger<RouteStateStore>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, exception));
    }

    static RouteState State(string path = "/")
        => new() { NavigationId = 1, AbsoluteUri = "http://localhost" + path, Path = path, Trigger = NavigationTrigger.Programmatic };

    [Test]
    public void A_new_state_has_no_error_no_redirects_and_no_level_data()
    {
        var state = State();

        Assert.That(state.Error, Is.Null);
        Assert.That(state.FailedLevel, Is.Null);
        Assert.That(state.RedirectedFrom, Is.Empty);
        Assert.That(state.LevelData, Is.Empty);
    }

    [Test]
    public void Changing_the_trigger_keeps_the_error_redirects_and_level_data()
    {
        var error = new RouteError(RouteErrorKind.LoadFailed, "boom", null, 2);
        var original = new RouteState
        {
            NavigationId = 7,
            AbsoluteUri = "http://localhost/a",
            Path = "/a",
            Trigger = NavigationTrigger.Link,
            Error = error,
            RedirectedFrom = ["http://localhost/old"],
            LevelData = [null],
        };

        var changed = original.WithTrigger(NavigationTrigger.HistoryRestore);

        Assert.That(changed.Trigger, Is.EqualTo(NavigationTrigger.HistoryRestore));
        Assert.That(changed.Error, Is.SameAs(error));
        Assert.That(changed.FailedLevel, Is.EqualTo(2));
        Assert.That(changed.RedirectedFrom, Is.EqualTo(new[] { "http://localhost/old" }));
        Assert.That(changed.LevelData, Has.Count.EqualTo(1));
        Assert.That(changed.NavigationId, Is.EqualTo(7));
    }

    [Test]
    public void Commit_updates_current_and_raises_changed()
    {
        var store = new RouteStateStore();
        var seen = new List<RouteState>();
        store.Changed += seen.Add;
        var state = State("/a");

        store.Commit(state);

        Assert.That(store.Current, Is.SameAs(state));
        Assert.That(seen, Is.EqualTo(new[] { state }));
    }

    [Test]
    public void A_throwing_subscriber_is_logged_and_skipped_and_the_commit_still_happens()
    {
        var logger = new RecordingLogger();
        var store = new RouteStateStore(logger);
        var second = new List<string>();
        store.Changed += _ => throw new InvalidOperationException("first");
        store.Changed += state => second.Add(state.Path);

        store.Commit(State("/a"));

        Assert.That(store.Current!.Path, Is.EqualTo("/a"));
        Assert.That(second, Is.EqualTo(new[] { "/a" }));
        Assert.That(logger.Entries.Single().Level, Is.EqualTo(LogLevel.Error));
        Assert.That(logger.Entries.Single().Exception!.Message, Is.EqualTo("first"));
    }

    [Test]
    public void A_throwing_subscriber_without_a_logger_is_still_isolated()
    {
        var store = new RouteStateStore();
        var reached = false;
        store.Changed += _ => throw new InvalidOperationException("first");
        store.Changed += _ => reached = true;

        Assert.DoesNotThrow(() => store.Commit(State()));
        Assert.That(reached, Is.True);
    }

    [Test]
    public void Navigating_raises_only_when_the_value_changes()
    {
        var store = new RouteStateStore(NullLogger<RouteStateStore>.Instance);
        var raised = new List<bool>();
        store.NavigatingChanged += () => raised.Add(store.IsNavigating);

        store.SetNavigating(false);
        store.SetNavigating(true);
        store.SetNavigating(true);
        store.SetNavigating(false);

        Assert.That(raised, Is.EqualTo(new[] { true, false }));
        Assert.That(store.IsNavigating, Is.False);
    }

    [Test]
    public void A_throwing_navigating_subscriber_does_not_stop_the_others()
    {
        var store = new RouteStateStore();
        var reached = false;
        store.NavigatingChanged += () => throw new InvalidOperationException("first");
        store.NavigatingChanged += () => reached = true;

        Assert.DoesNotThrow(() => store.SetNavigating(true));
        Assert.That(reached, Is.True);
    }

    [Test]
    public void A_route_error_knows_whether_it_belongs_to_a_chain_level()
    {
        Assert.That(new RouteError(RouteErrorKind.NotAuthorized, "m", null, 1).IsChainLevel, Is.True);
        Assert.That(new RouteError(RouteErrorKind.LoadFailed, "m", null, 1).IsChainLevel, Is.True);
        Assert.That(new RouteError(RouteErrorKind.GuardFailed, "m", null, 1).IsChainLevel, Is.True);
        Assert.That(new RouteError(RouteErrorKind.RedirectLoop, "m").IsChainLevel, Is.False);
        Assert.That(new RouteError(RouteErrorKind.NavigationFailed, "m").IsChainLevel, Is.False);
    }

    [Test]
    public async Task Retry_runs_the_bound_action_and_does_nothing_when_none_is_bound()
    {
        var error = new RouteError(RouteErrorKind.LoadFailed, "m", null, 0);
        await error.Retry();

        var calls = 0;
        error.RetryAction = () =>
        {
            calls++;

            return Task.CompletedTask;
        };
        await error.Retry();

        Assert.That(calls, Is.EqualTo(1));
    }
}
