using Microsoft.AspNetCore.Components;
using ModRouter.Navigation;
using ModRouter.Core.Matching;
using ModRouter.Diagnostics;

namespace ModRouter.Lab.Hud;

public sealed record Light(string Name, string State, string Ms, int WaitMs, string Hint);

public partial class SignalStrip : IDisposable
{
    // The stages of a navigation, in the order the router runs them, with what each one does in plain words.
    static readonly (string Name, string Hint)[] Pipeline =
    [
        ("Normalize", "Clean up the address and take away the base path."),
        ("Recognize", "Find the route that matches the address."),
        ("Bind", "Read the route values and convert them to their types."),
        ("LeaveGuard", "Ask the page on screen whether it may be left."),
        ("Redirect", "Apply redirect rules."),
        ("Guard", "Ask the gates of every level whether the navigation may go on."),
        ("Loader", "Fetch the data of every level."),
        ("Commit", "Show the new page."),
    ];

    [Inject]
    INavigationService Navigation { get; set; } = default!;

    [Inject]
    RouterDiagnostics Diagnostics { get; set; } = default!;

    RouteState? State => Navigation.Current;

    IReadOnlyList<Light> Lights { get; set; } = [];

    string Verdict { get; set; } = "Waiting for the first navigation.";

    long PipeKey { get; set; }

    IEnumerable<string> Segments => (State?.Path ?? "/").Split('/', StringSplitOptions.RemoveEmptyEntries);

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

    static string Describe(RouteContext level)
        => level.RouteName ?? (level.Route.IsGroup ? "group" : level.Template);

    void OnStateChanged(RouteState state) => OnChanged();

    void OnChanged() => _ = InvokeAsync(() =>
    {
        Refresh();
        StateHasChanged();
    });

    void Refresh()
    {
        var history = Diagnostics.History;
        var record = history.Count > 0 ? history[^1] : null;

        PipeKey = record?.NavigationId ?? 0;
        Lights = BuildLights(record);
        Verdict = record is null ? "Waiting for the first navigation." : Summarize(record);
    }

    static IReadOnlyList<Light> BuildLights(NavigationRecord? record)
    {
        var lights = new List<Light>(Pipeline.Length);
        var wait = 0;

        foreach (var (name, hint) in Pipeline)
        {
            var state = "idle";
            var ms = string.Empty;

            if (record is not null && name == "Commit")
            {
                state = record.Outcome switch
                {
                    NavigationRecordOutcome.Committed when record.OutcomeDetail is null => "pass",
                    NavigationRecordOutcome.Committed => "warn",
                    NavigationRecordOutcome.Canceled => "cancel",
                    NavigationRecordOutcome.Failed => "fail",
                    _ => "idle",
                };
            }
            else if (record?.Stages.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) is { } stage)
            {
                state = stage.Outcome switch
                {
                    StageOutcome.Continued => "pass",
                    StageOutcome.Redirected => "redirect",
                    StageOutcome.Canceled => "cancel",
                    _ => "fail",
                };
                ms = stage.Duration.TotalMilliseconds < 1 ? "<1 ms" : $"{stage.Duration.TotalMilliseconds:0} ms";

                // The signal waits a little longer at a slow stage, so the animation shows where the time went.
                wait += (int)Math.Min(stage.Duration.TotalMilliseconds * 0.35, 450);
            }

            lights.Add(new Light(name, state, ms, wait, hint));
            wait += 130;
        }

        return lights;
    }

    static string Summarize(NavigationRecord record)
    {
        var took = record.Duration.TotalMilliseconds < 1 ? "under 1 ms" : $"{record.Duration.TotalMilliseconds:0} ms";
        var what = record.Outcome switch
        {
            NavigationRecordOutcome.Committed when record.OutcomeDetail is { } detail => $"committed with {detail}",
            NavigationRecordOutcome.Committed => "showed the page",
            NavigationRecordOutcome.Redirected => $"redirected to {record.OutcomeDetail}",
            NavigationRecordOutcome.Canceled => record.OutcomeDetail ?? "canceled",
            NavigationRecordOutcome.Superseded => "replaced by a newer navigation",
            _ => record.OutcomeDetail ?? record.Outcome.ToString(),
        };

        return $"{record.Target}: {what}, {took}.";
    }
}
