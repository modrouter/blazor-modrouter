using System.Diagnostics.CodeAnalysis;
using ModRouter.Core.Matching;

namespace ModRouter.Core.Compilation;

public sealed partial class CompiledRouteTable
{
    /// <summary>
    /// Matches an app-relative path such as <c>/customers/42</c> (no base path, query or fragment). Static segments
    /// beat constrained parameters, which beat unconstrained parameters, which beat optional and catch-all
    /// segments, regardless of declaration order. Never throws.
    /// </summary>
    public RouteMatchResult Match(string appRelativePath)
    {
        if (!PathNormalizer.TrySplit(appRelativePath, out var segments))
        {
            return RouteMatchResult.Malformed;
        }

        var values = new object?[segments.Length];

        return Walk(_root, 0, segments, values, out var terminal)
            ? RouteMatchResult.Success(ChainBuilder.Build(terminal, segments, values))
            : RouteMatchResult.NoMatch;
    }

    static bool Walk(TrieNode node, int index, string[] segments, object?[] values, [NotNullWhen(true)] out Terminal? terminal)
    {
        if (index == segments.Length)
        {
            if (node.Terminals.Count > 0)
            {
                terminal = node.Terminals[0];

                return true;
            }

            if (node.CatchAlls.Count > 0)
            {
                terminal = node.CatchAlls[0];

                return true;
            }

            terminal = null;

            return false;
        }

        var segment = segments[index];

        if (node.Literals.TryGetValue(segment, out var literal) && Walk(literal, index + 1, segments, values, out terminal))
        {
            return true;
        }

        foreach (var edge in node.Constrained)
        {
            if (!edge.Constraint.TryMatch(segment, out var converted))
            {
                continue;
            }

            values[index] = converted;

            if (Walk(edge.Node, index + 1, segments, values, out terminal))
            {
                return true;
            }
        }

        if (node.Unconstrained is not null)
        {
            values[index] = segment;

            if (Walk(node.Unconstrained, index + 1, segments, values, out terminal))
            {
                return true;
            }
        }

        if (node.CatchAlls.Count > 0)
        {
            terminal = node.CatchAlls[0];

            return true;
        }

        terminal = null;

        return false;
    }
}
