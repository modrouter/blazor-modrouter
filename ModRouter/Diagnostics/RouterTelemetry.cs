using System.Diagnostics;
using ModRouter.Navigation;

namespace ModRouter.Diagnostics;

/// <summary>
/// The router's tracing contract: one <see cref="ActivitySource"/> named <see cref="SourceName"/>, a
/// <see cref="NavigationActivityName"/> activity per navigation (per redirect hop: each hop links to the previous one and
/// all share the origin's parent) from the request to its end, a child activity per pipeline stage and one for
/// <c>OnNavigateAsync</c>, and the tag names below. Nothing is created unless a listener samples the source.
/// </summary>
public static class RouterTelemetry
{
    /// <summary>The name of the router's <see cref="ActivitySource"/> (subscribe to it with an <see cref="ActivityListener"/> or OpenTelemetry).</summary>
    public const string SourceName = "ModRouter";

    /// <summary>The operation name of the activity that spans one navigation.</summary>
    public const string NavigationActivityName = "Navigation";

    /// <summary>The child activity (and stage record) of the compatibility Router's <c>OnNavigateAsync</c>.</summary>
    public const string OnNavigateActivityName = "OnNavigateAsync";

    /// <summary>The stage record of the wait between a prepared navigation and the platform's location change (no activity).</summary>
    public const string LocationChangeStage = "location-change";

    /// <summary>The navigation's id (<see cref="long"/>).</summary>
    public const string NavigationIdTag = "router.navigation.id";

    /// <summary>The target's absolute path (no scheme, host, query or fragment: <c>/myapp/customers/5</c>).</summary>
    public const string TargetTag = "router.target";

    /// <summary>The trigger the navigation started with.</summary>
    public const string TriggerTag = "router.trigger";

    /// <summary>The trigger it committed as (only when it committed).</summary>
    public const string CommittedAsTag = "router.committed_as";

    /// <summary>The redirect hops before this one (<see cref="int"/>).</summary>
    public const string RedirectCountTag = "router.redirect.count";

    /// <summary>A <see cref="NavigationRecordOutcome"/> name.</summary>
    public const string OutcomeTag = "router.outcome";

    /// <summary>What the outcome refers to (see <see cref="NavigationRecordOutcome"/>), when there is something.</summary>
    public const string OutcomeDetailTag = "router.outcome.detail";

    /// <summary>On a stage activity: a <see cref="StageOutcome"/> name.</summary>
    public const string StageOutcomeTag = "router.stage.outcome";

    internal static readonly ActivitySource Source = new(SourceName);

    /// <summary>
    /// What tags and records keep of an in-app URI: its absolute path only (<c>http://host/myapp/a?token=x#code=y</c> →
    /// <c>/myapp/a</c>). A query or fragment can carry tokens, codes, e-mail addresses or return URLs, and these values
    /// leave the app through an exporter.
    /// </summary>
    internal static string PathOnly(string uri)
    {
        var end = uri.AsSpan().IndexOfAny('?', '#');
        var path = end < 0 ? uri : uri[..end];
        var scheme = path.IndexOf("://", StringComparison.Ordinal);

        if (scheme < 0)
        {
            return path.Length == 0 ? "/" : path;
        }

        var slash = path.IndexOf('/', scheme + 3);

        return slash < 0 ? "/" : path[slash..];
    }

    /// <summary>
    /// What tags and records keep of a URI outside the app: scheme, host (without user information), port and path, never
    /// the query or fragment (an identity provider's <c>state</c>, a ticket, a return URL).
    /// </summary>
    internal static string WithoutQuery(string uri)
        => Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            ? $"{parsed.Scheme}://{parsed.Authority}{parsed.AbsolutePath}"
            : PathOnly(uri);

    /// <summary>
    /// Creates and starts an activity, or returns null when nobody samples it. A listener that throws is not allowed to
    /// break navigation: the half-started activity is stopped and the ambient activity restored.
    /// </summary>
    internal static Activity? TryStart(string name, ActivityContext parent, IEnumerable<ActivityLink>? links = null)
    {
        var ambient = Activity.Current;
        Activity? activity = null;

        try
        {
            activity = Source.CreateActivity(name, ActivityKind.Internal, parent, tags: null, links: links);

            return activity?.Start();
        }
        catch (Exception)
        {
            if (activity is not null)
            {
                TryStop(activity);
            }

            Activity.Current = ambient;

            return null;
        }
    }

