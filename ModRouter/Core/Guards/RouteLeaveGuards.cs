using ModRouter.Core.Matching;

namespace ModRouter.Core.Guards;

/// <summary>
/// Decides whether a route level that is on screen may be left: the "you have unsaved changes" check. Leave guards are
/// attached with <c>RouteMap.LeaveGuard&lt;T&gt;()</c> (or <c>[RouteLeaveGuard&lt;T&gt;]</c>), resolved from the navigation
/// scope's DI container (or created with it), and run before any enter guard, deepest level first, one at a time, in
/// declaration order. A guard must honor <see cref="RouteLeaveContext.CancellationToken"/>; it may await a confirmation
/// dialog, and the old page stays on screen until it answers.
/// </summary>
/// <remarks>
/// A level is left when the navigation's target does not show the same route at that depth: another page, a sibling,
/// a parent, or no match at all. A parameter change on the same route (<c>/customers/1</c> to <c>/customers/2</c>) does not
/// leave it, and neither does opening a child. A leave guard does not run for the first load, a reload, the later hops of
/// a redirect chain, a navigation out of the application (use a <c>NavigationLock</c> with <c>ConfirmExternalNavigation</c>
/// for the browser's own prompt), or a level that showed an error instead of its page.
/// </remarks>
public interface IRouteLeaveGuard
{
    /// <summary>
    /// Decides whether <paramref name="context"/>'s level may be left. The first result other than
    /// <see cref="RouteLeaveResult.Allow"/> ends the stage. An exception that is not the navigation's own cancellation fails
    /// the navigation, and the old page stays.
    /// </summary>
    ValueTask<RouteLeaveResult> CanDeactivateAsync(RouteLeaveContext context);
}

/// <summary>
/// Implemented by a route component that knows whether it may be left or have its route parameters changed: the page asks
/// itself, with its own state at hand, instead of a guard asking from outside. The router calls it on the instance on screen,
/// before the route-declared leave guards of the same level, deepest level first. It is asked when its level is left (see
/// <see cref="IRouteLeaveGuard"/> for when that is) and also when the same route stays but its route parameters change
/// (<see cref="RouteLeaveContext.IsParameterChange"/>), which the route-declared guards are not asked about. A query-only
/// navigation never asks. The same rules apply to the result and to what a thrown exception does.
/// </summary>
public interface IRouteNavigationLock
{
    /// <summary>Decides whether the navigation described by <paramref name="context"/> may take this component off screen or change its parameters.</summary>
    ValueTask<RouteLeaveResult> CanLeaveAsync(RouteLeaveContext context);
}

/// <summary>What a leave guard decided.</summary>
public enum RouteLeaveOutcome
{
    /// <summary>Let the navigation continue.</summary>
    Allow,

    /// <summary>Prevent the navigation; the old page and URL stay.</summary>
    Cancel,

    /// <summary>Navigate somewhere else instead.</summary>
    Redirect,
}

/// <summary>A leave guard's decision. Create it with the static factories.</summary>
public sealed class RouteLeaveResult
{
    static readonly RouteLeaveResult AllowResult = new(RouteLeaveOutcome.Allow);
    static readonly RouteLeaveResult CancelResult = new(RouteLeaveOutcome.Cancel);

    RouteLeaveResult(RouteLeaveOutcome outcome)
    {
        Outcome = outcome;
    }

    /// <summary>What the guard decided; it tells whether <see cref="Url"/> is set.</summary>
    public RouteLeaveOutcome Outcome { get; }

    /// <summary>The target of a <see cref="Redirect"/> (base-relative, rooted in the app, or absolute).</summary>
    public string? Url { get; private init; }

    /// <summary>Overrides the history behavior of a redirect: true replaces the current entry, false pushes, null uses the default.</summary>
    public bool? ReplaceHistory { get; private init; }

    /// <summary>Let the navigation continue.</summary>
    public static RouteLeaveResult Allow() => AllowResult;

    /// <summary>Keep the old page and URL; the navigation is prevented.</summary>
    public static RouteLeaveResult Cancel() => CancelResult;

    /// <summary>Navigate somewhere else instead (an absolute URL outside the app does a full page load).</summary>
    public static RouteLeaveResult Redirect(string url, bool? replaceHistory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        return new RouteLeaveResult(RouteLeaveOutcome.Redirect) { Url = url, ReplaceHistory = replaceHistory };
    }
}

/// <summary>Everything a leave guard may need. Public so guards can be unit tested.</summary>
public sealed class RouteLeaveContext(
    RouteContext level,
    int depth,
    RouteMatchChain chain,
    string to,
    string query,
    IServiceProvider services,
    CancellationToken cancellationToken,
    bool isParameterChange = false)
{
    /// <summary>The route level on screen that the navigation would leave.</summary>
    public RouteContext Level { get; } = level;

    /// <summary>The chain on screen (the committed one), of which <see cref="Level"/> is one level.</summary>
    public RouteMatchChain Chain { get; } = chain;

    /// <summary>The zero-based position of <see cref="Level"/> in <see cref="Chain"/>.</summary>
    public int Depth { get; } = depth;

    /// <summary>The absolute URI being navigated to.</summary>
    public string To { get; } = to;

    /// <summary>The raw query of the target including '?', or empty.</summary>
    public string Query { get; } = query;

    /// <summary>
    /// True when the level is not left: the same route stays and only its route parameters change (<c>/customers/1</c> to
    /// <c>/customers/2</c>). Only <see cref="IRouteNavigationLock"/> is asked in that case.
    /// </summary>
    public bool IsParameterChange { get; } = isParameterChange;

    /// <summary>The navigation scope's services (resolve the scoped state of the page's own form here).</summary>
    public IServiceProvider Services { get; } = services;

    /// <summary>Canceled when this navigation is superseded or canceled. A guard must pass it to everything it awaits.</summary>
    public CancellationToken CancellationToken { get; } = cancellationToken;
}
