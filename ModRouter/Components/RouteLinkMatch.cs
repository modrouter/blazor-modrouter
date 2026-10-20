namespace ModRouter.Components;

/// <summary>How a route link decides that it points at the current page.</summary>
public enum RouteLinkMatch
{
    /// <summary>The link points at exactly the current page (the leaf of the matched route, or the parent of an index leaf, which renders at the parent's URL; exactly the current path for an <c>Href</c>).</summary>
    Exact,

    /// <summary>The link points at the current page or one of its ancestors (its route is anywhere in the matched chain, or its path is a segment prefix of the current path).</summary>
    Prefix,
}
