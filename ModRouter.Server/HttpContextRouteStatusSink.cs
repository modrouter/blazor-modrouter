using Microsoft.AspNetCore.Http;
using ModRouter.Navigation;

namespace ModRouter;

/// <summary>
/// Answers a server-rendered request with the status of the page the router committed: 404 for an unmatched URL, 401 or 403
/// for a denied one and 500 for a failure. Sets nothing once the response has started (a streamed page) and does nothing
/// when there is no request (an interactive circuit, after the prerender).
/// </summary>
internal sealed class HttpContextRouteStatusSink(IHttpContextAccessor accessor) : IRouteStatusSink
{
    public void Report(RouteStatusKind status)
    {
        if (accessor.HttpContext is not { } context || context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = status switch
        {
            RouteStatusKind.NotFound => StatusCodes.Status404NotFound,
            RouteStatusKind.NotAuthorized => context.User.Identity?.IsAuthenticated == true ? StatusCodes.Status403Forbidden : StatusCodes.Status401Unauthorized,
            _ => StatusCodes.Status500InternalServerError,
        };
    }
}
