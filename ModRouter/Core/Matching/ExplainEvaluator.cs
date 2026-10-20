using ModRouter.Core.Compilation;
using ModRouter.Core.Constraints;
using ModRouter.Core.Syntax;

namespace ModRouter.Core.Matching;

/// <summary>
/// Diagnostic evaluator: tests every matchable entry independently and picks the winner by an ordering rule that
/// is written separately from the trie. DifferentialTests prove both always agree.
/// </summary>
internal static class ExplainEvaluator
{
    readonly record struct Decision(int Tier, int Rank, string Key);

    sealed record Evaluation(List<string> Verdicts, bool IsMatch, bool Include, RouteCandidateVerdict Failure);

    public static RouteExplanation Explain(IReadOnlyList<CompiledRoute> entries, string path)
    {
        if (!PathNormalizer.TrySplit(path, out var segments))
        {
            return new RouteExplanation(path, true, [], null, null);
        }

        var included = new List<(CompiledRoute Entry, Evaluation Evaluation, Decision[]? Decisions)>();
        CompiledRoute? selected = null;
        Decision[]? selectedDecisions = null;

        foreach (var entry in entries.Where(e => e.IsMatchable))
        {
            var evaluation = Evaluate(entry, segments);
            var decisions = evaluation.IsMatch ? Decisions(entry, segments.Length) : null;

            if (evaluation.Include)
            {
                included.Add((entry, evaluation, decisions));
            }

            if (decisions is not null && (selected is null || Compare(decisions, selectedDecisions!) < 0))
            {
                selected = entry;
                selectedDecisions = decisions;
            }
        }

        // The structured verdicts are derived after the selection: a matching candidate's kind depends on the winner.
        var candidates = included.Select(c => new RouteCandidate(c.Entry.Template, c.Entry.Definition.Name, c.Evaluation.Verdicts, c.Evaluation.IsMatch)
        {
            Route = c.Entry,
            IsAlias = c.Entry.IsAlias,
            Verdict = !c.Evaluation.IsMatch ? c.Evaluation.Failure
                : ReferenceEquals(c.Entry, selected) ? RouteCandidateVerdict.Matched
                : RouteCandidateVerdict.LostOnPrecedence,
            PrecedenceReason = c.Evaluation.IsMatch && !ReferenceEquals(c.Entry, selected)
                ? PrecedenceReason(selected!, selectedDecisions!, c.Decisions!, segments)
                : null,
        }).ToList();

        return new RouteExplanation(path, false, candidates, selected?.Template, selected?.Definition.Name);
    }

    /// <summary>
    /// Why <paramref name="selected"/> ranks before a matching loser: the first path position where their decisions
    /// differ (the rule <see cref="Compare"/> applies), or declaration order when they tie.
    /// </summary>
    static string PrecedenceReason(CompiledRoute selected, Decision[] winner, Decision[] loser, string[] path)
    {
        for (var position = 0; position < winner.Length; position++)
        {
            var (won, lost) = (winner[position], loser[position]);

            if (won == lost)
            {
                continue;
            }

            var where = position < path.Length ? $"At segment {position + 1} (\"{path[position]}\")" : "At the end of the path";
            // Within a tier only two things differ: the end of the path (tier 3) ranks a template without an optional
            // parameter first, and a constrained parameter (tier 1) ranks more constraints first, then by constraint key.
            var rule = won.Tier != lost.Tier
                ? $"has {Describe(won)}, which ranks before {Describe(lost)}"
                : won.Tier == 3
                    ? "has no optional parameter, which ranks before a template with one"
                    : won.Rank != lost.Rank
                        ? $"has {Describe(won)} with {Constraints(-won.Rank)}, which ranks before one with {-lost.Rank}"
                        : KeyOrder(won.Key, lost.Key);

            return $"{where} the selected route {selected.Template} {rule}.";
        }

        return $"It ranks the same as the selected route {selected.Template}, which is declared first.";
    }

    // Two keys that read the same differ only in what defines the converted value, so name that part.
    static string KeyOrder(string won, string lost)
    {
        var (wonText, lostText) = (ConstraintKeys.Display(won), ConstraintKeys.Display(lost));

        return wonText != lostText
            ? $"has the constraint '{wonText}', which sorts before '{lostText}'"
            : $"has the constraint '{wonText}' with its value defined by '{ConstraintKeys.Defining(won)}', which sorts before "
              + $"the same constraints with the value defined by '{ConstraintKeys.Defining(lost)}'";
    }

    static string Describe(Decision decision) => decision.Tier switch
    {
        0 => "a static segment",
        1 => "a constrained parameter",
        2 => "a parameter without a constraint",
        3 => "a template that ends here",
        _ => "a catch-all parameter",
    };

