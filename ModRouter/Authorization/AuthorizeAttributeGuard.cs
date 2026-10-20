using ModRouter.Core.Guards;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace ModRouter.Authorization;

/// <summary>
/// Enforces <c>[Authorize]</c> / <c>[AllowAnonymous]</c> on a route's component and the route's
/// <see cref="RouteAuthorizeMetadata"/> / <see cref="RouteAllowAnonymousMetadata"/>, deciding exactly as the built-in
/// <c>AuthorizeRouteView</c> does: the attributes are read with <c>inherit: true</c>, any <c>IAllowAnonymous</c> wins,
/// the user comes from <see cref="AuthenticationStateProvider"/> (awaited only when authorization applies, honoring the
/// navigation's cancellation token), the policy from <c>AuthorizationPolicy.CombineAsync</c>, and the decision from
/// <see cref="IAuthorizationService"/> with a null resource. A non-null <c>AuthenticationSchemes</c> throws the
/// built-in's <see cref="NotSupportedException"/>; a missing policy or an empty <c>Roles</c> throw as the built-in does.
/// A denial is <see cref="RouteGuardResult.NotAuthorized"/>: the navigation commits with a not-authorized error at that
/// level and the application's not-authorized content renders. The router attaches this guard first on every route
/// that needs it, whichever source declared the route; it runs per chain level, parent first.
/// </summary>
internal sealed class AuthorizeAttributeGuard : IAuthorizationRouteGuard
{
    /// <summary>The not-authorized message of a denied level.</summary>
    public const string DeniedMessage = "You are not authorized to view this page.";

    public async ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var route = context.Level.Route;

        if (AuthorizationRequirements.Collect(route) is not { } data)
        {
            return RouteGuardResult.Allow();
        }

        EnsureNoAuthenticationSchemeSpecified(data);

        var (authentication, policies, authorization) = RequiredServices(context, route.Component);
        var state = await authentication.GetAuthenticationStateAsync().WaitAsync(context.CancellationToken);
        var policy = await AuthorizationPolicy.CombineAsync(policies, data);

        if (policy is null)
        {
            return RouteGuardResult.Allow();
        }

        var result = await authorization.AuthorizeAsync(state.User, null, policy);

        return result.Succeeded ? RouteGuardResult.Allow() : RouteGuardResult.NotAuthorized(DeniedMessage);
    }

    /// <summary>The built-in's rule (AuthorizeViewCore): components cannot select an authentication scheme.</summary>
    static void EnsureNoAuthenticationSchemeSpecified(IReadOnlyList<IAuthorizeData> data)
    {
        foreach (var entry in data)
        {
            if (!string.IsNullOrEmpty(entry.AuthenticationSchemes))
            {
                throw new NotSupportedException($"The authorization data specifies an authentication scheme with value '{entry.AuthenticationSchemes}'. Authentication schemes cannot be specified for components.");
            }
        }
    }

    static (AuthenticationStateProvider, IAuthorizationPolicyProvider, IAuthorizationService) RequiredServices(RouteGuardContext context, Type component)
    {
        var services = context.Services;
        var authentication = services.GetService<AuthenticationStateProvider>();
        var policies = services.GetService<IAuthorizationPolicyProvider>();
        var authorization = services.GetService<IAuthorizationService>();

        if (authentication is not null && policies is not null && authorization is not null)
        {
            return (authentication, policies, authorization);
        }

        var missing = new List<string>();

        if (authentication is null)
        {
            missing.Add("AuthenticationStateProvider (the application's provider, registered as that base type)");
        }

        if (policies is null || authorization is null)
        {
            missing.Add("the authorization services (services.AddAuthorizationCore())");
        }

        throw new InvalidOperationException($"The route '{context.Level.Template}' requires authorization ({component.FullName} carries [Authorize], or the route declares Authorize), but {string.Join(" and ", missing)} is not registered.");
    }
}
