using System.Security.Claims;
using ModRouter.Core.Definitions;
using ModRouter.Core.Guards;
using ModRouter.Core.Matching;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ModRouter.Test;

/// <summary>
/// An <see cref="AuthenticationStateProvider"/> the tests drive: set <see cref="User"/> (or <see cref="Pending"/> for a
/// state that has not resolved yet) and call <see cref="Notify"/> to raise <c>AuthenticationStateChanged</c>, as a real
/// provider does on sign-in and sign-out.
/// </summary>
internal sealed class FakeAuthenticationStateProvider : AuthenticationStateProvider
{
    public FakeAuthenticationStateProvider(ClaimsPrincipal? user = null) => User = user ?? Users.Anonymous;

    /// <summary>The principal the next <see cref="GetAuthenticationStateAsync"/> returns.</summary>
    public ClaimsPrincipal User { get; set; }

    /// <summary>When set, <see cref="GetAuthenticationStateAsync"/> returns this (possibly unresolved) task instead.</summary>
    public Task<AuthenticationState>? Pending { get; set; }

    public int Reads { get; private set; }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        Reads++;

        return Pending ?? Task.FromResult(new AuthenticationState(User));
    }

    public void Notify() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
}

/// <summary>The principals of the authorization tests (the spike's thirteen users).</summary>
internal static class Users
{
    public static ClaimsPrincipal Anonymous => new(new ClaimsIdentity());

    public static ClaimsPrincipal Authenticated(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));

    public static Claim Role(string role) => new(ClaimTypes.Role, role);

    public static ClaimsPrincipal WithRoles(params string[] roles) => Authenticated([.. roles.Select(Role)]);

    /// <summary>Name and principal, in the spike's order.</summary>
    public static readonly (string Name, ClaimsPrincipal User)[] All =
    [
        ("Anon", Anonymous),
        ("Auth", Authenticated(new Claim(ClaimTypes.Name, "u"))),
        ("A", WithRoles("A")),
        ("B", WithRoles("B")),
        ("A+B", WithRoles("A", "B")),
        ("ITAdmin", WithRoles("IT Admin")),
        ("CapMkts", WithRoles("Capital Markets")),
        ("Emp", WithRoles("Employees")),
        ("ClaimX", Authenticated(new Claim("X", "1"))),
        ("A+ClaimX", Authenticated(Role("A"), new Claim("X", "1"))),
        ("B+ClaimX", Authenticated(Role("B"), new Claim("X", "1"))),
        ("UnauthRoleA", new ClaimsPrincipal(new ClaimsIdentity([Role("A")]))),
        ("a(lower)", WithRoles("a")),
    ];
}

/// <summary>Captures the resource an authorization handler sees (the built-in passes null; so must the guard).</summary>
internal sealed class ResourceCapture
{
    public List<string> Seen { get; } = [];
}

internal sealed class ResourceRequirement : IAuthorizationRequirement;

internal sealed class ResourceHandler(ResourceCapture capture) : AuthorizationHandler<ResourceRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ResourceRequirement requirement)
    {
        capture.Seen.Add(context.Resource?.GetType().FullName ?? "null");

        if (context.Resource is Microsoft.AspNetCore.Components.RouteData)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

internal static class AuthorizationKit
{
    /// <summary>The real authorization services (bUnit's placeholders removed) with the spike's policies, over <paramref name="provider"/>.</summary>
    public static IServiceCollection AddTestAuthorization(this IServiceCollection services, AuthenticationStateProvider provider, bool fallbackPolicy = false)
    {
        services.AddLogging();
        services.RemoveAll<AuthenticationStateProvider>();
        services.RemoveAll<IAuthorizationService>();
        services.RemoveAll<IAuthorizationPolicyProvider>();
        services.AddAuthorizationCore(options =>
        {
            options.AddPolicy("NeedsX", policy => policy.RequireClaim("X"));
            options.AddPolicy("ResourceIsRouteData", policy => policy.AddRequirements(new ResourceRequirement()));

            if (fallbackPolicy)
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            }
        });
        services.AddSingleton<ResourceCapture>();
        services.AddSingleton<IAuthorizationHandler, ResourceHandler>();
        services.AddSingleton(provider);

        return services;
    }

    /// <summary>A guard context for a one-level chain whose route renders <paramref name="component"/> with <paramref name="metadata"/>.</summary>
    public static RouteGuardContext GuardContext(Type component, IServiceProvider services, RouteMetadataCollection? metadata = null, CancellationToken cancellationToken = default)
    {
        var definition = new RouteDefinition { Template = "/x", Component = component, Metadata = metadata ?? RouteMetadataCollection.Empty };
        var level = new RouteContext(definition, "/x", RouteParameters.Empty, RouteParameters.Empty, ["x"], 1, null);

        return new RouteGuardContext(level, 0, new RouteMatchChain([level]), "http://localhost/x", null, string.Empty, true, services, cancellationToken);
    }
}
