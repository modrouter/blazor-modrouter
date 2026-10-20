using ModRouter.Core.Generation;
using ModRouter.Core.Matching;

namespace ModRouter.Core.Guards;

/// <summary>
/// Decides whether a route level may be activated. Guards are attached with <c>RouteMap.Guard&lt;T&gt;()</c>, resolved
/// from the navigation scope's DI container (or created with it), and run parent-first, one at a time, in
/// declaration order. A guard must honor <see cref="RouteGuardContext.CancellationToken"/>.
/// </summary>
public interface IRouteGuard
{
    /// <summary>
    /// Decides whether <paramref name="context"/>'s level may be activated. Called once per guard per level, for every
    /// navigation that reaches the level, parent first; the first result other than
    /// <see cref="RouteGuardResult.Allow"/> ends the stage. An exception that is not the navigation's own cancellation
    /// is caught and committed as a <see cref="Navigation.RouteErrorKind.GuardFailed"/> error at that level, so it does
    /// not fail the navigation. A guard that also implements <see cref="Authorization.IAuthorizationRouteGuard"/> is
    /// called again, parent first, when an <c>AuthenticationStateProvider</c> is registered and raises
    /// <c>AuthenticationStateChanged</c>: only for the committed chain, down to the level the committed state failed at
    /// (and at that level not past an ordinary guard that denied), and not while a navigation is in flight (the check runs
    /// when it commits or ends). That call is not a navigation: <see cref="RouteGuardContext.IsInitialLoad"/> is false and
    /// the guard and <see cref="RouteGuardContext.Services"/> come from the router's services, not a navigation scope. When an outcome differs from the committed state,
    /// the router reloads the current URL, which runs every guard again.
    /// </summary>
    ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context);
}

/// <summary>What a guard decided.</summary>
public enum RouteGuardOutcome
{
    /// <summary>Let the navigation continue.</summary>
    Allow,
    /// <summary>Prevent the navigation; the previous page and URL stay.</summary>
    Cancel,
    /// <summary>Navigate somewhere else instead, by URL or by route name.</summary>
    Redirect,
    /// <summary>Commit the navigation with a not-authorized error at the guarded level.</summary>
    NotAuthorized,
}

/// <summary>A guard's decision. Create it with the static factories.</summary>
public sealed class RouteGuardResult
{
    static readonly RouteGuardResult AllowResult = new(RouteGuardOutcome.Allow);
    static readonly RouteGuardResult CancelResult = new(RouteGuardOutcome.Cancel);

    RouteGuardResult(RouteGuardOutcome outcome)
    {
        Outcome = outcome;
    }

    /// <summary>What the guard decided; it tells which of the other properties are set.</summary>
    public RouteGuardOutcome Outcome { get; }

    /// <summary>The explanation of a <see cref="RouteGuardOutcome.NotAuthorized"/> result.</summary>
    public string? Message { get; private init; }

    /// <summary>The target of a redirect given as a URL (base-relative, rooted in the app, or absolute).</summary>
    public string? Url { get; private init; }

    /// <summary>The target of a redirect given as a route name.</summary>
    public string? RouteName { get; private init; }

    /// <summary>
    /// The route parameter values of a <see cref="RedirectToRoute"/> redirect, as a dictionary; null for every other
    /// outcome.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Parameters { get; private init; }

    /// <summary>
    /// The query-string values added to the URL generated for a <see cref="RedirectToRoute"/> redirect; null when none
    /// were given or for any other outcome.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? Query { get; private init; }

    /// <summary>
    /// The fragment added to the URL generated for a <see cref="RedirectToRoute"/> redirect; null when none was given
    /// or for any other outcome.
    /// </summary>
    public string? Fragment { get; private init; }

    /// <summary>Overrides the history behavior of a redirect: true replaces the current entry, false pushes, null uses the default.</summary>
    public bool? ReplaceHistory { get; private init; }

    /// <summary>Let the navigation continue.</summary>
    public static RouteGuardResult Allow() => AllowResult;

    /// <summary>Keep the old page and URL; the navigation is prevented.</summary>
    public static RouteGuardResult Cancel() => CancelResult;

    /// <summary>Navigate somewhere else instead (an absolute URL outside the app does a full page load).</summary>
    public static RouteGuardResult Redirect(string url, bool? replaceHistory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        return new RouteGuardResult(RouteGuardOutcome.Redirect) { Url = url, ReplaceHistory = replaceHistory };
    }

    /// <summary>Navigate to a named route instead. <paramref name="parameters"/> may be an anonymous object.</summary>
    public static RouteGuardResult RedirectToRoute(
        string routeName,
        object? parameters = null,
        IReadOnlyDictionary<string, object?>? query = null,
        string? fragment = null,
        bool? replaceHistory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeName);

        return new RouteGuardResult(RouteGuardOutcome.Redirect)
        {
            RouteName = routeName,
            Parameters = RouteValues.From(parameters),
            Query = query,
            Fragment = fragment,
            ReplaceHistory = replaceHistory,
        };
    }

    /// <summary>
    /// The user may not see this route level. The navigation still commits: levels above render, this level shows
    /// its not-authorized content.
    /// </summary>
    public static RouteGuardResult NotAuthorized(string? message = null)
        => new(RouteGuardOutcome.NotAuthorized) { Message = message };
}

/// <summary>Everything a guard may need. Public so guards can be unit tested.</summary>
public sealed class RouteGuardContext(
    RouteContext level,
    int depth,
    RouteMatchChain chain,
    string to,
    string? from,
    string query,
    bool isInitialLoad,
    IServiceProvider services,
    CancellationToken cancellationToken)
{
    /// <summary>The route level being guarded.</summary>
    public RouteContext Level { get; } = level;

    /// <summary>The whole matched chain.</summary>
    public RouteMatchChain Chain { get; } = chain;

    /// <summary>The zero-based position of <see cref="Level"/> in the chain.</summary>
    public int Depth { get; } = depth;

    /// <summary>The absolute URI being navigated to.</summary>
    public string To { get; } = to;

    /// <summary>The absolute URI of the previously committed state, or null on the first navigation.</summary>
    public string? From { get; } = from;

    /// <summary>The raw query of the target including '?', or empty.</summary>
    public string Query { get; } = query;

    /// <summary>
    /// True when this navigation is the first one after the page loaded (its trigger is
    /// <c>NavigationTrigger.InitialLoad</c>), false for every later in-app navigation.
    /// </summary>
    public bool IsInitialLoad { get; } = isInitialLoad;

    /// <summary>The navigation scope's services (resolve an <c>AuthenticationStateProvider</c> here, for example).</summary>
    public IServiceProvider Services { get; } = services;

    /// <summary>
    /// Canceled when this navigation is superseded or canceled. A guard must pass it to everything it awaits.
    /// </summary>
    public CancellationToken CancellationToken { get; } = cancellationToken;
}