    /// <summary>
    /// Stops <paramref name="activity"/> without disturbing the caller's ambient activity: <see cref="Activity.Stop"/>
    /// restores, in the calling flow, the activity that was current when this one started, which is right only when this
    /// one is that flow's current activity (a commit runs in the location-changed event's flow instead). A throwing
    /// listener is ignored.
    /// </summary>
    internal static void TryStop(Activity activity)
    {
        var ambient = Activity.Current;
        var wasCurrent = ReferenceEquals(ambient, activity);

        try
        {
            activity.Stop();

            if (!wasCurrent)
            {
                Activity.Current = ambient;
            }
        }
        catch (Exception)
        {
            // A listener threw before Stop restored the ambient activity; the activity is stopped either way.
            Activity.Current = wasCurrent ? activity.Parent : ambient;
        }
    }
}

/// <summary>
/// The trace of one navigation (one redirect hop): its activity when a listener samples it, its stage timings and its
/// record when the history is on. The coordinator creates one only when either wants it, so with neither every hook is a
/// null check. <see cref="End"/> is idempotent and freezes the trace: the record is published once, as an immutable
/// snapshot, and a stage that finishes later (a canceled <c>OnNavigateAsync</c>) changes nothing.
/// </summary>
internal sealed class NavigationTrace
{
    readonly object _gate = new();
    readonly RouterDiagnostics? _history;
    readonly List<StageRecord>? _stages;
    readonly long _id;
    readonly string _target;
    readonly NavigationTrigger _trigger;
    readonly string? _redirectOrigin;
    readonly int _redirectCount;
    readonly DateTimeOffset _startedAt;
    readonly long _started;
    long _preparedAt;
    Stage? _open;
    bool _ended;

    NavigationTrace(Activity? activity, RouterDiagnostics? history, long id, string target, NavigationTrigger trigger, IReadOnlyList<string> redirectChain, DateTimeOffset startedAt)
    {
        Activity = activity;
        _history = history;
        _stages = history is null ? null : [];
        _id = id;
        _target = target;
        _trigger = trigger;
        _redirectOrigin = redirectChain.Count > 0 ? RouterTelemetry.PathOnly(redirectChain[0]) : null;
        _redirectCount = redirectChain.Count;
        _startedAt = startedAt;
        _started = Stopwatch.GetTimestamp();
    }

    /// <summary>The navigation's activity, or null when no listener sampled it.</summary>
    public Activity? Activity { get; }

    /// <summary>A stage in progress: its span (if any) and when it started.</summary>
    public sealed class Stage(string name, Activity? activity)
    {
        public string Name { get; } = name;

        public Activity? Activity { get; } = activity;

        public long Started { get; } = Stopwatch.GetTimestamp();

        public bool Closed { get; set; }
    }

    /// <summary>
    /// Starts the trace of a navigation, or returns null when there is no listener sampling it and no history to fill
    /// (the coordinator then runs its original, uninstrumented path). Only the path of <paramref name="target"/> and of the
    /// chain's origin is kept (see <see cref="RouterTelemetry.PathOnly"/>).
    /// </summary>
    public static NavigationTrace? Start(long id, string target, NavigationTrigger trigger, IReadOnlyList<string> redirectChain, ActivityContext? previousHop, RouterDiagnostics? diagnostics, TimeProvider time)
    {
        var history = diagnostics is { IsEnabled: true } ? diagnostics : null;
        var path = RouterTelemetry.PathOnly(target);
        Activity? activity = null;

        if (RouterTelemetry.Source.HasListeners())
        {
            // A default parent context makes the ambient activity (the origin's caller) the parent of every hop.
            activity = RouterTelemetry.TryStart(RouterTelemetry.NavigationActivityName, default, previousHop is { } hop ? [new ActivityLink(hop)] : null);
            activity?.SetTag(RouterTelemetry.NavigationIdTag, id);
            activity?.SetTag(RouterTelemetry.TargetTag, path);
            activity?.SetTag(RouterTelemetry.TriggerTag, trigger.ToString());
            activity?.SetTag(RouterTelemetry.RedirectCountTag, redirectChain.Count);
        }

        return activity is null && history is null
            ? null
            : new NavigationTrace(activity, history, id, path, trigger, redirectChain, time.GetUtcNow());
    }

