namespace ModRouter.Core.Generation;

/// <summary>Why URL generation failed.</summary>
public enum UrlGenerationErrorCode
{
    /// <summary>No route has the requested name.</summary>
    UnknownRoute,
    /// <summary>A required parameter (not optional, not catch-all) has no value, explicit or ambient.</summary>
    MissingParameter,
    /// <summary>
    /// A non-null value was supplied for a name the template does not declare as a parameter (query values go through
    /// the query argument).
    /// </summary>
    UnknownParameter,
    /// <summary>A value does not satisfy its parameter's constraint, or formats as an empty path segment.</summary>
    ConstraintViolation,
    /// <summary>
    /// An optional parameter has a value while an earlier optional parameter has none; optional parameters can only be
    /// omitted from the right.
    /// </summary>
    OptionalGap,
}

/// <summary>A URL generation failure; <see cref="Message"/> names the route, the parameter and the template.</summary>
public sealed record RouteUrlGenerationError(UrlGenerationErrorCode Code, string Message, string? RouteName);

/// <summary>Thrown by <c>GenerateUrl</c>; <c>TryGenerateUrl</c> returns the same data without throwing.</summary>
public sealed class RouteUrlGenerationException(RouteUrlGenerationError error) : Exception(error.Message)
{
    /// <summary>The failure kind, taken from the error this exception was created with.</summary>
    public UrlGenerationErrorCode Code { get; } = error.Code;

    /// <summary>The name the caller asked for; null when the failure was not tied to a named route.</summary>
    public string? RouteName { get; } = error.RouteName;
}
