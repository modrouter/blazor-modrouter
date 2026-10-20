using ModRouter.Core.Matching;

namespace ModRouter.Core.Titles;

/// <summary>Which route title stands for a matched chain.</summary>
public static class RouteTitles
{
    /// <summary>
    /// The title of the chain's leaf. An index route that has none renders at its parent's path, so it takes the title of
    /// the nearest ancestor that has one (a chain of index routes is not possible, so this is at most one step); any other
    /// untitled leaf has no title.
    /// </summary>
    public static string? Effective(RouteMatchChain chain)
    {
        ArgumentNullException.ThrowIfNull(chain);

        for (var depth = chain.Matches.Count - 1; depth >= 0; depth--)
        {
            var level = chain.Matches[depth];

            if (!string.IsNullOrWhiteSpace(level.Title))
            {
                return level.Title;
            }

            if (!level.Route.SharesParentPath)
            {
                return null;
            }
        }

        return null;
    }
}
