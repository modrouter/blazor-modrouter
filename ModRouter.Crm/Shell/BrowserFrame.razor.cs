using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using ModRouter.Navigation;

namespace ModRouter.Crm.Shell;

/// <summary>A browser-shaped frame around the CRM: tab, back, forward, reload and an address bar that reads and drives the router.</summary>
public partial class BrowserFrame : IDisposable
{
    /// <summary>The page the router rendered.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Inject]
    INavigationService Navigation { get; set; } = default!;

    [Inject]
    IJSRuntime Js { get; set; } = default!;

    string Address { get; set; } = "/";

    string Title => Navigation.Title ?? "Northwind CRM";

    bool Busy => Navigation.IsNavigating;

    // The thin bar under the tab strip: it creeps towards 80% while the router works, runs to 100% when the page commits, then fades.
    enum BarPhase { Idle, Running, Done, Fading }

    BarPhase _bar = BarPhase.Idle;

    int _barRun;

    string BarClass => _bar switch
    {
        BarPhase.Running => "crm-bar-run",
        BarPhase.Done => "crm-bar-done",
        BarPhase.Fading => "crm-bar-fade",
        _ => string.Empty,
    };

    void UpdateBar()
    {
        if (Busy && _bar != BarPhase.Running)
        {
            _barRun++;
            _bar = BarPhase.Running;
        }
        else if (!Busy && _bar == BarPhase.Running)
        {
            _bar = BarPhase.Done;
            _ = FinishBarAsync(_barRun);
        }
    }

    async Task FinishBarAsync(int run)
    {
        await Task.Delay(260);
        if (run != _barRun || _bar != BarPhase.Done)
        {
            return;
        }

        _bar = BarPhase.Fading;
        await InvokeAsync(StateHasChanged);

        await Task.Delay(320);
        if (run == _barRun && _bar == BarPhase.Fading)
        {
            _bar = BarPhase.Idle;
            await InvokeAsync(StateHasChanged);
        }
    }

    protected override void OnInitialized()
    {
        Navigation.RouteStateChanged += OnStateChanged;
        Navigation.NavigatingChanged += OnChanged;
        Sync();
    }

    public void Dispose()
    {
        Navigation.RouteStateChanged -= OnStateChanged;
        Navigation.NavigatingChanged -= OnChanged;
    }

    void Sync() => Address = (Navigation.Current?.Path ?? "/") + (Navigation.Current?.Query ?? string.Empty);

    void OnStateChanged(RouteState state) => OnChanged();

    void OnChanged() => _ = InvokeAsync(() =>
    {
        Sync();
        UpdateBar();
        StateHasChanged();
    });

    void OnKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !string.IsNullOrWhiteSpace(Address))
        {
            // What is typed is an app address; without its leading slash it is relative to the base path the app is served from.
            Navigation.NavigateTo(Address.Trim().TrimStart('/'));
        }
        else if (e.Key == "Escape")
        {
            Sync();
        }
    }

    Task Back() => Js.InvokeVoidAsync("crmFrame.back").AsTask();

    Task Forward() => Js.InvokeVoidAsync("crmFrame.forward").AsTask();

    Task Reload() => Navigation.ReloadAsync();
}
