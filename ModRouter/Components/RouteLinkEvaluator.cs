using System.Globalization;
using ModRouter.Core.Generation;
using ModRouter.Core.Matching;
using ModRouter.Navigation;

namespace ModRouter.Components;

/// <summary>
/// What a route link renders: its href and whether it points at the current page. Shared by <see cref="RouteLink"/> and the
/// design-system link of the satellite package, so both decide the same way.
/// </summary>
internal readonly record struct RouteLinkState(string? Href, bool IsExact, bool IsActive)
{
    /// <summary>The CSS class a link carries while it counts as the current page.</summary>
    public const string ActiveClass = "mod-route-link-active";

    /// <summary><c>page</c> for the exact current page, <c>true</c> for an ancestor, null otherwise.</summary>
    public string? AriaCurrent => IsExact ? "page" : IsActive ? "true" : null;

    /// <summary><paramref name="className"/> plus <see cref="ActiveClass"/> while the link is active.</summary>
    public string? ClassName(string? className)
        => IsActive ? string.Join(' ', new[] { className, ActiveClass }.Where(c => !string.IsNullOrEmpty(c))) : className;
}

/// <summary>Builds the href of a route link and works out whether it points at the current page.</summary>
internal static class RouteLinkEvaluator
{
    /// <summary>The state of a link to <paramref name="route"/> (or to <paramref name="href"/> when there is no route name).</summary>
    /// <exception cref="RouteUrlGenerationException">
    ///   <paramref name="route"/> is unknown, or a required parameter is missing or invalid.
    /// </exception>
    public static RouteLinkState Evaluate(
        INavigationService navigation,
        string? route,
        object? parameters,
        IReadOnlyDictionary<string, object?>? query,
        string? fragment,
        bool useAmbientValues,
        string? href,
        RouteLinkMatch? match)
    {
        var values = route is null ? null : RouteValues.From(parameters);

        var resolved = route is null
            ? href
            : navigation.GetRouteUrl(route, values, query, fragment, useAmbientValues);

        var (exact, prefix) = navigation.Current is { } state
            ? route is null ? MatchHref(navigation, state, href) : MatchRoute(state, route, values!)
            : (false, false);

        var mode = match ?? (route is null ? RouteLinkMatch.Exact : RouteLinkMatch.Prefix);

        return new RouteLinkState(resolved, exact, mode == RouteLinkMatch.Exact ? exact : prefix);
    }

    static (bool Exact, bool Prefix) MatchRoute(RouteState state, string route, IReadOnlyDictionary<string, object?> parameters)
    {
        if (state.Chain is not { } chain)
        {
            return (false, false);
        }

        // An index leaf renders at its parent's URL, so a link to that parent generates exactly the current URL: it is the
        // current page too, not merely an ancestor.
        var leaf = chain.Leaf;
        var exact = Describes(leaf, route, parameters) || (leaf.Route.IsIndex && leaf.Parent is { } parent && Describes(parent, route, parameters));
        var prefix = exact || chain.Matches.Any(level => Describes(level, route, parameters));

        return (exact, prefix);
    }

    static bool Describes(RouteContext level, string route, IReadOnlyDictionary<string, object?> parameters)
    {
        if (!string.Equals(level.RouteName, route, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Only the parameters the link names have to agree; the others (an ancestor's, an optional one) are not its business.
        foreach (var (name, value) in parameters)
        {
            if (value is null)
            {
                continue;
            }

            if (!level.InheritedParameters.Values.TryGetValue(name, out var current) || !SameValue(current, value))
            {
                return false;
            }
        }

        return true;
    }

    static (bool Exact, bool Prefix) MatchHref(INavigationService navigation, RouteState state, string? href)
    {
        if (string.IsNullOrEmpty(href) || !BaseUriHelper.TryGetBaseRelative(navigation.BaseUri, href, out var target))
        {
            return (false, false);
        }

        var link = Trim(target.Path);
        var current = Trim(state.Path);
        var exact = string.Equals(link, current, StringComparison.OrdinalIgnoreCase);
        var prefix = exact
            || link.Length == 0
            || current.StartsWith(link + "/", StringComparison.OrdinalIgnoreCase);

        return (exact, prefix);
    }

    static string Trim(string path) => path.TrimEnd('/');

    static bool SameValue(object? current, object? wanted)
        => string.Equals(
            Convert.ToString(current, CultureInfo.InvariantCulture),
            Convert.ToString(wanted, CultureInfo.InvariantCulture),
            StringComparison.OrdinalIgnoreCase);
}
