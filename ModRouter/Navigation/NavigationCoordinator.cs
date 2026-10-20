using System.Diagnostics;
using ModRouter.Authorization;
using ModRouter.Components;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Guards;
using ModRouter.Core.Matching;
using ModRouter.Core.Titles;
using ModRouter.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ModRouter.Navigation;

/// <summary>
/// Owns navigation: assigns IDs, runs each navigation through the stages while the platform holds it, and
/// commits the prepared <see cref="RouteState"/> when the platform reports the location change (two-phase commit).
/// A newer navigation cancels and supersedes an unfinished one; a stale result can never commit. A redirect from a
/// stage prevents the held navigation and starts a new one for the target, carrying the redirect chain with it.
/// </summary>
internal sealed class NavigationCoordinator
{
    /// <summary>The longest redirect chain allowed; one more hop is reported as a redirect loop.</summary>
    public const int MaxRedirects = 10;

    readonly INavigationPlatformAdapter _adapter;
    readonly RouteStateStore _store;
    readonly IServiceProvider _services;
    readonly ModRouterOptions _options;
    readonly Dictionary<long, NavigationCompletion> _completions = [];
    (long Id, IReadOnlyCollection<int> Levels)? _stale;
    readonly IReadOnlyList<INavigationStage> _stages;
    readonly ILogger _logger;

    /// <summary>The navigation history; null (or off) unless <c>Diagnostics.HistorySize</c> is above 0.</summary>
    readonly RouterDiagnostics? _diagnostics;

    readonly TimeProvider _time;
    long _latestId;
    CancellationTokenSource? _active;
    Pending? _pending;
    RedirectIntent? _redirect;
    bool _attached;
    IDisposable? _authenticationSubscription;
    CancellationTokenSource? _recheckLifetime;
    bool _rechecking;
    bool _recheckPending;
    bool _ignoreAuthenticationEvents;

    /// <summary>
    /// True once the current attachment committed a state. The scoped store outlives a router, so a remounted router
    /// must not treat the previous router's (discarded) state as the one the user is looking at.
    /// </summary>
    bool _committedSinceAttach;

    /// <summary>The re-check started by the latest authentication change (completed when none is running); tests await it.</summary>
    internal Task AuthorizationRecheck { get; private set; } = Task.CompletedTask;

    CancellationTokenSource? _onNavigateCancellation;

    /// <summary>
    /// The navigation whose location has changed and whose <see cref="OnNavigate"/> is running (or returned while a newer
    /// navigation was still preparing, see <see cref="_arrivedReady"/>): the platform shows its URL while the old page
    /// stays committed. Only the next location change, or a reload (which renders this location and commits once, as this
    /// navigation: its trigger, so the new page's effects run), supersedes it; a newer navigation that is canceled or
    /// prevented leaves it to commit.
    /// </summary>
    Pending? _arrived;

    /// <summary>True once <see cref="_arrived"/>'s hook returned; it commits as soon as no navigation is preparing.</summary>
    bool _arrivedReady;

    /// <summary>The reloads preparing right now that will replace <see cref="_arrived"/>: while there are any it does not commit on its own.</summary>
    int _reloadsReplacingArrived;

    /// <summary>
    /// The compatibility Router's <c>OnNavigateAsync</c>. Like the built-in Router it runs after the platform's location
    /// changed (so <c>NavigationManager.Uri</c> is the new URI) and before the new page renders: the prepared state commits
    /// when it returns, with the old page kept and <see cref="RouteStateStore.IsNavigating"/> true meanwhile. It receives
    /// the absolute target URI and a token canceled while the callback is running, when the next location change happens
    /// (or a reload replaces the navigation, or the router detaches); once the callback has returned the token is disposed
    /// with its source and never canceled. A navigation superseded meanwhile never commits, and one that only started
    /// preparing and was then canceled or prevented supersedes nothing. The first navigation (no table yet) and reloads
    /// (no location change) do not run it.
    /// </summary>
    internal Func<string, CancellationToken, Task>? OnNavigate { get; set; }

    /// <summary>A prepared navigation and, when it is traced, its trace (ended by its commit or release).</summary>
    sealed record Pending(long Id, string Uri, RouteState State, NavigationTrace? Trace);

    /// <summary>The detail of a navigation the router's disposal (or detach) canceled before it committed.</summary>
    const string DisposedDetail = "the router was disposed";

    /// <summary>
    /// A redirect the coordinator started and expects to see arrive in its own location-changing handler. <paramref name="Origin"/>
    /// is the trigger of the navigation that started the chain; it travels with every hop. <paramref name="PreviousHop"/> is
    /// the redirecting hop's activity, which the next hop's activity links to.
    /// </summary>
    sealed record RedirectIntent(string Uri, IReadOnlyList<string> Chain, bool ReplaceRedirects, NavigationTrigger Origin, ActivityContext? PreviousHop);

    readonly record struct PrepareOptions(
        IReadOnlyList<string>? RedirectChain = null,
        int? ReloadLevel = null,
        bool ReloadAll = false,
        IReadOnlyCollection<int>? ReloadLevels = null,
        bool LoadersOnly = false,
        bool ReplaceRedirects = false,
        ModNavigationOptions? Options = null,
        NavigationTrigger? RedirectOrigin = null,
        ActivityContext? PreviousHop = null);

    /// <summary>What a preparation produced; <paramref name="Id"/> is the navigation's, so its caller commits only its own prepared state.</summary>
    readonly record struct PrepareResult(long Id, NavigationOutcome Outcome, Exception? Exception = null)
    {
        public bool CanProceed => Outcome is NavigationOutcome.Prepared or NavigationOutcome.NotFound;
    }

    public NavigationCoordinator(
        INavigationPlatformAdapter adapter,
        RouteStateStore store,
        RouteTableHolder holder,
        RouteComponentBinder binder,
        IServiceProvider services,
        ModRouterOptions options,
        IEnumerable<INavigationStage> additionalStages,
        ILoggerFactory loggerFactory,
        RouterDiagnostics? diagnostics = null)
    {
        _adapter = adapter;
        _store = store;
        _services = services;
        _options = options;

        // Guarded: a logging provider that throws must not change what a navigation does.
        _logger = RouterLog.Guard(loggerFactory.CreateLogger<NavigationCoordinator>());
        _diagnostics = diagnostics ?? (options.Diagnostics.HistorySize > 0 ? new RouterDiagnostics(options) : null);
        _time = services.GetService<TimeProvider>() ?? TimeProvider.System;

        // The stages hold the holder, not a table: they read the current table per navigation.
        _stages =
        [
            new NormalizeStage(adapter),
            new RecognizeStage(holder),
            new BindStage(binder, RouterLog.Guard(loggerFactory.CreateLogger<BindStage>())),
            new LeaveGuardStage(),
            new RedirectStage(holder, options.AliasBehavior, RouterLog.Guard(loggerFactory.CreateLogger<RedirectStage>())),
            new GuardStage(holder, RouterLog.Guard(loggerFactory.CreateLogger<GuardStage>())),
            new LoaderStage(RouterLog.Guard(loggerFactory.CreateLogger<LoaderStage>())),
            .. additionalStages,
        ];
    }

