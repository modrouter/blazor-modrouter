using ModRouter.Core.Guards;

namespace ModRouter.Authorization;

/// <summary>
/// Marks a guard whose decision depends on the authentication state. When the application's
/// <c>AuthenticationStateProvider</c> raises <c>AuthenticationStateChanged</c> (a sign-in, a sign-out, a role change),
/// the router runs only these guards of the committed chain again, parent first, and reloads the current URL when a
/// level's outcome changed. The router's built-in <c>[Authorize]</c> guard implements it; an application guard that reads the
/// authentication state should too.
/// </summary>
public interface IAuthorizationRouteGuard : IRouteGuard;
