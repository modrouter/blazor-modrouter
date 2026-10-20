using ModRouter.Core.Matching;
using Microsoft.Extensions.Logging;

namespace ModRouter.Navigation;

/// <summary>
/// How a navigation started: <see cref="InitialLoad"/>, <see cref="Link"/>, <see cref="Programmatic"/>,
/// <see cref="HistoryRestore"/> (a browser back/forward traversal, recognized when the location-changed
/// notification is intercepted but the handler's request was not), <see cref="Redirect"/> (a guard, redirect rule or
/// canonical alias sent the user here) and <see cref="Reload"/> (a retry or reload of the current URL).
/// <see cref="BrowserBack"/> and <see cref="BrowserForward"/> are reserved for later slices.
/// </summary>
public enum NavigationTrigger
{
    /// <summary>The router's first navigation, to the URL the page was loaded at.</summary>
    InitialLoad,
    /// <summary>The user activated a link whose navigation the platform intercepted.</summary>
    Link,
    /// <summary>
    /// Code asked for it (<c>NavigateTo</c>, <c>NavigateToRoute</c>); the platform did not intercept it from a link. A
    /// browser back or forward traversal also prepares with this trigger (guards, loaders and stages see it), because the
    /// platform reports the traversal only after they run; the committed state then says <see cref="HistoryRestore"/>.
    /// </summary>
    Programmatic,
    /// <summary>
    /// Reserved; the router never produces it (a browser back traversal commits as <see cref="HistoryRestore"/>).
    /// </summary>
    BrowserBack,
    /// <summary>
    /// Reserved; the router never produces it (a browser forward traversal commits as <see cref="HistoryRestore"/>).
    /// </summary>
    BrowserForward,
    /// <summary>
    /// A browser back or forward traversal; the scroll position saved for the history entry is restored when scroll
    /// restoration is enabled. Only the committed state carries it: guards, loaders and stages of that navigation run
    /// with <see cref="Programmatic"/>.
    /// </summary>
    HistoryRestore,
    /// <summary>
    /// A guard, a redirect rule or a canonical-alias redirect sent the user here;
    /// <see cref="RouteState.RedirectOrigin"/> holds the trigger the chain started with.
    /// </summary>
    Redirect,
    /// <summary>
    /// A retry or reload of the current URL (<c>ReloadAsync</c>, <see cref="RouteError.Retry"/>, or the re-check after
    /// the authentication state changed).
    /// </summary>
    Reload,
}

/// <summary>Why a navigation committed without a matched chain.</summary>
public enum RouteFailure
{
    /// <summary>The URL matched a route; there is a chain.</summary>
    None,
    /// <summary>No route matches the URL, or the URL is outside the application base.</summary>
    NotFound,
    /// <summary>A percent-escape in the path is invalid or not UTF-8.</summary>
    MalformedPath,
    /// <summary>A route matched, but its parameters could not be bound to the component's parameters.</summary>
    ParameterBindingFailed,

    /// <summary>The pipeline failed or the first navigation was canceled; <see cref="RouteState.Error"/> says why.</summary>
    NavigationFailed,

    /// <summary>Redirects looped or exceeded the limit; <see cref="RouteState.Error"/> lists the URLs.</summary>
    RedirectLoop,
}

/// <summary>The result of preparing a navigation.</summary>
internal enum NavigationOutcome
{
    /// <summary>Prepared with a matched chain; the navigation may proceed.</summary>
    Prepared,

    /// <summary>Prepared with a failure state (no chain); the navigation may proceed and the not-found content renders.</summary>
    NotFound,

    /// <summary>A newer navigation started first.</summary>
    Superseded,

    /// <summary>A stage or an outside handler canceled it.</summary>
    Cancelled,

    /// <summary>A stage threw.</summary>
    Failed,

    /// <summary>A guard or redirect rule sent the user to another URL; that navigation continues on its own.</summary>
    Redirected,
}

/// <summary>The immutable committed routing state. Swapped atomically; <see cref="Chain"/> is null when <see cref="Failure"/> is set.</summary>
public sealed class RouteState
{
    /// <summary>
    /// Identifies the navigation this state commits. Every navigation, each redirect hop included, takes the next
    /// number from one increasing counter.
    /// </summary>
    public required long NavigationId { get; init; }

    /// <summary>The absolute URI this state is for, query and fragment included.</summary>
    public required string AbsoluteUri { get; init; }

    /// <summary>Base-relative path with a leading slash, without query or fragment.</summary>
    public required string Path { get; init; }

    /// <summary>The raw query including '?', or empty.</summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>The URI fragment without the leading <c>#</c>; null when there is none.</summary>
    public string? Fragment { get; init; }

    /// <summary>
    /// The matched levels, root first. Null when <see cref="Failure"/> is not <see cref="RouteFailure.None"/>; a
    /// chain-level <see cref="Error"/> keeps it.
    /// </summary>
    public RouteMatchChain? Chain { get; init; }