    /// <summary>
    /// Starts observing the platform: registers the location-changing handler and the location-changed
    /// subscription. Only one router may be attached; dispose the result to detach.
    /// </summary>
    public IDisposable Attach()
    {
        if (_attached)
        {
            throw new InvalidOperationException("A ModRouter is already mounted. Only one ModRouter can own navigation in an application scope.");
        }

        var registration = _adapter.RegisterLocationChanging(OnLocationChangingAsync);
        _adapter.LocationChanged += OnLocationChanged;
        _store.ReloadAll = token => ReloadAsync(all: true, cancellationToken: token);
        _store.Revalidate = token => RevalidateAsync(background: false, token);
        _store.Preload = PreloadAsync;
        _committedSinceAttach = false;
        _attached = true;

        // A sign-in or sign-out re-runs the authorization guards of the committed chain (nothing without a provider).
        _recheckLifetime = new CancellationTokenSource();
        _authenticationSubscription = AuthenticationChangeSubscription.Subscribe(_services, OnAuthenticationStateChanged);

        return new Lease(this, registration);
    }

    // ---- re-check on authentication change -----------------------------------------------------------------------------

    void OnAuthenticationStateChanged()
    {
        // A follow-up check's own read can raise the event (a provider that refreshes and notifies on every read). Taken
        // as a new change, it would arm another follow-up, whose read arms the next, forever.
        if (_ignoreAuthenticationEvents)
        {
            return;
        }

        AuthorizationRecheck = RecheckAuthorizationAsync(followUp: false);
    }

    /// <summary>
    /// Runs only the <see cref="IAuthorizationRouteGuard"/>s of the committed chain again, parent first, and reloads the
    /// current URL (the full pipeline, trigger <see cref="NavigationTrigger.Reload"/>) when a level's outcome differs
    /// from what the committed state shows; a token refresh that changes nothing reloads nothing. Deferred while a
    /// navigation is in flight (its own guards see the new state; a reload now would supersede it and reload the old
    /// URL) until that navigation commits or ends, including one the user starts while the guards run, and never run twice at once: events that arrive during a check run
    /// one follow-up after it. A check runs only for an authentication event and a reload only for a changed outcome,
    /// and events raised while a follow-up's guards run are taken as that follow-up's own read (a provider that notifies
    /// on every read), so each external event costs at most one follow-up and nothing loops.
    /// </summary>
    async Task RecheckAuthorizationAsync(bool followUp)
    {
        if (!_attached)
        {
            return;
        }

        // Any navigation still to commit (preparing, waiting for its location change or its OnNavigateAsync, or a redirect
        // on its way) defers the check, whatever the state on screen: that navigation's commit or end runs it, against
        // the state it leaves showing.
        if (RecheckMustWait)
        {
            _recheckPending = true;

            return;
        }

        _recheckPending = false;

        if (_store.Current is not { Chain: { } chain } current || _recheckLifetime is not { } lifetime)
        {
            return;
        }

        _rechecking = true;

        try
        {
            var before = AuthorizationOutcome(current.Error);
            string after;
            _ignoreAuthenticationEvents = followUp;

            try
            {
                after = await EvaluateAuthorizationGuardsAsync(current, chain, lifetime.Token);
            }
            finally
            {
                _ignoreAuthenticationEvents = false;
            }

            if (lifetime.IsCancellationRequested)
            {
                return;
            }

            // The user navigated while the guards ran: a reload now would cancel that navigation (still preparing or
            // waiting to commit) or reload the page it committed on a verdict about the old one. Defer instead; the
            // navigation's commit or end runs the check again, or the finally below does when it already committed.
            if (NavigationInFlight || !ReferenceEquals(_store.Current, current))
            {
                _recheckPending = true;

                return;
            }

            RouterLog.AuthorizationRechecked(_logger, current.Path, after, before);

            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                await ReloadAsync(cancellationToken: lifetime.Token);
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
            // Detached while a guard was running.
        }
        finally
        {
            _rechecking = false;
            RunDeferredRecheck();
        }
    }

    /// <summary>The outcome the authorization guards produced at commit time: an authorization guard's own error, else allow.</summary>
    static string AuthorizationOutcome(RouteError? error)
        => error is { Kind: RouteErrorKind.NotAuthorized or RouteErrorKind.GuardFailed, GuardType: { } guard } && typeof(IAuthorizationRouteGuard).IsAssignableFrom(guard)
            ? $"{error.Kind}@{error.Level}"
            : "Allow";

    /// <summary>
    /// Parent first, stopping at the first authorization guard that does not allow. Guards that never ran at commit
    /// time (every level below an error, and at the error's own level those declared after an ordinary guard that
    /// denied) are not run now either: their outcome then is unknown, so it cannot have changed.
    /// </summary>
    async Task<string> EvaluateAuthorizationGuardsAsync(RouteState current, RouteMatchChain chain, CancellationToken cancellationToken)
    {
        var lastDepth = current.Error is { IsChainLevel: true, Level: { } failed } ? failed : chain.Matches.Count - 1;
        var stopBefore = current.Error is { Kind: RouteErrorKind.NotAuthorized or RouteErrorKind.GuardFailed, GuardType: { } type } && !typeof(IAuthorizationRouteGuard).IsAssignableFrom(type) ? type : null;

        for (var depth = 0; depth <= lastDepth; depth++)
        {
            var level = chain.Matches[depth];

            foreach (var metadata in level.Route.Metadata.GetAll<RouteGuardMetadata>())
            {
                if (depth == lastDepth && metadata.GuardType == stopBefore)
                {
                    break;
                }

                if (!typeof(IAuthorizationRouteGuard).IsAssignableFrom(metadata.GuardType))
                {
                    continue;
                }

                RouteGuardResult result;

                try
                {
                    var guard = (IRouteGuard)ActivatorUtilities.GetServiceOrCreateInstance(_services, metadata.GuardType);
                    var guardContext = new RouteGuardContext(level, depth, chain, current.AbsoluteUri, current.AbsoluteUri, current.Query, false, _services, cancellationToken);
                    result = await guard.CanActivateAsync(guardContext);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    return $"{RouteErrorKind.GuardFailed}@{depth}";
                }

                if (result.Outcome != RouteGuardOutcome.Allow)
                {
                    return $"{(result.Outcome == RouteGuardOutcome.NotAuthorized ? RouteErrorKind.NotAuthorized.ToString() : result.Outcome.ToString())}@{depth}";
                }
            }
        }

        return "Allow";
    }

