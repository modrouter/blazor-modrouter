using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using ModRouter.Core.Compilation;
using ModRouter.Core.Matching;
using ModRouter.Navigation;

namespace ModRouter.Lab.Lessons.L3;

public sealed record Rung(string Template, string Name, string Kind, string Why);

public partial class Lesson3Page
{
    const string Prefix = "/lessons/matching/atlas";

    static readonly string[] Presets = ["new", "42", "andromeda", "42/notes/7"];

    // The ladder shows the winner first, then routes that matched but lost, then routes a rule rejected, then the rest.
    static readonly string[] Order = ["win", "lost", "fail", "miss"];

    // "At segment 4 ("42") the selected route /lessons/.../{id:int} has ..." becomes "The winner, atlas/{id:int}, has ...".
    static readonly Regex Reason = new(@"^At segment \d+ \(""[^""]*""\) the selected route (?<route>\S+) (?<rest>.*)$", RegexOptions.Compiled);

    [Inject]
    INavigationService Navigation { get; set; } = default!;

    [Inject]
    RouteTableHolder Holder { get; set; } = default!;

    string Rest { get; set; } = "42";

    IReadOnlyList<Rung> Rows { get; set; } = [];

    protected override void OnInitialized() => Explain();

    void OnInput(ChangeEventArgs e)
    {
        Rest = e.Value?.ToString() ?? string.Empty;
        Explain();
    }

    void Pick(string preset)
    {
        Rest = preset;
        Explain();
    }

    void GoThere() => Navigation.NavigateTo($"{Prefix}/{Rest.TrimStart('/')}");

    // Explain does not navigate and does not render. It evaluates every route whose first segment fits and returns a verdict for each.
    void Explain()
    {
        var explanation = Holder.Table.Explain($"{Prefix}/{Rest.TrimStart('/')}");

        Rows = explanation.Candidates
            .Where(c => c.Template.StartsWith(Prefix, StringComparison.Ordinal) && c.Template.Length > Prefix.Length)
            .Select(c => ToRung(c, explanation))
            .OrderBy(r => Array.IndexOf(Order, r.Kind))
            .ToList();
    }

    static Rung ToRung(RouteCandidate candidate, RouteExplanation explanation)
    {
        var template = candidate.Template[Prefix.Length..];
        var name = candidate.RouteName ?? string.Empty;

        if (candidate.IsMatch && candidate.Template == explanation.SelectedTemplate)
        {
            return new Rung(template, name, "win", "Wins. This route renders.");
        }

        return candidate.Verdict switch
        {
            RouteCandidateVerdict.LostOnPrecedence => new Rung(template, name, "lost", Plain(candidate.PrecedenceReason)),
            RouteCandidateVerdict.ConstraintFailed => new Rung(template, name, "fail", string.Join(" ", candidate.Verdicts.Where(v => v.Contains("constraint", StringComparison.OrdinalIgnoreCase)).DefaultIfEmpty("A rule on a value failed."))),
            _ => new Rung(template, name, "miss", "The address does not fit this shape."),
        };
    }

    static string Plain(string? reason)
    {
        if (reason is null)
        {
            return "Matches, but a better route wins.";
        }

        var match = Reason.Match(reason);

        return match.Success
            ? $"Matches, but loses. The winner, {match.Groups["route"].Value.Replace(Prefix + "/", "atlas/")}, {match.Groups["rest"].Value}"
            : reason;
    }
}
