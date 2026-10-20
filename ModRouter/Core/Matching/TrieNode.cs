using ModRouter.Core.Compilation;
using ModRouter.Core.Constraints;

namespace ModRouter.Core.Matching;

/// <summary>A route that ends at a trie node, how many segments it consumed there, and whether it uses an optional parameter.</summary>
internal sealed record Terminal(CompiledRoute Route, int SegmentCount, bool ViaOptional);

/// <summary>An edge for parameters that share the same constraint key. The node is shared by every route using that key.</summary>
internal sealed class ConstrainedEdge(string key, int constraintCount, IRouteConstraint constraint, TrieNode node)
{
    public string Key { get; } = key;

    public int ConstraintCount { get; } = constraintCount;

    public IRouteConstraint Constraint { get; } = constraint;

    public TrieNode Node { get; } = node;
}

/// <summary>
/// One segment position. Matching tries, in order: <see cref="Literals"/>, <see cref="Constrained"/> (sorted),
/// <see cref="Unconstrained"/>, then <see cref="CatchAlls"/>. <see cref="Terminals"/> answer when the path ends here.
/// Mutated only while the table is built; read-only afterwards.
/// </summary>
internal sealed class TrieNode
{
    public Dictionary<string, TrieNode> Literals { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<ConstrainedEdge> Constrained { get; } = [];

    public TrieNode? Unconstrained { get; set; }

    public List<Terminal> Terminals { get; } = [];

    public List<Terminal> CatchAlls { get; } = [];
}