    /// <summary>Why there is no chain, or <see cref="RouteFailure.None"/> when there is one.</summary>
    public RouteFailure Failure { get; init; }

    /// <summary>How the navigation that produced this state started.</summary>
    public required NavigationTrigger Trigger { get; init; }

    /// <summary>The state string stored with the history entry this state was reached through, or null.</summary>
    public string? HistoryEntryState { get; init; }

    /// <summary>Why this state shows an error instead of (or at a level of) the page; null for a normal state or plain not-found.</summary>
    public RouteError? Error { get; init; }

    /// <summary>The chain level that failed (<see cref="RouteError.Level"/>); null without a chain-level error.</summary>
    public int? FailedLevel => Error?.Level;

    /// <summary>The absolute URIs that redirected to this navigation, oldest first; empty when it was not a redirect.</summary>
    public IReadOnlyList<string> RedirectedFrom { get; init; } = [];

    /// <summary>
    /// For a state reached by a redirect (<see cref="Trigger"/> is <see cref="NavigationTrigger.Redirect"/>): the trigger
    /// of the navigation that started the chain, so a redirect at the first load or on back/forward is treated like the
    /// navigation the user actually made. Null when the state was not reached by a redirect.
    /// </summary>
    public NavigationTrigger? RedirectOrigin { get; init; }

    /// <summary>One entry per chain level: what its loader produced, or null for a level without data.</summary>
    public IReadOnlyList<RouteLevelData?> LevelData { get; init; } = [];

    /// <summary>
    /// The browser title of this state (the leaf's route title through <c>ModRouterOptions.Title</c>), or null when the
    /// document title should be left alone. The document title, the announcement and the breadcrumb leaf share it.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>The options the navigation was started with (<c>INavigationService.NavigateTo(uri, ModNavigationOptions)</c>), or null.</summary>
    public ModNavigationOptions? Options { get; init; }

    /// <summary>
    /// This state as the page of <paramref name="navigation"/>: its trigger, redirect history and options, so a reload that
    /// replaces a navigation still waiting on its <c>OnNavigateAsync</c> plans that navigation's effects (title, announcement,
    /// focus, scroll), not the reload's none.
    /// </summary>
    internal RouteState WithNavigationOf(RouteState navigation) => new()
    {
        NavigationId = NavigationId,
        AbsoluteUri = AbsoluteUri,
        Path = Path,
        Query = Query,
        Fragment = Fragment,
        Chain = Chain,
        Failure = Failure,
        Trigger = navigation.Trigger,
        HistoryEntryState = HistoryEntryState,
        Error = Error,
        RedirectedFrom = navigation.RedirectedFrom,
        RedirectOrigin = navigation.RedirectOrigin,
        LevelData = LevelData,
        Title = Title,
        Options = navigation.Options,
    };

    internal RouteState WithTrigger(NavigationTrigger trigger) => new()
    {
        NavigationId = NavigationId,
        AbsoluteUri = AbsoluteUri,
        Path = Path,
        Query = Query,
        Fragment = Fragment,
        Chain = Chain,
        Failure = Failure,
        Trigger = trigger,
        HistoryEntryState = HistoryEntryState,
        Error = Error,
        RedirectedFrom = RedirectedFrom,
        RedirectOrigin = RedirectOrigin,
        LevelData = LevelData,
        Title = Title,
        Options = Options,
    };
}

/// <summary>
/// Holds the current <see cref="RouteState"/> and announces changes. One throwing subscriber cannot stop the others
/// (or the commit): each handler runs in its own try/catch and failures are logged.
/// </summary>
public sealed class RouteStateStore(ILogger<RouteStateStore>? logger = null)
{
    // Guarded: a logging provider that throws must not stop the other subscribers or the commit.
    readonly ILogger? _logger = logger is null ? null : Diagnostics.RouterLog.Guard(logger);
    bool _isNavigating;

    /// <summary>The committed state; null until the first navigation commits. Written only by the router.</summary>
    public RouteState? Current { get; private set; }

    /// <summary>
    /// Raised after each commit with the new state, one subscriber at a time. A subscriber that throws is logged and
    /// the others still run.
    /// </summary>
    public event Action<RouteState>? Changed;

    /// <summary>True from the moment a navigation is requested until it is prepared, canceled or superseded.</summary>
    public bool IsNavigating => _isNavigating;

    /// <summary>Raised when <see cref="IsNavigating"/> changes.</summary>
    public event Action? NavigatingChanged;

    /// <summary>Set by the mounted router: reruns every loader of the current route. Null while no router is mounted.</summary>
    internal Func<CancellationToken, Task>? ReloadAll { get; set; }

    /// <summary>Set by the mounted router: reruns the loaders of the current route without guards and without a URL change.</summary>
    internal Func<CancellationToken, Task<NavigationResult>>? Revalidate { get; set; }

    /// <summary>Set by the mounted router: runs the cacheable loaders of a URL into the cache, without guards or a commit.</summary>
    internal Func<string, CancellationToken, Task>? Preload { get; set; }

