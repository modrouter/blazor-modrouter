using ModRouter.Core.Breadcrumbs;
using ModRouter.Navigation;

namespace ModRouter.Components;

/// <summary>One crumb of a route breadcrumb: a label, a link (null for the current page and the ellipsis) and whether it is the current page.</summary>
internal readonly record struct RouteCrumb(string Label, string? Href, bool IsCurrent);

/// <summary>
/// Builds the breadcrumb trail of the committed route chain. Shared by <see cref="RouteBreadcrumb"/> and the design-system
/// breadcrumb of the satellite package, so both show the same trail.
/// </summary>
internal static class RouteCrumbs
{
    /// <summary>The marker the collapsed middle of a long trail shows.</summary>
    public const string Ellipsis = "…";

    /// <summary>Checks the <c>MaxItems</c> parameter of a breadcrumb component.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxItems"/> is set and below 3.</exception>
    public static void ValidateMaxItems(int? maxItems)
    {
        if (maxItems is < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(maxItems), maxItems, "MaxItems must be at least 3 (the first crumb, the ellipsis and the current page).");
        }
    }

    /// <summary>
    /// The trail of the current state, empty when no route matched. <paramref name="home"/> adds a leading crumb to the
    /// application root unless a titled root route already starts the trail; <paramref name="maxItems"/> keeps the first and
    /// the last crumbs and replaces the middle with <see cref="Ellipsis"/>.
    /// </summary>
    public static IReadOnlyList<RouteCrumb> Build(INavigationService navigation, string? home, int? maxItems)
    {
        var basePath = new Uri(navigation.BaseUri).AbsolutePath.TrimEnd('/');
        var entries = navigation.Current?.Chain is { } chain ? BreadcrumbTrail.From(chain) : [];
        var crumbs = new List<(string Label, string? Href)>();

        // A titled root route ("/") is its own first crumb and already links to the application root: adding Home too
        // would show the root twice.
        if (entries.Count > 0 && home is not null && entries[0].Path != "/")
        {
            crumbs.Add((home, basePath + "/"));
        }

        crumbs.AddRange(entries.Select(entry => (entry.Label, (string?)(basePath + entry.Path))));

        if (maxItems is { } max && crumbs.Count > max)
        {
            crumbs = [crumbs[0], (Ellipsis, null), .. crumbs.TakeLast(max - 2)];
        }

        // The last crumb is the current page: no link.
        return [.. crumbs.Select((crumb, index) => index == crumbs.Count - 1
            ? new RouteCrumb(crumb.Label, null, true)
            : new RouteCrumb(crumb.Label, crumb.Href, false))];
    }
}