    /// <summary>
    /// True while a re-check must wait: a navigation is preparing, waiting for its location change or its
    /// OnNavigateAsync, or a redirect is on its way, or another check is running. One predicate for both the check's
    /// entry and <see cref="RunDeferredRecheck"/>, so the two conditions cannot drift apart.
    /// </summary>
    bool RecheckMustWait => _rechecking || NavigationInFlight;

    /// <summary>
    /// True while a navigation is preparing, waiting for its location change or its OnNavigateAsync, or a redirect is on
    /// its way. Shared by <see cref="RecheckMustWait"/> and the check's post-guard deferral: if that deferral tested a
    /// term this predicate lacked, its <c>finally</c> would start the check again at once, without bound.
    /// </summary>
    bool NavigationInFlight => _active is not null || _pending is not null || _arrived is not null || _redirect is not null;

    /// <summary>Runs the re-check an event asked for while a navigation or another check was in progress, now that neither is.</summary>
    void RunDeferredRecheck()
    {
        // Tested here as well as at the check's entry (not only there): the entry would re-arm the flag and return, but
        // it would also replace the AuthorizationRecheck task a test may still be awaiting.
        if (_recheckPending && !RecheckMustWait)
        {
            AuthorizationRecheck = RecheckAuthorizationAsync(followUp: true);
        }
    }

    /// <summary>
    /// Runs the first navigation for the platform's current URI and commits it. No handler fires on first load. A
    /// first navigation that fails or is canceled commits a <see cref="RouteFailure.NavigationFailed"/> state, so the
    /// router never waits forever; a redirect continues as its own navigation.
    /// </summary>
    public async Task<NavigationOutcome> NavigateInitialAsync(CancellationToken cancellationToken = default)
    {
        var result = await PrepareAsync(NavigationTrigger.InitialLoad, _adapter.Uri, _adapter.HistoryEntryState, cancellationToken);

        if (!result.CanProceed)
        {
            CommitFailureIfNothingShowing(result, initialChain: true);

            return result.Outcome;
        }

        return CommitPending(result.Id) ? result.Outcome : NotCommitted(result);
    }

    /// <summary>
    /// Runs the current URL through the pipeline again and commits the result directly (the platform's URL does not
    /// change). <paramref name="level"/> forces that chain level's loader to run; <paramref name="all"/> forces every loader.
    /// </summary>
    public async Task<NavigationOutcome> ReloadAsync(int? level = null, bool all = false, CancellationToken cancellationToken = default)
    {
        // A RouteError's retry action outlives the router that bound it; a disposed router must not run the pipeline.
        if (!_attached || _store.Current is not { } current)
        {
            return NavigationOutcome.Cancelled;
        }

        // While OnNavigateAsync runs, the platform already shows that navigation's URL and the old page only waits to be
        // replaced: reloading the old page's URL would commit it under the new one. The built-in renders the platform's
        // current location after its hook, so the reload does too; it replaces the waiting navigation (its hook is canceled).
        var hookRunning = _arrived is not null;
        PrepareResult result;

        // While this reload prepares it is the commit of the navigation it replaces: if that navigation's hook returns
        // meanwhile it must not commit on its own, or the location would commit twice.
        if (hookRunning)
        {
            _reloadsReplacingArrived++;
        }

        try
        {
            result = await PrepareAsync(
                NavigationTrigger.Reload,
                hookRunning ? _adapter.Uri : current.AbsoluteUri,
                hookRunning ? _adapter.HistoryEntryState : current.HistoryEntryState,
                cancellationToken,
                new PrepareOptions(ReloadLevel: level, ReloadAll: all));
        }
        finally
        {
            if (hookRunning)
            {
                _reloadsReplacingArrived--;
            }
        }

        if (result.CanProceed && OwnsPending(result.Id))
        {
            // The page is new to the user whatever replaced the hook's wait: it commits once, as that navigation.
            var replaced = _arrived?.State;
            ReleaseArrived();

            return CommitPending(result.Id, replaced) ? result.Outcome : NotCommitted(result);
        }

        // Nothing replaced it: a navigation whose hook returned meanwhile commits now (once no other is preparing).
        CommitArrivedIfReady();

        return result.CanProceed ? NotCommitted(result) : result.Outcome;
    }

