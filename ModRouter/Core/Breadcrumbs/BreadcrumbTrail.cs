using ModRouter.Core.Definitions;
using ModRouter.Core.Matching;

namespace ModRouter.Core.Breadcrumbs;

/// <summary>One step of a breadcrumb trail: its label and the base-relative path of the route level it stands for.</summary>
public sealed record BreadcrumbEntry(string Label, string Path);

/// <summary>
/// Builds the breadcrumb trail of a matched chain. A level contributes a crumb when it is not an index route (it shares
/// its parent's path, so the parent already stands for it), is not hidden by <see cref="RouteBreadcrumbMetadata"/> and
/// has a label: the metadata's label or factory, else the level's title. The last entry is the current page.
/// </summary>
public static class BreadcrumbTrail
{
    /// <summary>
    /// The crumbs of <paramref name="chain"/>, root first; empty when no level has a label. Never null. Each entry's
    /// path is the level's <see cref="RouteContext.MatchedPath"/>, so it is base-relative and already normalized.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="chain"/> is null.</exception>
    public static IReadOnlyList<BreadcrumbEntry> From(RouteMatchChain chain)
    {
        ArgumentNullException.ThrowIfNull(chain);

        var entries = new List<BreadcrumbEntry>();

        foreach (var level in chain.Matches)
        {
            if (level.Route.SharesParentPath)
            {
                continue;
            }

            var metadata = level.Metadata.Get<RouteBreadcrumbMetadata>();

            if (metadata is { Hidden: true })
            {
                continue;
            }

            var label = metadata switch
            {
                { Label: { } fixedLabel } => fixedLabel,
                { Factory: { } factory } => factory(level.InheritedParameters),
                _ => level.Title,
            };

            if (!string.IsNullOrWhiteSpace(label))
            {
                entries.Add(new BreadcrumbEntry(label, level.MatchedPath));
            }
        }

        return entries;
    }
}
