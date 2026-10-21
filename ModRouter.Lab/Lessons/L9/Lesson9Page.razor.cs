using Microsoft.AspNetCore.Components;
using ModRouter.Diagnostics;
using ModRouter.Navigation;

namespace ModRouter.Lab.Lessons.L9;

public partial class Lesson9Page : IDisposable
{
    [Inject]
    INavigationService Navigation { get; set; } = default!;

    [Inject]
    RouterDiagnostics Diagnostics { get; set; } = default!;

    // History is oldest first and is a snapshot; show the newest on top.
    IReadOnlyList<NavigationRecord> Records => Diagnostics.History.Reverse().Take(14).ToList();

    protected override void OnInitialized() => Diagnostics.Changed += OnChanged;

    public void Dispose() => Diagnostics.Changed -= OnChanged;

    void OnChanged() => _ = InvokeAsync(StateHasChanged);

    static string Ms(TimeSpan time) => time.TotalMilliseconds < 1 ? "<1 ms" : $"{time.TotalMilliseconds:0} ms";

    // A bar's width is its share of the time, but never so thin that a fast stage disappears.
    static int Weight(StageRecord stage) => Math.Max(3, (int)Math.Round(stage.Duration.TotalMilliseconds));

    static string Kind(NavigationRecord record) => record.Outcome switch
    {
        NavigationRecordOutcome.Committed when record.OutcomeDetail is null => "pass",
        NavigationRecordOutcome.Committed => "warn",
        NavigationRecordOutcome.Redirected => "redirect",
        _ => "stop",
    };

    static string Ending(NavigationRecord record) => record.Outcome switch
    {
        NavigationRecordOutcome.Committed when record.OutcomeDetail is { } detail => $"committed, {detail}",
        NavigationRecordOutcome.Committed => "committed",
        NavigationRecordOutcome.Redirected => "redirected",
        NavigationRecordOutcome.Canceled => "canceled",
        NavigationRecordOutcome.Superseded => "replaced",
        _ => record.Outcome.ToString().ToLowerInvariant(),
    };

    static string Describe(NavigationRecord record)
        => string.Join(", ", record.Stages.Select(s => $"{s.Name} {s.Outcome.ToString().ToLowerInvariant()} in {Ms(s.Duration)}"));
}