    /// <summary>
    /// Runs the loaders of the page on screen again, without guards, redirects or a URL change, and commits the refreshed
    /// data as a reload. <paramref name="levels"/> limits it to those chain levels (the stale cached ones); null reloads
    /// every level. A <paramref name="background"/> refresh never disturbs a navigation: it does nothing while one is in
    /// flight. The page stays as it is when a loader fails.
    /// </summary>
    public async Task<NavigationResult> RevalidateAsync(bool background, CancellationToken cancellationToken = default, IReadOnlyCollection<int>? levels = null)
    {
        // Only a plain page is refreshed: an error state has nothing to revalidate, and a navigation in flight is never disturbed.
        if (!_attached || _store.Current is not { Chain: not null, Error: null } current || NavigationInFlight)
        {
            return new NavigationResult(NavigationStatus.Cancelled);
        }

        var completion = new NavigationCompletion();
        var result = await PrepareAsync(
            NavigationTrigger.Reload,
            current.AbsoluteUri,
            current.HistoryEntryState,
            cancellationToken,
            new PrepareOptions(ReloadAll: levels is null, ReloadLevels: levels, LoadersOnly: true, Options: new ModNavigationOptions().WithCompletion(completion)));

        if (result.CanProceed && OwnsPending(result.Id))
        {
            CommitPending(result.Id);
        }

        // Whatever the path, the caller is told: a commit settled it already, anything else was superseded.
        Settle(result.Id, new NavigationResult(NavigationStatus.Superseded));

        return await completion.Task.ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the cacheable loaders of <paramref name="uri"/> into the cache. It is not a navigation: no guard runs, nothing
    /// commits, and a navigation in flight is neither cancelled nor released. The data it loads is only ever cached.
    /// </summary>
    public async Task PreloadAsync(string uri, CancellationToken cancellationToken = default)
    {
        if (!_attached)
        {
            return;
        }

        using var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var context = new RouteNavigationContext
        {
            NavigationId = -1,
            To = BaseUriHelper.ToAbsolute(_adapter.BaseUri, uri),
            Trigger = NavigationTrigger.Programmatic,
            CancellationToken = source.Token,
            Services = _services,
            IsPreload = true,
        };

        try
        {
            foreach (var stage in _stages)
            {
                if (stage is not (NormalizeStage or RecognizeStage or BindStage or LoaderStage)
                    || ((context.Failure != RouteFailure.None || context.Error is not null) && !stage.RunsOnFailure))
                {
                    continue;
                }

                await stage.RunAsync(context).ConfigureAwait(false);
            }
        }
        finally
        {
            foreach (var data in context.LevelData)
            {
                data?.Discard();
            }
        }
    }

    async ValueTask<LocationChangingDecision> OnLocationChangingAsync(LocationChangingRequest request)
    {
        // Parsed once: a target no URI parser accepts is treated like one outside the base (nothing the router owns is involved).
        var parsed = BaseUriHelper.TryToAbsolute(_adapter.BaseUri, request.TargetLocation, out var absolute);
        var intent = TakeRedirectIntent(absolute);

        // The options belong to this navigation whatever becomes of it: taken before the early returns, so a navigation
        // that goes nowhere (outside the base, or to the current location) uses them up rather than leaving them armed
        // for a later navigation to the same URL. A redirect hop never takes them: the hop it was started from already did.
        var requested = intent is null && absolute is not null ? _store.TakeOptions(StripFragment(absolute)) : null;

        if (!parsed)
        {
            RouterLog.UnparseableLocation(_logger, request.TargetLocation);
        }

        // No navigation runs for these; a redirect hop arriving here has given up its intent, so a re-check deferred
        // behind it runs now.
        if (!parsed || !BaseUriHelper.TryGetBaseRelative(_adapter.BaseUri, request.TargetLocation, out var target) || IsCurrentLocation(target))
        {
            // Nothing runs for this navigation: the page the caller asked for is already shown (or is not the router's to show).
            requested?.Completion?.Complete(new NavigationResult(NavigationStatus.Succeeded));
            RunDeferredRecheck();

            return LocationChangingDecision.Allow;
        }

        var trigger = intent is not null
            ? NavigationTrigger.Redirect
            : request.IsNavigationIntercepted ? NavigationTrigger.Link : NavigationTrigger.Programmatic;
        var options = intent is null
            ? new PrepareOptions(Options: requested)
            : new PrepareOptions(intent.Chain, ReplaceRedirects: intent.ReplaceRedirects, RedirectOrigin: intent.Origin, PreviousHop: intent.PreviousHop);
        var result = await PrepareAsync(trigger, request.TargetLocation, request.HistoryEntryState, request.CancellationToken, options);

        if (result.CanProceed)
        {
            return LocationChangingDecision.Allow;
        }

        CommitFailureIfNothingShowing(result, initialChain: trigger == NavigationTrigger.Redirect && options.ReplaceRedirects);

        return LocationChangingDecision.Prevent;
    }

    void OnLocationChanged(LocationChangedNotification notification)
    {
        // Null for a location no URI parser accepts: it equals no pending or arrived URI, so both are released below, and
        // the location is then ignored like one outside the base.
        string? uri = null;

        if (BaseUriHelper.TryToAbsolute(_adapter.BaseUri, notification.Location, out var absolute))
        {
            uri = StripFragment(absolute!);
        }
        else
        {
            RouterLog.UnparseableLocation(_logger, notification.Location);
        }

        if (_pending is { } pending)
        {
            if (pending.Id == Volatile.Read(ref _latestId) && string.Equals(pending.Uri, uri, StringComparison.Ordinal))
            {
                // Blazor reports a browser history traversal (back/forward) to the handler as not intercepted but to
                // LocationChanged as intercepted; a programmatic navigation is not intercepted in both places.
                var historyTraversal = pending.State.Trigger == NavigationTrigger.Programmatic && notification.IsNavigationIntercepted;
                CommitAfterLocationChanged(historyTraversal ? NavigationTrigger.HistoryRestore : null);

                return;
            }

            ReleasePending();
        }

        if (_arrived is { } arrived)
        {
            // A fragment change on the location whose hook is running: that navigation's commit is still to come.
            if (string.Equals(arrived.Uri, uri, StringComparison.Ordinal))
            {
                return;
            }

            // The location moved away from it (the built-in cancels its hook on the next location change too).
            ReleaseArrived();
            SettleNavigating();
        }

        if (!BaseUriHelper.TryGetBaseRelative(_adapter.BaseUri, notification.Location, out var target) || IsCurrentLocation(target))
        {
            // Nothing runs for this location (the page on screen stays), so a re-check deferred behind the navigation
            // released above runs now.
            RunDeferredRecheck();

            return;
        }

        RouterLog.LocationChangedWithoutPreparedNavigation(_logger, notification.Location);
        _ = FallbackAsync(notification);
    }

    async Task FallbackAsync(LocationChangedNotification notification)
    {
        try
        {
            var trigger = notification.IsNavigationIntercepted ? NavigationTrigger.Link : NavigationTrigger.Programmatic;
            var result = await PrepareAsync(trigger, notification.Location, notification.HistoryEntryState, CancellationToken.None);

            if (result.CanProceed)
            {
                CommitAfterLocationChanged(null);
            }
        }
        catch (Exception exception)
        {
            RouterLog.FallbackNavigationFailed(_logger, exception, notification.Location);
        }
    }

    /// <summary>Commits the prepared state of a navigation whose location has changed, after <see cref="OnNavigate"/> when one is set.</summary>
    void CommitAfterLocationChanged(NavigationTrigger? trigger)
    {
        if (_pending is not { } pending)
        {
            return;
        }

        _pending = null;
        pending.Trace?.LocationChanged();
        var changed = trigger is { } actual ? pending with { State = pending.State.WithTrigger(actual) } : pending;

        // The location changed: a navigation still waiting on its hook is superseded now, and only now.
        ReleaseArrived();

        if (OnNavigate is { } onNavigate)
        {
            _arrived = changed;
            _arrivedReady = false;
            _ = RunOnNavigateAsync(changed, onNavigate);

            return;
        }

        Commit(changed);
    }

    /// <summary>
    /// Runs <see cref="OnNavigate"/> for a navigation whose location has changed: the old page stays, with the navigating
    /// flag on, until it returns; then the prepared state commits (once no newer navigation is preparing), unless a
    /// location change or a reload released it meanwhile. A throwing callback is logged and the navigation commits anyway.
    /// </summary>
    async Task RunOnNavigateAsync(Pending arrived, Func<string, CancellationToken, Task> onNavigate)
    {
        var cancellation = new CancellationTokenSource();
        _onNavigateCancellation = cancellation;
        _store.SetNavigating(true);

        // The hook's own span: its code sees it as Activity.Current (this async method's flow only).
        var stage = arrived.Trace?.BeginStage(RouterTelemetry.OnNavigateActivityName);
        var outcome = StageOutcome.Continued;

        try
        {
            await onNavigate(arrived.State.AbsoluteUri, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            outcome = StageOutcome.Canceled;
            RouterLog.OnNavigateCanceled(_logger, arrived.Id);
        }
        catch (Exception exception)
        {
            outcome = StageOutcome.Failed;
            RouterLog.OnNavigateThrew(_logger, exception, arrived.Id);
        }
        finally
        {
            Interlocked.CompareExchange(ref _onNavigateCancellation, null, cancellation);
            cancellation.Dispose();

            if (stage is not null)
            {
                arrived.Trace!.EndStage(stage, outcome);
            }
        }

        if (ReferenceEquals(_arrived, arrived))
        {
            _arrivedReady = true;
            CommitArrivedIfReady();
        }
    }

    /// <summary>
    /// Commits the navigation whose hook returned, unless a newer navigation is still preparing: that one's end calls this
    /// again, so a navigation that is then canceled or prevented leaves the page of the location the platform shows.
    /// </summary>
    void CommitArrivedIfReady()
    {
        if (_active is not null || _reloadsReplacingArrived > 0 || !_arrivedReady || _arrived is not { } arrived)
        {
            return;
        }

        _arrived = null;
        _arrivedReady = false;

        if (!Commit(arrived))
        {
            RouterLog.NavigationSuperseded(_logger, arrived.Id);
        }
    }

    /// <summary>
    /// Discards the navigation waiting on its hook (its location is gone or a reload replaced it: superseded; the router
    /// is being disposed: canceled) and cancels the hook.
    /// </summary>
    void ReleaseArrived(bool disposing = false)
    {
        if (_arrived is not { } arrived)
        {
            return;
        }

        _arrived = null;
        _arrivedReady = false;
        EndReleased(arrived, disposing, "released while OnNavigateAsync ran");
        DiscardLevelData(arrived.State, _store.Current);

        // After the state is gone: canceling runs the hook's continuation synchronously, and it must find nothing to commit.
        Interlocked.Exchange(ref _onNavigateCancellation, null)?.Cancel();
    }

    /// <summary>Turns the navigating flag off when no navigation is preparing and none waits on its hook.</summary>
    void SettleNavigating()
    {
        // _arrived is null at every caller by invariant (Commit runs only once the waiting navigation was released or
        // committed; OnLocationChanged calls this right after ReleaseArrived), so that half cannot be tested; it is kept
        // as a guard against a future caller that breaks the invariant.
        if (_active is null && _arrived is null)
        {
            _store.SetNavigating(false);
        }
    }

    async Task<PrepareResult> PrepareAsync(
        NavigationTrigger trigger,
        string target,
        string? historyState,
        CancellationToken external,
        PrepareOptions options = default)
    {
        var absolute = BaseUriHelper.ToAbsolute(_adapter.BaseUri, target);
        var id = Interlocked.Increment(ref _latestId);
        var source = CancellationTokenSource.CreateLinkedTokenSource(external);
        RouteNavigationContext? context = null;

        if (options.Options?.Completion is { } completion)
        {
            _completions[id] = completion;
        }

        // A revalidation runs in the background: it neither releases a prepared navigation nor shows the navigation indicator.
        if (!options.LoadersOnly)
        {
            ReleasePending();
            _store.SetNavigating(true);
        }

        RouterLog.NavigationRequested(_logger, id, target, trigger);

        // Null unless a listener samples the router's activities or the history is on: every hook below is then a null check.
        var trace = NavigationTrace.Start(id, absolute, trigger, options.RedirectChain ?? [], options.PreviousHop, _diagnostics, _time);

        // A navigation waiting on its OnNavigateAsync is not touched here: this one may still be canceled or prevented,
        // and the platform then keeps showing that navigation's location. Its location change supersedes it.
        try
        {
            Interlocked.Exchange(ref _active, source)?.Cancel();

            // Until this router commits, what the store holds is an earlier router's state and is never reused.
            var from = trigger == NavigationTrigger.InitialLoad || !_committedSinceAttach ? null : _store.Current;

            context = new RouteNavigationContext
            {
                NavigationId = id,
                From = from?.AbsoluteUri,
                To = absolute,
                Trigger = trigger,
                CancellationToken = source.Token,
                Services = _services,
                HistoryEntryState = historyState,
                FromState = from,
                RedirectChain = options.RedirectChain ?? [],
                ReloadLevel = options.ReloadLevel,
                ReloadAll = options.ReloadAll,
                ReloadLevels = options.ReloadLevels,
            };

            foreach (var stage in _stages)
            {
                if (source.IsCancellationRequested)
                {
                    return Interrupted(id, context, trace);
                }

                if ((context.Failure != RouteFailure.None || context.Error is not null) && !stage.RunsOnFailure)
                {
                    continue;
                }

                // A revalidation reads the page that is already shown: it loads, it never asks, redirects or guards again.
                if (options.LoadersOnly && stage is not (NormalizeStage or RecognizeStage or BindStage or LoaderStage))
                {
                    continue;
                }

                var result = trace is null ? await stage.RunAsync(context) : await RunTracedAsync(stage, context, trace);

                // A stage that ignored its token may come back after a newer navigation won: its verdict (above all
                // a redirect) belongs to a navigation that no longer exists and must not be acted on.
                if (source.IsCancellationRequested)
                {
                    return Interrupted(id, context, trace);
                }

                if (result.IsCancel)
                {
                    RouterLog.NavigationCanceledByStage(_logger, id, stage.GetType().Name);
                    DiscardNewData(context);
                    trace?.End(NavigationRecordOutcome.Canceled, $"canceled by {StageName(stage)}");
                    Settle(id, new NavigationResult(NavigationStatus.Cancelled));

                    return new PrepareResult(id, NavigationOutcome.Cancelled);
                }

                if (result.IsRedirect)
                {
                    DiscardNewData(context);

                    return new PrepareResult(id, Redirect(context, result, absolute, options, trace));
                }
            }

            if (source.IsCancellationRequested)
            {
                return Interrupted(id, context, trace);
            }

            // The page on screen stays when a revalidation cannot produce a better one.
            if (options.LoadersOnly && (context.Failure != RouteFailure.None || context.Error is not null))
            {
                DiscardNewData(context);
                trace?.End(NavigationRecordOutcome.Failed, "revalidation failed");
                Settle(id, new NavigationResult(NavigationStatus.Failed, Error: context.Error));

                return new PrepareResult(id, NavigationOutcome.Failed);
            }

            var state = new RouteState
            {
                NavigationId = id,
                AbsoluteUri = absolute,
                Path = context.Location.Path,
                Query = context.Location.Query,
                Fragment = context.Location.Fragment,
                Chain = context.MatchChain,
                Failure = context.Failure,
                Trigger = trigger,
                HistoryEntryState = historyState,
                Error = context.Error,
                RedirectedFrom = options.RedirectChain ?? [],
                RedirectOrigin = options.RedirectOrigin,
                LevelData = context.LevelData,
                Title = ResolveTitle(context.MatchChain, context.Failure),
                Options = options.Options,
            };

            if (state.Error is { } error)
            {
                BindRetry(error);
            }

            _pending = new Pending(id, StripFragment(absolute), state, trace);
            _stale = options.LoadersOnly || context.StaleLevels.IsEmpty ? null : (id, [.. context.StaleLevels]);
            trace?.Prepared();

            return new PrepareResult(id, state.Failure == RouteFailure.None ? NavigationOutcome.Prepared : NavigationOutcome.NotFound);
        }
        catch (OperationCanceledException) when (source.IsCancellationRequested)
        {
            return Interrupted(id, context, trace);
        }
        catch (Microsoft.AspNetCore.Components.NavigationException)
        {
            // A static server render (.NET 8 and 9, or 10 without BlazorDisableThrowNavigationException) throws from NavigateTo to
            // end the request with a redirect. The redirect is already recorded and settled; the framework must see the exception.
            if (context is not null)
            {
                DiscardNewData(context);
            }

            throw;
        }
        catch (Exception exception)
        {
            RouterLog.NavigationFailed(_logger, exception, id, target);
            trace?.End(NavigationRecordOutcome.Failed, exception.GetType().Name, exception: exception);
            Settle(id, new NavigationResult(NavigationStatus.Failed, Exception: exception));

            if (context is not null)
            {
                DiscardNewData(context);
            }

            return new PrepareResult(id, NavigationOutcome.Failed, exception);
        }
        finally
        {
            Interlocked.CompareExchange(ref _active, null, source);
            source.Dispose();

            if (Volatile.Read(ref _latestId) == id)
            {
                // A navigation whose hook returned while this one ran commits now (the platform shows its location until
                // this one's location change, if any, arrives); one still waiting on its hook keeps the flag on.
                CommitArrivedIfReady();

                // A first load or reload that prepared commits directly, in its caller: the flag stays on until that
                // commit turns it off. Dropped here, it would run NavigatingChanged subscribers before the commit, and one
                // that navigates would put its own prepared state where the caller commits.
                if (!CommitsDirectly(trigger) || !OwnsPending(id))
                {
                    _store.SetNavigating(_arrived is not null);
                }

                // A navigation that ends without a commit to come (canceled, failed, prevented; a redirect that has not
                // arrived is excluded by its intent): a re-check deferred while it ran runs now against the state on screen.
                RunDeferredRecheck();
            }
        }
    }

    /// <summary>
    /// Executes a redirect: a loop is committed as a router-level error; an external target is a full page load; any
    /// other target is a new navigation that arrives in this coordinator's handler and is recognized by its URI.
    /// </summary>
    NavigationOutcome Redirect(RouteNavigationContext context, StageResult redirect, string from, PrepareOptions options, NavigationTrace? trace)
    {
        var target = BaseUriHelper.ToAbsolute(_adapter.BaseUri, redirect.RedirectTarget!);
        var key = StripFragment(target);
        var chain = new List<string>([.. context.RedirectChain, StripFragment(from)]);

        // The first hop's trigger is the user's own navigation; a later hop already carries it.
        var origin = options.RedirectOrigin ?? context.Trigger;

        if (chain.Count > MaxRedirects || chain.Contains(key, StringComparer.Ordinal))
        {
            var path = string.Join(" -> ", [.. chain, key]);
            RouterLog.RedirectLoop(_logger, context.NavigationId, path);

            var error = new RouteError(RouteErrorKind.RedirectLoop, $"The navigation was redirected in a loop: {path}.");
            BindRetry(error);

            _pending = new Pending(context.NavigationId, StripFragment(from), new RouteState
            {
                NavigationId = context.NavigationId,
                AbsoluteUri = from,
                Path = context.Location.Path,
                Query = context.Location.Query,
                Fragment = context.Location.Fragment,
                Failure = RouteFailure.RedirectLoop,
                Trigger = context.Trigger,
                HistoryEntryState = context.HistoryEntryState,
                Error = error,
                RedirectedFrom = context.RedirectChain,
                RedirectOrigin = options.RedirectOrigin,
                Title = ResolveTitle(null, RouteFailure.RedirectLoop),
            }, trace);

            // The loop is committed like any prepared navigation; its trace ends there (detail RedirectLoop).
            trace?.Prepared();

            return NavigationOutcome.NotFound;
        }

        if (!BaseUriHelper.TryGetBaseRelative(_adapter.BaseUri, target, out var location))
        {
            trace?.End(NavigationRecordOutcome.Redirected, "external " + RouterTelemetry.WithoutQuery(target));
            Settle(context.NavigationId, new NavigationResult(NavigationStatus.Redirected, target));
            _adapter.NavigateTo(target, replaceHistory: false, forceLoad: true);

            return NavigationOutcome.Redirected;
        }

        // Where the user already is: navigating would push a duplicate entry (and wipe its state) that the handler then
        // lets through without a pipeline, so Back would seem to do nothing. The redirecting navigation is prevented and
        // nothing else happens. After the loop check, so a first load or reload redirected to its own URL is a loop.
        if (IsWhereTheUserIs(location))
        {
            trace?.End(NavigationRecordOutcome.Redirected, RouterTelemetry.PathOnly(key));
            Settle(context.NavigationId, new NavigationResult(NavigationStatus.Redirected, target));

            return NavigationOutcome.Redirected;
        }

        // Push by default (the redirecting URL never enters history while the page the user came from stays), but
        // replace when the chain started at the initial load: there the redirecting URL is already the current entry.
        var replaceDefault = context.Trigger == NavigationTrigger.InitialLoad || options.ReplaceRedirects;

        _redirect = new RedirectIntent(key, chain, replaceDefault, origin, trace?.Activity?.Context);

        // Ended before NavigateTo: a platform that runs the next hop synchronously must not see this hop as ambient.
        trace?.End(NavigationRecordOutcome.Redirected, RouterTelemetry.PathOnly(key));
        Settle(context.NavigationId, new NavigationResult(NavigationStatus.Redirected, target));
        _adapter.NavigateTo(redirect.RedirectTarget!, redirect.ReplaceHistory ?? replaceDefault);

        return NavigationOutcome.Redirected;
    }

    // The intent is used up by whatever navigation arrives next, so it is cleared even when the target does not parse.
    RedirectIntent? TakeRedirectIntent(string? requestAbsolute)
    {
        var intent = _redirect;
        _redirect = null;

        return intent is not null && requestAbsolute is not null && string.Equals(intent.Uri, StripFragment(requestAbsolute), StringComparison.Ordinal)
            ? intent
            : null;
    }

    PrepareResult Interrupted(long id, RouteNavigationContext? context, NavigationTrace? trace)
    {
        var superseded = Volatile.Read(ref _latestId) != id;

        if (context is not null)
        {
            DiscardNewData(context);
        }

        if (superseded)
        {
            RouterLog.NavigationSuperseded(_logger, id);
        }
        else
        {
            RouterLog.NavigationCanceled(_logger, id);
        }

        trace?.End(superseded ? NavigationRecordOutcome.Superseded : NavigationRecordOutcome.Canceled);
        Settle(id, new NavigationResult(superseded ? NavigationStatus.Superseded : NavigationStatus.Cancelled));

        return new PrepareResult(id, superseded ? NavigationOutcome.Superseded : NavigationOutcome.Cancelled);
    }

    void BindRetry(RouteError error)
        => error.RetryAction = async () => await ReloadAsync(error.Kind == RouteErrorKind.LoadFailed ? error.Level : null);

    /// <summary>
    /// Commits a navigation-failed state when the first navigation (or a redirect chain that started at it) ended without
    /// anything to show, so the router never waits forever.
    /// </summary>
    void CommitFailureIfNothingShowing(PrepareResult result, bool initialChain)
    {
        if (!initialChain || _committedSinceAttach || result.Outcome is not (NavigationOutcome.Cancelled or NavigationOutcome.Failed))
        {
            return;
        }

        var uri = _adapter.Uri;
        var location = BaseUriHelper.TryGetBaseRelative(_adapter.BaseUri, uri, out var found) ? found : new BaseRelativeLocation("/", string.Empty, null);
        var message = result.Outcome == NavigationOutcome.Cancelled
            ? "The first navigation was canceled, so there is no page to show."
            : $"The first navigation failed: {result.Exception?.Message ?? "unknown error"}";
        var error = new RouteError(RouteErrorKind.NavigationFailed, message, result.Exception);
        BindRetry(error);
        _committedSinceAttach = true;

        _store.Commit(new RouteState
        {
            NavigationId = Volatile.Read(ref _latestId),
            AbsoluteUri = uri,
            Path = location.Path,
            Query = location.Query,
            Fragment = location.Fragment,
            Failure = RouteFailure.NavigationFailed,
            Trigger = NavigationTrigger.InitialLoad,
            HistoryEntryState = _adapter.HistoryEntryState,
            Error = error,
            Title = ResolveTitle(null, RouteFailure.NavigationFailed),
        });
    }

    /// <summary>
    /// The browser title of a state: the leaf's route title through the format, the default title for an untitled route,
    /// "not found" text when nothing matched, and the default title (or none) when the pipeline itself failed.
    /// </summary>
    string? ResolveTitle(RouteMatchChain? chain, RouteFailure failure)
    {
        var titles = _options.Title;

        if (chain is not null)
        {
            return RouteTitleFormatter.Resolve(RouteTitles.Effective(chain), titles.Format, titles.Default);
        }

        return failure is RouteFailure.NavigationFailed or RouteFailure.RedirectLoop
            ? RouteTitleFormatter.Resolve(null, titles.Format, titles.Default)
            : titles.NotFound;
    }

    /// <summary>A first load and a reload commit their prepared state themselves; every other navigation commits at its location change.</summary>
    static bool CommitsDirectly(NavigationTrigger trigger) => trigger is NavigationTrigger.InitialLoad or NavigationTrigger.Reload;

    /// <summary>True when the prepared state waiting to commit is navigation <paramref name="id"/>'s.</summary>
    bool OwnsPending(long id) => _pending?.Id == id;

    /// <summary>
    /// Commits navigation <paramref name="id"/>'s prepared state; <paramref name="asNavigation"/> is the navigation it
    /// replaces, whose trigger it takes. Returns false, committing nothing, when the prepared state is not that
    /// navigation's (anything that ran since its preparation may have released it or put another in its place).
    /// </summary>
    bool CommitPending(long id, RouteState? asNavigation = null)
    {
        if (_pending is not { } pending || pending.Id != id)
        {
            return false;
        }

        _pending = null;

        return Commit(asNavigation is null ? pending : pending with { State = pending.State.WithNavigationOf(asNavigation) });
    }

    /// <summary>
    /// The outcome of a first load or reload that prepared but found its state gone at its commit: its own location
    /// change committed it (a fragment jump on the same URL) or handed it to OnNavigateAsync (the prepared outcome), a newer navigation or a location change released it, or a
    /// navigation started by a <c>NavigatingChanged</c> subscriber committed first (superseded; its trace already ended
    /// there), or the router was detached (canceled). The flag it kept on for its
    /// commit is settled, and a re-check deferred behind it runs when nothing else is in flight.
    /// </summary>
    NavigationOutcome NotCommitted(PrepareResult result)
    {
        // Its own location change committed it, or handed it to OnNavigateAsync, which commits it when the hook returns.
        if ((_committedSinceAttach && _store.Current?.NavigationId == result.Id) || _arrived?.Id == result.Id)
        {
            return result.Outcome;
        }

        if (!_attached)
        {
            RouterLog.NavigationCanceled(_logger, result.Id);

            return NavigationOutcome.Cancelled;
        }

        RouterLog.NavigationSuperseded(_logger, result.Id);
        SettleNavigating();
        RunDeferredRecheck();

        return NavigationOutcome.Superseded;
    }

    /// <summary>
    /// Commits <paramref name="pending"/>. Returns false, committing nothing and ending its trace as superseded, when a
    /// <c>NavigatingChanged</c> subscriber, run by the flag drop that precedes the commit, navigated and that navigation
    /// committed first: the newer state is what the platform shows, so this one must not be written over it.
    /// </summary>
    bool Commit(Pending pending)
    {
        var previous = _store.Current;
        var state = pending.State;
        SettleNavigating();

        if (!ReferenceEquals(_store.Current, previous))
        {
            pending.Trace?.End(NavigationRecordOutcome.Superseded, "a newer navigation committed while the flag dropped");
            Settle(pending.Id, new NavigationResult(NavigationStatus.Superseded));
            DiscardLevelData(state, _store.Current, _pending?.State);

            return false;
        }

        RouterLog.NavigationCommitted(_logger, pending.Id);

        // The ancestor links are set only for a state that commits (never while it is merely prepared, so a navigation
        // that loses can't rewire what the user is looking at), and for the whole chain: a reused level may now sit
        // under a parent that loaded again.
        LinkAccessors(state.LevelData);
        _committedSinceAttach = true;
        _store.Commit(state);
        pending.Trace?.End(NavigationRecordOutcome.Committed, CommitDetail(state), state.Trigger);
        Settle(pending.Id, ResultOf(state));

        // Streams start now, never while only prepared; levels the new state did not carry over are done, except those a
        // navigation prepared from the previous state (still waiting for its location change) reuses.
        foreach (var data in state.LevelData)
        {
            data?.Activate();
        }

        DiscardLevelData(previous, state, _pending?.State);
        RunDeferredRecheck();

        // Cached data that was past its fresh window is on screen: refresh it in the background.
        if (_stale is { } stale && stale.Id == pending.Id)
        {
            _stale = null;
            _ = RevalidateAsync(background: true, CancellationToken.None, stale.Levels);
        }

        return true;
    }

    /// <summary>Gives each level with data its nearest ancestor with data, as of the state being committed.</summary>
    static void LinkAccessors(IReadOnlyList<RouteLevelData?> levels)
    {
        ModRouteData? nearest = null;

        foreach (var level in levels)
        {
            if (level is null)
            {
                continue;
            }

            level.Accessor.Parent = nearest;
            nearest = level.Accessor;
        }
    }

    /// <summary>
    /// Discards the pending state's level data (it never committed), keeping what the committed state still uses. A newer
    /// navigation or location change supersedes it; the router's disposal cancels it.
    /// </summary>
    void ReleasePending(bool disposing = false)
    {
        if (_pending is not { } pending)
        {
            return;
        }

        _pending = null;
        EndReleased(pending, disposing, "released before its location change");
        DiscardLevelData(pending.State, _store.Current);
    }

    /// <summary>Ends the trace of a navigation released before it committed, and settles its caller.</summary>
    void EndReleased(Pending released, bool disposing, string supersededDetail)
    {
        if (disposing)
        {
            released.Trace?.End(NavigationRecordOutcome.Canceled, DisposedDetail);
            Settle(released.Id, new NavigationResult(NavigationStatus.Cancelled));
        }
        else
        {
            released.Trace?.End(NavigationRecordOutcome.Superseded, supersededDetail);
            Settle(released.Id, new NavigationResult(NavigationStatus.Superseded));
        }
    }

    /// <summary>Settles the <c>NavigateToAsync</c> that started navigation <paramref name="id"/>, if one did.</summary>
    void Settle(long id, NavigationResult result)
    {
        if (_completions.Remove(id, out var completion))
        {
            completion.Complete(result);
        }
    }

    /// <summary>What a committed state means for the caller of <c>NavigateToAsync</c>.</summary>
    static NavigationResult ResultOf(RouteState state) => RouteStatus.For(state) switch
    {
        null => new NavigationResult(NavigationStatus.Succeeded),
        RouteStatusKind.NotFound => new NavigationResult(NavigationStatus.NotFound),
        RouteStatusKind.NotAuthorized => new NavigationResult(NavigationStatus.NotAuthorized, Error: state.Error),
        _ => new NavigationResult(NavigationStatus.Failed, Error: state.Error),
    };

    /// <summary>
    /// Runs one stage of a traced navigation inside its own span (the ambient activity while the stage runs, in this
    /// method's flow only) and records its duration and outcome. Untraced navigations never come here.
    /// </summary>
    static async ValueTask<StageResult> RunTracedAsync(INavigationStage stage, RouteNavigationContext context, NavigationTrace trace)
    {
        var span = trace.BeginStage(StageName(stage));
        var outcome = StageOutcome.Failed;

        try
        {
            var result = await stage.RunAsync(context);
            outcome = result.IsCancel ? StageOutcome.Canceled : result.IsRedirect ? StageOutcome.Redirected : StageOutcome.Continued;

            return result;
        }
        catch (OperationCanceledException)
        {
            outcome = StageOutcome.Canceled;

            throw;
        }
        finally
        {
            trace.EndStage(span, outcome);
        }
    }

    /// <summary>A stage's span and record name: its type name without "Stage", lower case (<c>GuardStage</c> → <c>guard</c>).</summary>
    static string StageName(INavigationStage stage)
    {
        var name = stage.GetType().Name;

        return (name.Length > 5 && name.EndsWith("Stage", StringComparison.Ordinal) ? name[..^5] : name).ToLowerInvariant();
    }

    /// <summary>What a committed state shows besides its page: not found, a redirect loop, or an error kind at its level.</summary>
    static string? CommitDetail(RouteState state)
        => state.Error is { } error ? (error.Level is { } level ? $"{error.Kind}@{level}" : error.Kind.ToString())
            : state.Failure != RouteFailure.None ? state.Failure.ToString()
            : null;

    /// <summary>Discards the data this navigation loaded itself (not data it reused from the committed state).</summary>
    static void DiscardNewData(RouteNavigationContext context)
    {
        foreach (var data in context.LevelData)
        {
            if (data is not null && context.FromState?.LevelData.Contains(data) != true)
            {
                data.Discard();
            }
        }
    }

    static void DiscardLevelData(RouteState? discarded, RouteState? keep, RouteState? alsoKeep = null)
    {
        if (discarded is null)
        {
            return;
        }

        foreach (var data in discarded.LevelData)
        {
            if (data is not null && keep?.LevelData.Contains(data) != true && alsoKeep?.LevelData.Contains(data) != true)
            {
                data.Discard();
            }
        }
    }

    bool IsCurrentLocation(BaseRelativeLocation target)
        => _store.Current is { } current
            && string.Equals(current.Path, target.Path, StringComparison.Ordinal)
            && string.Equals(current.Query, target.Query, StringComparison.Ordinal);

    /// <summary>
    /// True when <paramref name="target"/> is exactly where the user is: this router committed a state, no navigation
    /// waits on its hook (the platform would show that navigation's URL instead), the committed page has the target's
    /// path and query, and the platform shows the target, fragment included (after a same-page fragment jump, which
    /// commits nothing, only the platform knows the fragment). The first two terms are guards the last two imply today: a
    /// navigation waiting on its hook ran its pipeline because its path or query differed from the committed page, and the
    /// platform shows it, so the target cannot match both; and before this router commits, the store holds an earlier
    /// router's state, which the location-changing handler's same-location shortcut would let a navigated hop through on
    /// anyway (nothing commits either way).
    /// </summary>
    bool IsWhereTheUserIs(BaseRelativeLocation target)
        => _committedSinceAttach
            && _arrived is null
            && IsCurrentLocation(target)
            && BaseUriHelper.TryGetBaseRelative(_adapter.BaseUri, _adapter.Uri, out var shown)
            && shown == target;

    static string StripFragment(string uri)
    {
        var index = uri.IndexOf('#');

        return index < 0 ? uri : uri[..index];
    }

    void Detach(IDisposable registration)
    {
        registration.Dispose();
        _adapter.LocationChanged -= OnLocationChanged;
        _authenticationSubscription?.Dispose();
        _authenticationSubscription = null;
        _recheckPending = false;
        if (Interlocked.Exchange(ref _recheckLifetime, null) is { } recheckLifetime)
        {
            recheckLifetime.Cancel();
            recheckLifetime.Dispose();
        }
        _store.ReloadAll = null;
        _store.Revalidate = null;
        _store.Preload = null;
        _stale = null;
        Interlocked.Exchange(ref _active, null)?.Cancel();
        _redirect = null;
        ReleasePending(disposing: true);

        // Whatever is still in flight dies with the router: nobody is left to settle it.
        foreach (var completion in _completions.Values)
        {
            completion.Complete(new NavigationResult(NavigationStatus.Cancelled));
        }

        _completions.Clear();

        // Releases the state before canceling the hook: its continuation runs synchronously and must find nothing to commit.
        ReleaseArrived(disposing: true);
        OnNavigate = null;
        Interlocked.Exchange(ref _onNavigateCancellation, null)?.Cancel();
        DiscardLevelData(_store.Current, null);
        _store.SetNavigating(false);
        _committedSinceAttach = false;
        _attached = false;
    }

    sealed class Lease(NavigationCoordinator owner, IDisposable registration) : IDisposable
    {
        bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner.Detach(registration);
            }
        }
    }
}
