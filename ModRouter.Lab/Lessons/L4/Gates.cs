using ModRouter.Core.Guards;
using ModRouter.Lab.Services;

namespace ModRouter.Lab.Lessons.L4;

// A guard runs before a page appears and answers one question: may this navigation go on?
// It is created from the navigation's services, so it can take what it needs in the constructor.

/// <summary>Visitors are sent to the dock and told where they were going; crew and captains pass.</summary>
public sealed class CrewOnlyGuard(Clearance clearance) : IRouteGuard
{
    public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context)
    {
        if (clearance.Current >= Level.Crew)
        {
            return ValueTask.FromResult(RouteGuardResult.Allow());
        }

        // Redirect: the navigation never commits here. The router starts a new one for the dock.
        var wanted = new Uri(context.To).PathAndQuery;

        return ValueTask.FromResult(RouteGuardResult.RedirectToRoute("gate-dock", query: new Dictionary<string, object?> { ["returnUrl"] = wanted }));
    }
}

/// <summary>Only captains pass. Everyone else sees the "gate closed" content in place of the page, with the URL changed.</summary>
public sealed class CaptainOnlyGuard(Clearance clearance) : IRouteGuard
{
    public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context)
        => ValueTask.FromResult(clearance.Current == Level.Captain
            ? RouteGuardResult.Allow()
            : RouteGuardResult.NotAuthorized("The vault only opens for a captain."));
}

/// <summary>Always says no: the old page and the old URL stay exactly as they were.</summary>
public sealed class SealedGuard : IRouteGuard
{
    public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context)
        => ValueTask.FromResult(RouteGuardResult.Cancel());
}