    /// <summary>Opens a stage: a child activity of the navigation (the ambient one while the stage runs) and its start time.</summary>
    public Stage BeginStage(string name)
    {
        var stage = new Stage(name, Activity is null ? null : RouterTelemetry.TryStart(name, Activity.Context));

        lock (_gate)
        {
            _open = stage;
        }

        return stage;
    }

    /// <summary>Closes a stage; once the trace has ended only its activity is stopped.</summary>
    public void EndStage(Stage stage, StageOutcome outcome)
    {
        lock (_gate)
        {
            if (stage.Closed)
            {
                return;
            }

            stage.Closed = true;

            if (ReferenceEquals(_open, stage))
            {
                _open = null;
            }

            if (!_ended)
            {
                _stages?.Add(new StageRecord(stage.Name, Stopwatch.GetElapsedTime(stage.Started), outcome));
            }
        }

        if (stage.Activity is { } activity)
        {
            // SetTag calls no listener; TryStop ignores a throwing one.
            activity.SetTag(RouterTelemetry.StageOutcomeTag, outcome.ToString());
            RouterTelemetry.TryStop(activity);
        }
    }

    /// <summary>The navigation is prepared and waits for the platform (its location change, or a direct commit).</summary>
    public void Prepared() => Interlocked.Exchange(ref _preparedAt, Stopwatch.GetTimestamp());

    /// <summary>The platform's location changed: the wait since <see cref="Prepared"/> is recorded as its own stage.</summary>
    public void LocationChanged()
    {
        var prepared = Interlocked.Exchange(ref _preparedAt, 0);

        lock (_gate)
        {
            if (prepared != 0 && !_ended)
            {
                _stages?.Add(new StageRecord(RouterTelemetry.LocationChangeStage, Stopwatch.GetElapsedTime(prepared), StageOutcome.Continued));
            }
        }
    }

    /// <summary>Ends the navigation once: closes a stage still open, tags and stops the activity, publishes the record.</summary>
    public void End(NavigationRecordOutcome outcome, string? detail = null, NavigationTrigger? committedAs = null, Exception? exception = null)
    {
        Stage? open;
        NavigationRecord? record = null;

        lock (_gate)
        {
            if (_ended)
            {
                return;
            }

            open = _open;
        }

        // A stage still running (OnNavigateAsync released by a location change or a reload) ends with the navigation.
        if (open is not null)
        {
            EndStage(open, StageOutcome.Canceled);
        }

        lock (_gate)
        {
            if (_ended)
            {
                return;
            }

            _ended = true;

            if (_history is not null)
            {
                record = new NavigationRecord
                {
                    NavigationId = _id,
                    Target = _target,
                    Trigger = _trigger,
                    CommittedAs = committedAs,
                    StartedAt = _startedAt,
                    Duration = Stopwatch.GetElapsedTime(_started),
                    Outcome = outcome,
                    OutcomeDetail = detail,
                    RedirectOrigin = _redirectOrigin,
                    RedirectCount = _redirectCount,
                    Stages = _stages!,
                };
            }
        }

        if (Activity is { } activity)
        {
            try
            {
                activity.SetTag(RouterTelemetry.OutcomeTag, outcome.ToString());

                if (detail is not null)
                {
                    activity.SetTag(RouterTelemetry.OutcomeDetailTag, detail);
                }

                if (committedAs is { } committed)
                {
                    activity.SetTag(RouterTelemetry.CommittedAsTag, committed.ToString());
                }

                if (exception is not null)
                {
                    activity.SetStatus(ActivityStatusCode.Error, exception.Message);

#if NET9_0_OR_GREATER
                    // Calls every listener's ExceptionRecorder.
                    activity.AddException(exception);
#else
                    // .NET 8 has no Activity.AddException: record the same event the OpenTelemetry conventions name.
                    activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
                    {
                        ["exception.type"] = exception.GetType().FullName,
                        ["exception.message"] = exception.Message,
                        ["exception.stacktrace"] = exception.ToString(),
                    }));
#endif
                }
            }
            catch (Exception)
            {
                // A listener that throws is not allowed to break navigation: the activity still stops, the record still publishes.
            }
            finally
            {
                RouterTelemetry.TryStop(activity);
            }
        }

        if (record is not null)
        {
            _history!.Record(record);
        }
    }
}
