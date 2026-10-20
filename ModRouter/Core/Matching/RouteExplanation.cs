using ModRouter.Core.Compilation;

namespace ModRouter.Core.Matching;

/// <summary>
/// How one flattened route fared against a path: a verdict per segment and whether it matches overall. The
/// <c>init</c> properties are the structured side of the same verdict, for tools that style or filter candidates.
/// </summary>
public sealed record RouteCandidate(string Template, string? RouteName, IReadOnlyList<string> Verdicts, bool IsMatch)
{
    /// <summary>The flattened route entry this candidate is (its definition, canonical template and chain).</summary>
    public CompiledRoute? Route { get; init; }

    /// <summary>True when this entry is an alias of its route (<see cref="Template"/> is then the alias template).</summary>
    public bool IsAlias { get; init; }

    /// <summary>The outcome as a kind: selected, matched but outranked, or why it does not match.</summary>
    public RouteCandidateVerdict Verdict { get; init; }

    /// <summary>For <see cref="RouteCandidateVerdict.LostOnPrecedence"/>: the path position and the rule that decided against it.</summary>
    public string? PrecedenceReason { get; init; }
}

/// <summary>The structured outcome of a <see cref="RouteCandidate"/>.</summary>
public enum RouteCandidateVerdict
{
    /// <summary>It matches and is the selected route.</summary>
    Matched,

    /// <summary>It matches, but the selected route ranks before it (see <see cref="RouteCandidate.PrecedenceReason"/>).</summary>
    LostOnPrecedence,

    /// <summary>A static segment differs, the path ends before a required segment, or the path has a segment too many.</summary>
    SegmentMismatch,

    /// <summary>A parameter's value fails its constraint.</summary>
    ConstraintFailed,
}

/// <summary>A human-readable account of how a path was (or was not) matched, for debugging route precedence.</summary>
public sealed class RouteExplanation
{
    internal RouteExplanation(
        string path,
        bool isMalformed,
        IReadOnlyList<RouteCandidate> candidates,
        string? selectedTemplate,
        string? selectedRouteName)
    {
        Path = path;
        IsMalformed = isMalformed;
        Candidates = candidates;
        SelectedTemplate = selectedTemplate;
        SelectedRouteName = selectedRouteName;
    }

    /// <summary>The path that was explained, exactly as it was passed to <c>Explain</c>.</summary>
    public string Path { get; }

    /// <summary>
    /// True when the path has an invalid or non-UTF-8 percent-escape; there are then no candidates and nothing is
    /// selected.
    /// </summary>
    public bool IsMalformed { get; }

    /// <summary>Routes whose first segment matched (or, for an empty path, routes that can match it), in declaration order.</summary>
    public IReadOnlyList<RouteCandidate> Candidates { get; }

    /// <summary>The template of the winning entry, chosen by the precedence rules; null when nothing matches.</summary>
    public string? SelectedTemplate { get; }

    /// <summary>The name of the winning route; null when nothing matched or the winner is unnamed.</summary>
    public string? SelectedRouteName { get; }

    /// <summary>
    /// A multi-line report: the URL, every candidate with its per-segment verdicts, and the selected template (or
    /// <c>(none)</c>). Lines are separated by <c>\n</c> on every platform.
    /// </summary>
    public override string ToString()
    {
        var lines = new List<string> { $"URL: {Path}", string.Empty };

        if (IsMalformed)
        {
            lines.Add("The path contains a malformed percent-escape, so it cannot match any route.");
            lines.Add(string.Empty);
        }

        foreach (var candidate in Candidates)
        {
            lines.Add($"Candidate: {candidate.Template}");
            lines.AddRange(candidate.Verdicts.Select(verdict => "    " + verdict));
            lines.Add(string.Empty);
        }

        lines.Add($"Selected: {SelectedTemplate ?? "(none)"}");

        return string.Join("\n", lines);
    }
}
