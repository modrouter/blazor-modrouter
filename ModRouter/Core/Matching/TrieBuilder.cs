using ModRouter.Core.Compilation;
using ModRouter.Core.Constraints;
using ModRouter.Core.Syntax;

namespace ModRouter.Core.Matching;

/// <summary>Builds the matching trie from matchable entries. Precedence is decided here, once, by edge order.</summary>
internal sealed class TrieBuilder
{
    readonly TrieNode _root = new();

    public void Insert(CompiledRoute route)
    {
        var segments = route.Segments;
        var hasOptional = segments.Any(s => s.Kind == TemplateSegmentKind.OptionalParameter);
        var node = _root;

        for (var i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];

            switch (segment.Kind)
            {
                case TemplateSegmentKind.Literal:
                    node = Literal(node, segment.Text);
                    break;
                case TemplateSegmentKind.Parameter:
                    node = Parameter(node, segment, route.Constraints[i]);
                    break;
                case TemplateSegmentKind.OptionalParameter:
                    node.Terminals.Add(new Terminal(route, i, true));
                    node = Parameter(node, segment, route.Constraints[i]);
                    break;
                case TemplateSegmentKind.CatchAll:
                    node.CatchAlls.Add(new Terminal(route, i, false));

                    return;
            }
        }

        node.Terminals.Add(new Terminal(route, segments.Count, hasOptional));
    }

    /// <summary>Fixes edge and terminal order and returns the finished root.</summary>
    public TrieNode Complete()
    {
        Sort(_root);

        return _root;
    }

    /// <summary>
    /// Finds route pairs that end at the same node with equal rank: the matcher could not tell them apart. Each pair
    /// is reported once, even when the routes collide at several nodes.
    /// </summary>
    public IEnumerable<(CompiledRoute First, CompiledRoute Second)> FindAmbiguities()
    {
        var reported = new HashSet<(int First, int Second)>();
        var pending = new Stack<TrieNode>();
        pending.Push(_root);

        while (pending.Count > 0)
        {
            var node = pending.Pop();

            foreach (var pair in AmbiguousPairs(node))
            {
                if (reported.Add((pair.First.Id, pair.Second.Id)))
                {
                    yield return pair;
                }
            }

            foreach (var child in node.Literals.Values)
            {
                pending.Push(child);
            }

            foreach (var edge in node.Constrained)
            {
                pending.Push(edge.Node);
            }

            if (node.Unconstrained is not null)
            {
                pending.Push(node.Unconstrained);
            }
        }
    }

    static IEnumerable<(CompiledRoute First, CompiledRoute Second)> AmbiguousPairs(TrieNode node)
    {
        foreach (var group in node.Terminals.GroupBy(t => t.ViaOptional))
        {
            var routes = group.Select(t => t.Route).ToList();

            for (var i = 1; i < routes.Count; i++)
            {
                yield return (routes[0], routes[i]);
            }
        }

        for (var i = 1; i < node.CatchAlls.Count; i++)
        {
            yield return (node.CatchAlls[0].Route, node.CatchAlls[i].Route);
        }
    }

    static TrieNode Literal(TrieNode node, string text)
    {
        if (!node.Literals.TryGetValue(text, out var next))
        {
            next = new TrieNode();
            node.Literals[text] = next;
        }

        return next;
    }

    static TrieNode Parameter(TrieNode node, TemplateSegment segment, IRouteConstraint? constraint)
    {
        if (constraint is null)
        {
            return node.Unconstrained ??= new TrieNode();
        }

        var key = ConstraintKeys.Canonical(segment.Constraints, constraint);

        foreach (var edge in node.Constrained)
        {
            if (edge.Key == key)
            {
                return edge.Node;
            }
        }

        var created = new ConstrainedEdge(key, ConstraintKeys.Count(segment.Constraints), constraint, new TrieNode());
        node.Constrained.Add(created);

        return created.Node;
    }

    // More constraints first; ties by key, so order never depends on declaration order. Routes using an optional
    // parameter rank below routes that do not, at the same node.
    static void Sort(TrieNode node)
    {
        node.Constrained.Sort(static (a, b) => a.ConstraintCount != b.ConstraintCount
            ? b.ConstraintCount.CompareTo(a.ConstraintCount)
            : string.CompareOrdinal(a.Key, b.Key));
        node.Terminals.Sort(static (a, b) => a.ViaOptional.CompareTo(b.ViaOptional));

        foreach (var child in node.Literals.Values)
        {
            Sort(child);
        }

        foreach (var edge in node.Constrained)
        {
            Sort(edge.Node);
        }

        if (node.Unconstrained is not null)
        {
            Sort(node.Unconstrained);
        }
    }
}