    (string Uri, ModNavigationOptions Options)? _expectedOptions;

    /// <summary>Remembers the options of a navigation that is about to reach the coordinator's handler (matched by its absolute URI, fragment excluded).</summary>
    internal void ExpectOptions(string absoluteUri, ModNavigationOptions options)
    {
        // A navigation still waiting for its handler loses its expectation to the newer one: nothing will settle it.
        _expectedOptions?.Options.Completion?.Complete(new NavigationResult(NavigationStatus.Superseded));
        _expectedOptions = (absoluteUri, options);
    }

    /// <summary>The options expected for <paramref name="absoluteUri"/>, once; any other pending expectation is dropped too.</summary>
    internal ModNavigationOptions? TakeOptions(string absoluteUri)
    {
        var expected = _expectedOptions;
        _expectedOptions = null;

        if (expected is { } found && string.Equals(found.Uri, absoluteUri, StringComparison.Ordinal))
        {
            return found.Options;
        }

        // Expected for another URL: that navigation never reached the handler, so nobody else will settle it.
        expected?.Options.Completion?.Complete(new NavigationResult(NavigationStatus.Superseded));

        return null;
    }

    internal void Commit(RouteState state)
    {
        Current = state;
        Raise(Changed, state);
    }

    internal void SetNavigating(bool value)
    {
        if (_isNavigating == value)
        {
            return;
        }

        _isNavigating = value;
        Raise(NavigatingChanged);
    }

    void Raise(Action? handlers)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch (Exception exception)
            {
                if (_logger is not null)
                {
                    Diagnostics.RouterLog.StateSubscriberThrew(_logger, exception);
                }
            }
        }
    }

    void Raise(Action<RouteState>? handlers, RouteState state)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action<RouteState>>())
        {
            try
            {
                handler(state);
            }
            catch (Exception exception)
            {
                if (_logger is not null)
                {
                    Diagnostics.RouterLog.StateSubscriberThrew(_logger, exception);
                }
            }
        }
    }
}

/// <summary>The path, query and fragment of a URI relative to the application base.</summary>
public readonly record struct BaseRelativeLocation(string Path, string Query, string? Fragment);

/// <summary>
/// Converts absolute or relative URIs to base-relative locations. Every target is resolved against the base with
/// <see cref="Uri(Uri, string)"/>, exactly as <c>NavigationManager.ToAbsoluteUri</c> does: an absolute target wins,
/// a relative one (including a rooted <c>/path</c>) resolves against the base. A target is never parsed on its own,
/// because on non-Windows runtimes (browser-wasm included) <c>Uri.TryCreate("/path", UriKind.Absolute, ...)</c>
/// succeeds as a <c>file:</c> URI and would put every rooted target outside the base.
/// </summary>
internal static class BaseUriHelper
{
    /// <summary>Resolves <paramref name="uri"/> (absolute or relative to the base) to an absolute URI string.</summary>
    public static string ToAbsolute(string baseUri, string uri)
        => new Uri(new Uri(baseUri), uri).AbsoluteUri;

    /// <summary>
    /// Like <see cref="ToAbsolute"/> but returns false instead of throwing when the base or <paramref name="uri"/> cannot be
    /// parsed (a custom platform adapter can report such a string; Blazor's own navigation manager never does).
    /// </summary>
    public static bool TryToAbsolute(string baseUri, string uri, out string? absolute)
    {
        absolute = null;

        if (!Uri.TryCreate(baseUri, UriKind.Absolute, out var baseAbsolute) || !Uri.TryCreate(baseAbsolute, uri, out var target))
        {
            return false;
        }

        absolute = target.AbsoluteUri;

        return true;
    }

    /// <summary>
    /// Returns false when <paramref name="uri"/> is outside <paramref name="baseUri"/> (another host or path
    /// prefix). A relative <paramref name="uri"/> is resolved against the base first.
    /// </summary>
    public static bool TryGetBaseRelative(string baseUri, string uri, out BaseRelativeLocation location)
    {
        location = default;

        if (!Uri.TryCreate(baseUri, UriKind.Absolute, out var baseAbsolute))
        {
            return false;
        }

        if (!Uri.TryCreate(baseAbsolute, uri, out var target))
        {
            return false;
        }

        var basePath = baseAbsolute.AbsolutePath.EndsWith('/') ? baseAbsolute.AbsolutePath : baseAbsolute.AbsolutePath + "/";

        if (!string.Equals(target.GetLeftPart(UriPartial.Authority), baseAbsolute.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var targetPath = target.AbsolutePath;
        string relative;

        if (targetPath.StartsWith(basePath, StringComparison.Ordinal))
        {
            relative = targetPath[basePath.Length..];
        }
        else if (targetPath + "/" == basePath)
        {
            relative = string.Empty;
        }
        else
        {
            return false;
        }

        var fragment = target.Fragment.Length > 1 ? target.Fragment[1..] : null;
        location = new BaseRelativeLocation("/" + relative, target.Query, fragment);

        return true;
    }
}
