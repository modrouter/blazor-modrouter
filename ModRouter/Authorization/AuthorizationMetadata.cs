namespace ModRouter.Authorization;

/// <summary>
/// Declares that a route level requires authorization, like <c>[Authorize(Roles = ..., Policy = ...)]</c> on its
/// component: added by <c>RouteMap.Authorize(...)</c> and by the <c>Authorize</c>, <c>Roles</c> and <c>Policy</c>
/// parameters of <c>ModRouteEntry</c>. Several declarations on one level, and the component's own attributes, combine
/// with AND, exactly as several <c>[Authorize]</c> attributes do. The router's built-in <c>[Authorize]</c> guard enforces it.
/// </summary>
/// <param name="Roles">A comma-separated list of roles, any one of which admits the user; null for none.</param>
/// <param name="Policy">The name of an authorization policy; null for none.</param>
public sealed record RouteAuthorizeMetadata(string? Roles = null, string? Policy = null);

/// <summary>
/// Declares that a route level is open to everyone even when its component carries <c>[Authorize]</c> (from an
/// <c>_Imports.razor</c>, for example), like <c>[AllowAnonymous]</c> on the component: added by
/// <c>RouteMap.AllowAnonymous()</c> and by the <c>AllowAnonymous</c> parameter of <c>ModRouteEntry</c>. It wins over
/// every <see cref="RouteAuthorizeMetadata"/> and <c>[Authorize]</c> of the same level.
/// </summary>
public sealed record RouteAllowAnonymousMetadata;
