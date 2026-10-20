using System.Diagnostics.CodeAnalysis;
using ModRouter.Core.Definitions;

namespace ModRouter.Core.Generation;

/// <summary>Looks up routes by name and generates URLs from them. Implemented by the compiled table and, in DI, by the <c>RouteTableHolder</c> that serves the current table.</summary>
public interface INamedRouteRegistry
{
    /// <summary>
    /// Looks a route up by name, ignoring case; false (and a null <paramref name="route"/>) when no route has that
    /// name.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="routeName"/> is null.</exception>
    bool TryGetRoute(string routeName, [NotNullWhen(true)] out RouteDefinition? route);

    /// <summary>Like <see cref="TryGetRoute"/> but throws when the name is unknown.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="routeName"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">No route has that name.</exception>
    RouteDefinition GetRequiredRoute(string routeName);

    /// <summary>
    /// Generates an app-relative URL (starting with '/') from the route's canonical template. Parameter precedence:
    /// explicit values, then <paramref name="ambientValues"/> (only when supplied), then an omitted optional.
    /// </summary>
    /// <exception cref="RouteUrlGenerationException">The route or a value is invalid.</exception>
    string GenerateUrl(
        string routeName,
        IReadOnlyDictionary<string, object?>? parameters = null,
        IReadOnlyDictionary<string, object?>? query = null,
        string? fragment = null,
        IReadOnlyDictionary<string, object?>? ambientValues = null);

    /// <summary>
    /// The non-throwing form of <see cref="GenerateUrl"/>: returns false with <paramref name="error"/> set instead of
    /// throwing <see cref="RouteUrlGenerationException"/>.
    /// </summary>
    bool TryGenerateUrl(
        string routeName,
        IReadOnlyDictionary<string, object?>? parameters,
        IReadOnlyDictionary<string, object?>? query,
        string? fragment,
        IReadOnlyDictionary<string, object?>? ambientValues,
        [NotNullWhen(true)] out string? url,
        [NotNullWhen(false)] out RouteUrlGenerationError? error);
}
