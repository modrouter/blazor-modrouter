using ModRouter.Authorization;
using ModRouter.Core.Definitions;

namespace ModRouter;

/// <summary>The fluent authorization declarations (in the same namespace as <c>AddModRouter</c>, so no extra <c>using</c> is needed).</summary>
public static class RouteMapAuthorizationExtensions
{
    /// <summary>
    /// Requires authorization for the route declared last, like <c>[Authorize(Roles = roles, Policy = policy)]</c> on its
    /// component: without arguments any authenticated user; with <paramref name="roles"/> (a comma-separated list) a user
    /// in any of them; with <paramref name="policy"/> a user the named policy admits; both together must hold. It combines
    /// with the component's own <c>[Authorize]</c> attributes (AND) and loses to <c>[AllowAnonymous]</c> or
    /// <see cref="AllowAnonymous"/>.
    /// </summary>
    public static RouteMap Authorize(this RouteMap map, string? roles = null, string? policy = null)
    {
        ArgumentNullException.ThrowIfNull(map);

        return map.WithLast(new RouteAuthorizeMetadata(roles, policy), nameof(Authorize));
    }

    /// <summary>
    /// Opens the route declared last to everyone, like <c>[AllowAnonymous]</c> on its component: no authorization guard is
    /// attached even when the component carries <c>[Authorize]</c> or the route declares <see cref="Authorize"/>.
    /// </summary>
    public static RouteMap AllowAnonymous(this RouteMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return map.WithLast(new RouteAllowAnonymousMetadata(), nameof(AllowAnonymous));
    }
}
