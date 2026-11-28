using Microsoft.AspNetCore.Components;
using ModRouter.Core.Matching;
using ModRouter.Diagnostics;
using ModRouter.Navigation;

namespace ModRouter.Crm.Guide;

/// <summary>One row of the stage list: a dot and name on the timeline, a note, and the time the stage took.</summary>
public sealed record StageRow(string Name, string Hint, string Dot, string Text, string? Note, string Ms);

public sealed record HistoryLine(string Outcome, string Target, string Class);

public sealed record LogSummary(string Text, string Class);

/// <summary>Shows what the router did with the last navigation, in the order it did it.</summary>
public partial class RouterLog : IDisposable
{
    // The stages of a navigation in the order the router runs them, with what each one does.
    static readonly (string Name, string Hint)[] Pipeline =
    [
        ("Normalize", "Clean up the address."),
        ("Recognize", "Find the matching route."),
        ("Bind", "Read the route values."),
        ("LeaveGuard", "May the current page be left?"),
        ("Redirect", "Apply redirect rules."),
        ("Guard", "May this navigation go on?"),
        ("Loader", "Fetch the page data."),
        ("Commit", "Show the new page."),
    ];

    [Inject]
    INavigationService Navigation { get; set; } = default!;

    [Inject]
    RouterDiagnostics Diagnostics { get; set; } = default!;

    RouteState? State => Navigation.Current;

    NavigationRecord? Last { get; set; }

    IReadOnlyList<StageRow> Steps { get; set; } = [];

    IReadOnlyList<HistoryLine> Earlier { get; set; } = [];

    LogSummary Summary { get; set; } = new(string.Empty, string.Empty);

    // Below lg the log starts folded; the Show/Hide button flips this. From lg up CSS keeps the body open.
    bool Open { get; set; }

    void Toggle() => Open = !Open;

    protected override void OnInitialized()
    {
        Navigation.RouteStateChanged += OnStateChanged;
        Navigation.NavigatingChanged += OnChanged;
        Diagnostics.Changed += OnChanged;
        Refresh();
    }

    public void Dispose()
    {
        Navigation.RouteStateChanged -= OnStateChanged;
        Navigation.NavigatingChanged -= OnChanged;
        Diagnostics.Changed -= OnChanged;
    }

    static string Describe(RouteContext level) => level.RouteName ?? (level.Route.IsGroup ? "layout" : level.Template);

    void OnStateChanged(RouteState state) => OnChanged();

    void OnChanged() => _ = InvokeAsync(() =>
    {
        Refresh();
        StateHasChanged();
    });

    void Refresh()
    {
        var history = Diagnostics.History;

        Last = history.Count > 0 ? history[^1] : null;
        Steps = BuildSteps(Last);
        Summary = Summarize(Last);
        Earlier = history.Reverse().Skip(1).Take(3).Select(r => new HistoryLine(OutcomeName(r), r.Target, OutcomeClass(r))).ToList();
    }

    static LogSummary Summarize(NavigationRecord? record)
    {
        if (record is null)
        {
            return new(string.Empty, string.Empty);
        }

        var ms = record.Duration.TotalMilliseconds < 1 ? "<1" : record.Duration.TotalMilliseconds.ToString("0");

        return new($"{OutcomeName(record)} in {ms} ms", OutcomeClass(record));
    }

    static string OutcomeName(NavigationRecord r) => r.Outcome switch
    {
        NavigationRecordOutcome.Committed => "shown",
        NavigationRecordOutcome.Redirected => "redirect",
        NavigationRecordOutcome.Canceled => "canceled",
        NavigationRecordOutcome.Superseded => "replaced",
        _ => "failed",
    };

    static string OutcomeClass(NavigationRecord r) => r.Outcome switch
    {
        NavigationRecordOutcome.Committed => "text-statusSuccessForeground",
        NavigationRecordOutcome.Redirected => "text-brandForeground",
        NavigationRecordOutcome.Canceled or NavigationRecordOutcome.Superseded => "text-statusWarningForeground",
        _ => "text-statusDangerForeground",
    };

    // Dot colour and text colour of a row, by what the stage did.
    static readonly (string Dot, string Text) Idle = ("bg-stroke", "text-foreground3");
    static readonly (string Dot, string Text) Pass = ("bg-statusSuccessForeground", "text-foreground");
    static readonly (string Dot, string Text) Redirect = ("bg-brandForeground", "text-brandForeground");
    static readonly (string Dot, string Text) Cancel = ("bg-statusWarningForeground", "text-statusWarningForeground");
    static readonly (string Dot, string Text) Fail = ("bg-statusDangerForeground", "text-statusDangerForeground");

    static IReadOnlyList<StageRow> BuildSteps(NavigationRecord? record)
    {
        var steps = new List<StageRow>(Pipeline.Length);
        var stopped = false;

        foreach (var (name, hint) in Pipeline)
        {
            var look = Idle;
            string? note = null;
            var ms = string.Empty;

            if (record is null)
            {
                steps.Add(new StageRow(name, hint, look.Dot, look.Text, null, ms));
                continue;
            }

            if (name == "Commit")
            {
                (look, note) = record.Outcome switch
                {
                    NavigationRecordOutcome.Committed when record.OutcomeDetail is null => (Pass, "Page shown"),
                    NavigationRecordOutcome.Committed => (Cancel, $"Showed: {record.OutcomeDetail}"),
                    NavigationRecordOutcome.Canceled => (Cancel, "Canceled, the old page stays"),
                    NavigationRecordOutcome.Superseded => (Cancel, "Replaced by a newer navigation"),
                    NavigationRecordOutcome.Failed => (Fail, "Failed"),
                    _ => (Idle, stopped ? "Not reached" : null),
                };
            }
            else if (record.Stages.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) is { } stage)
            {
                (look, note) = stage.Outcome switch
                {
                    StageOutcome.Continued => (Pass, null),
                    StageOutcome.Redirected => (Redirect, record.OutcomeDetail is { } to ? $"Redirected to {to}" : "Redirected"),
                    StageOutcome.Canceled => (Cancel, "Stopped the navigation here"),
                    _ => (Fail, "Failed here"),
                };
                stopped |= stage.Outcome != StageOutcome.Continued;
                ms = stage.Duration.TotalMilliseconds < 1 ? string.Empty : $"{stage.Duration.TotalMilliseconds:0} ms";
            }
            else if (stopped)
            {
                note = "Not reached";
            }

            steps.Add(new StageRow(name, hint, look.Dot, look.Text, note, ms));
        }

        return steps;
    }
}
