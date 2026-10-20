using ModRouter.Core.Syntax;

namespace ModRouter.Core.Matching;

/// <summary>Builds the <see cref="RouteMatchChain"/> for a successful trie walk. Runs only on success.</summary>
internal static class ChainBuilder
{
    public static RouteMatchChain Build(Terminal terminal, string[] segments, object?[] values)
    {
        var route = terminal.Route;
        var contexts = new RouteContext[route.Chain.Count];
        var inherited = RouteParameters.Empty;
        RouteContext? parent = null;
        var start = 0;

        for (var level = 0; level < contexts.Length; level++)
        {
            var isLeaf = level == contexts.Length - 1;
            var end = isLeaf ? terminal.SegmentCount : route.LevelEnds[level];
            Dictionary<string, object?>? own = null;

            for (var i = start; i < end; i++)
            {
                var segment = route.Segments[i];

                if (segment.IsParameter)
                {
                    own ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    own[segment.Text] = values[i];
                }
            }

            if (isLeaf && terminal.SegmentCount < route.Segments.Count && route.Segments[terminal.SegmentCount].Kind == TemplateSegmentKind.CatchAll)
            {
                own ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                own[route.Segments[terminal.SegmentCount].Text] = string.Join('/', segments, terminal.SegmentCount, segments.Length - terminal.SegmentCount);
            }

            var parameters = RouteParameters.Empty;

            if (own is not null)
            {
                if (inherited.Count == 0)
                {
                    // Nothing is inherited, so the level's own parameters are also its inherited ones: one wrapper over
                    // one dictionary that is never mutated again.
                    parameters = RouteParameters.Wrap(own);
                    inherited = parameters;
                }
                else
                {
                    var merged = new Dictionary<string, object?>(inherited.Values, StringComparer.OrdinalIgnoreCase);

                    foreach (var pair in own)
                    {
                        merged[pair.Key] = pair.Value;
                    }

                    parameters = RouteParameters.Wrap(own);
                    inherited = RouteParameters.Wrap(merged);
                }
            }

            var matchedCount = isLeaf ? segments.Length : end;
            var context = new RouteContext(route.Chain[level], route.LevelTemplates[level], parameters, inherited, segments, matchedCount, parent);

            if (parent is not null)
            {
                parent.Child = context;
            }

            contexts[level] = context;
            parent = context;
            start = end;
        }

        return new RouteMatchChain(contexts)
        {
            MatchedTemplate = route.Template,
            CanonicalTemplate = route.CanonicalTemplate,
            MatchedViaAlias = route.IsAlias,
        };
    }
}
