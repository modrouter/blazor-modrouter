namespace ModRouter.Core.Matching;

/// <summary>The full ancestor chain for a matched URL, root first. Outlets render one level each; they never re-match.</summary>
/// <remarks>
/// Record equality compares the <see cref="Matches"/> list by reference, so two chains from separate matches of the same URL
/// are never equal. To compare matches, compare the levels' <see cref="RouteContext.Parameters"/> (which have value equality).
/// </remarks>
public sealed record RouteMatchChain(IReadOnlyList<RouteContext> Matches)
{
    /// <summary>The last level of the chain, the route the URL matched itself.</summary>
    public RouteContext Leaf => Matches[Matches.Count - 1];

    /// <summary>True when the URL matched an alias template rather than the canonical one.</summary>
    public bool MatchedViaAlias { get; init; }

    /// <summary>The template that matched (the alias form when <see cref="MatchedViaAlias"/>).</summary>
    public string MatchedTemplate { get; init; } = string.Empty;

    /// <summary>The canonical template of the matched route, for redirects and canonical-URL policy.</summary>
    public string CanonicalTemplate { get; init; } = string.Empty;
}
