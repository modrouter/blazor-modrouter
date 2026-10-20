using ModRouter.Core.Guards;

namespace ModRouter.Sample.Features;

/// <summary>Sends signed-out visitors to the login page and remembers where they were going.</summary>
public sealed class SignedInGuard(SampleSession session) : IRouteGuard
{
    public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context)
    {
        if (session.IsSignedIn)
        {
            return ValueTask.FromResult(RouteGuardResult.Allow());
        }

        var returnUrl = new Uri(context.To).PathAndQuery;

        return ValueTask.FromResult(RouteGuardResult.RedirectToRoute("login", query: new Dictionary<string, object?> { ["returnUrl"] = returnUrl }));
    }
}

/// <summary>Lets the navigation commit but shows the not-authorized content at this level for non-administrators.</summary>
public sealed class AdminOnlyGuard(SampleSession session) : IRouteGuard
{
    public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context)
        => ValueTask.FromResult(session.IsAdmin ? RouteGuardResult.Allow() : RouteGuardResult.NotAuthorized("Administrators only."));
}

/// <summary>Half of a redirect loop (see <see cref="LoopBGuard"/>).</summary>
public sealed class LoopAGuard : IRouteGuard
{
    public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context)
        => ValueTask.FromResult(RouteGuardResult.Redirect("loop-b"));
}

/// <summary>The other half of the loop: it sends the user straight back.</summary>
public sealed class LoopBGuard : IRouteGuard
{
    public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context)
        => ValueTask.FromResult(RouteGuardResult.Redirect("loop-a"));
}

/// <summary>Always cancels: the old page and URL stay.</summary>
public sealed class BlockedGuard : IRouteGuard
{
    public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context)
        => ValueTask.FromResult(RouteGuardResult.Cancel());
}