    static string Constraints(int count) => count == 1 ? "1 constraint" : $"{count} constraints";

    static Evaluation Evaluate(CompiledRoute entry, string[] path)
    {
        var verdicts = new List<string>();
        var failure = RouteCandidateVerdict.SegmentMismatch;
        var segments = entry.Segments;
        var consumed = 0;
        var ok = true;
        var firstOk = false;

        for (var i = 0; i < segments.Count && ok; i++)
        {
            var segment = segments[i];
            var present = consumed < path.Length;

            switch (segment.Kind)
            {
                case TemplateSegmentKind.Literal:
                    if (!present)
                    {
                        verdicts.Add($"(end of path) → expected static segment '{segment.Text}'");
                        ok = false;
                    }
                    else if (string.Equals(segment.Text, path[consumed], StringComparison.OrdinalIgnoreCase))
                    {
                        verdicts.Add($"\"{path[consumed]}\" → static match");
                        consumed++;
                    }
                    else
                    {
                        verdicts.Add($"\"{path[consumed]}\" → failed static match (expected '{segment.Text}')");
                        ok = false;
                    }

                    break;

                case TemplateSegmentKind.CatchAll:
                    verdicts.Add($"\"{string.Join('/', path.Skip(consumed))}\" → catch-all '{segment.Text}'");
                    consumed = path.Length;

                    break;

                case TemplateSegmentKind.OptionalParameter when !present:
                    verdicts.Add($"(end of path) → optional '{segment.Text}' omitted");

                    break;

                default:
                    if (!present)
                    {
                        verdicts.Add($"(end of path) → required parameter '{segment.Text}' has no value");
                        ok = false;

                        break;
                    }

                    var constraint = entry.Constraints[i];

                    if (constraint is null)
                    {
                        verdicts.Add($"\"{path[consumed]}\" → matched '{segment.Text}'");
                        consumed++;
                    }
                    else if (constraint.TryMatch(path[consumed], out _))
                    {
                        verdicts.Add($"\"{path[consumed]}\" → matched '{segment.Text}' ({segment.ConstraintKey})");
                        consumed++;
                    }
                    else
                    {
                        verdicts.Add($"\"{path[consumed]}\" → failed {segment.ConstraintKey} constraint");
                        failure = RouteCandidateVerdict.ConstraintFailed;
                        ok = false;
                    }

                    break;
            }

            if (i == 0)
            {
                firstOk = ok;
            }
        }

        if (ok && consumed < path.Length)
        {
            verdicts.Add($"\"{path[consumed]}\" → no segment in the template to match it");
            ok = false;
        }

        var include = path.Length == 0 ? ok : segments.Count > 0 && firstOk;

        return new Evaluation(verdicts, ok, include, failure);
    }

    // One decision per path position (plus one for "the path ends here"). Lower sorts first; the first position
    // where two matching entries differ decides the winner, which is exactly the trie's depth-first order.
    static Decision[] Decisions(CompiledRoute entry, int pathLength)
    {
        var segments = entry.Segments;
        var hasCatchAll = segments.Count > 0 && segments[segments.Count - 1].Kind == TemplateSegmentKind.CatchAll;
        var catchAllAt = hasCatchAll ? segments.Count - 1 : int.MaxValue;
        var hasOptional = segments.Any(s => s.Kind == TemplateSegmentKind.OptionalParameter);
        var decisions = new Decision[pathLength + 1];

        for (var position = 0; position < pathLength; position++)
        {
            if (position >= catchAllAt)
            {
                decisions[position] = new Decision(4, 0, string.Empty);

                continue;
            }

            var segment = segments[position];

            decisions[position] = segment.Kind == TemplateSegmentKind.Literal
                ? new Decision(0, 0, string.Empty)
                : entry.Constraints[position] is null
                    ? new Decision(2, 0, string.Empty)
                    : new Decision(1, -ConstraintKeys.Count(segment.Constraints), ConstraintKeys.Canonical(segment.Constraints, entry.Constraints[position]));
        }

        decisions[pathLength] = hasCatchAll
            ? new Decision(4, 0, string.Empty)
            : new Decision(3, hasOptional ? 1 : 0, string.Empty);

        return decisions;
    }

    static int Compare(Decision[] left, Decision[] right)
    {
        for (var i = 0; i < left.Length; i++)
        {
            var result = left[i].Tier.CompareTo(right[i].Tier);

            if (result == 0)
            {
                result = left[i].Rank.CompareTo(right[i].Rank);
            }

            if (result == 0)
            {
                result = string.CompareOrdinal(left[i].Key, right[i].Key);
            }

            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }
}
