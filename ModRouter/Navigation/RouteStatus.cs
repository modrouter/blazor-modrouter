namespace ModRouter.Navigation;

/// <summary>What a committed state means for the HTTP response of a server-rendered request.</summary>
public enum RouteStatusKind
{
    /// <summary>No route matched the URL (or its values could not be bound): the response is a 404.</summary>
    NotFound,

    /// <summary>A guard denied the user a level: the response is a 401 for an anonymous user, else a 403.</summary>
    NotAuthorized,

    /// <summary>A guard, a loader or the navigation itself failed (or redirects looped): the response is a 500.</summary>
    Failed,
}

/// <summary>
/// Receives the HTTP meaning of each state <see cref="Components.ModRouter"/> commits, so a server host can answer a
/// prerendered request with the right status code. Optional: nothing is registered by default (a WebAssembly app has no
/// response), and <c>ModRouter.AspNetCore</c> registers the implementation that sets <c>HttpContext.Response.StatusCode</c>.
/// A state that is fine reports nothing.
/// </summary>
public interface IRouteStatusSink
{
    /// <summary>Called when the router commits a state that is not a normal page: not found, not authorized or failed.</summary>
    void Report(RouteStatusKind status);
}

/// <summary>Maps a committed state to the status it reports.</summary>
internal static class RouteStatus
{
    /// <summary>The status <paramref name="state"/> reports, or null for a normal page.</summary>
    public static RouteStatusKind? For(RouteState state)
    {
        if (state.Error is { } error)
        {
            return error.Kind == RouteErrorKind.NotAuthorized ? RouteStatusKind.NotAuthorized : RouteStatusKind.Failed;
        }

        return state.Failure switch
        {
            RouteFailure.None => null,
            RouteFailure.NotFound or RouteFailure.MalformedPath or RouteFailure.ParameterBindingFailed => RouteStatusKind.NotFound,
            _ => RouteStatusKind.Failed,
        };
    }
}
