using System.Diagnostics;
using ModRouter.Diagnostics;

namespace ModRouter.Test;

/// <summary>How the whole suite runs, chosen by <see cref="TelemetryModeSetup.Variable"/>.</summary>
public enum TelemetryMode
{
    /// <summary>No listener, no history: the coordinator's null-trace path (the default).</summary>
    Plain,

    /// <summary>An <see cref="ActivityListener"/> samples every router activity for the whole run; started must equal stopped at the end.</summary>
    Listener,

    /// <summary>Every <c>ModRouterOptions</c> starts with a history of 50, so every coordinator records every navigation.</summary>
    History,
}

/// <summary>
/// Runs the whole router suite in one of three telemetry modes, so instrumentation is proven not to change navigation:
/// <c>MODROUTER_TEST_TELEMETRY=listener dotnet test ModRouter.Test</c> (or
/// <c>history</c>; unset or <c>plain</c> is the ordinary run). A few tests that are about one mode only assume it.
/// </summary>
[SetUpFixture]
public sealed class TelemetryModeSetup
{
    public const string Variable = "MODROUTER_TEST_TELEMETRY";

    static long _started;
    static long _stopped;
    ActivityListener? _listener;

    public static TelemetryMode Current { get; private set; }

    [OneTimeSetUp]
    public void Start()
    {
        Current = Environment.GetEnvironmentVariable(Variable)?.Trim().ToLowerInvariant() switch
        {
            null or "" or "plain" => TelemetryMode.Plain,
            "listener" => TelemetryMode.Listener,
            "history" => TelemetryMode.History,
            var other => throw new InvalidOperationException($"{Variable}='{other}' is not a mode: use plain, listener or history."),
        };

        if (Current == TelemetryMode.Listener)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == RouterTelemetry.SourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = _ => Interlocked.Increment(ref _started),
                ActivityStopped = _ => Interlocked.Increment(ref _stopped),
            };
            ActivitySource.AddActivityListener(_listener);
        }

        if (Current == TelemetryMode.History)
        {
            DiagnosticsOptions.DefaultHistorySize = 50;
        }

        TestContext.Progress.WriteLine($"Router telemetry test mode: {Current}");
    }

    [OneTimeTearDown]
    public void Stop()
    {
        _listener?.Dispose();
        DiagnosticsOptions.DefaultHistorySize = 0;

        if (Current == TelemetryMode.Listener)
        {
            TestContext.Progress.WriteLine($"Router activities started {Interlocked.Read(ref _started)}, stopped {Interlocked.Read(ref _stopped)}");
            Assert.That(Interlocked.Read(ref _stopped), Is.EqualTo(Interlocked.Read(ref _started)), "every router activity the suite started was stopped");
            Assert.That(Interlocked.Read(ref _started), Is.GreaterThan(1000), "the listener saw the suite's navigations");
        }
    }
}
