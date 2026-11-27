using Microsoft.AspNetCore.Components;
using ModRouter.Crm.Data;
using ModRouter.Navigation;

namespace ModRouter.Crm.Guide;

/// <summary>The guide: one chapter at a time, with things to try in the CRM, the code behind them and what to watch.</summary>
public partial class GuidePanel : IDisposable
{
    [Inject]
    Tour Tour { get; set; } = default!;

    [Inject]
    Session Session { get; set; } = default!;

    [Inject]
    Network Network { get; set; } = default!;

    [Inject]
    INavigationService Navigation { get; set; } = default!;

    Chapter Chapter => Tour.Chapter;

    protected override void OnInitialized()
    {
        Tour.Changed += OnChanged;
        Network.Changed += OnChanged;
    }

    public void Dispose()
    {
        Tour.Changed -= OnChanged;
        Network.Changed -= OnChanged;
    }

    void OnChanged() => _ = InvokeAsync(StateHasChanged);

    // Opening a chapter takes the CRM to the chapter's first address, so what is written and what is on screen agree.
    void Open(int number)
    {
        Tour.Go(number);
        Navigation.NavigateTo(Tour.Chapter.Start.TrimStart('/'));
    }

    void Previous() => Open(Tour.Current - 1);

    void Next() => Open(Tour.Current + 1);

    void Run(Step step)
    {
        if (step.SignIn is { } role)
        {
            Session.SignInAs(role);
        }

        if (step.Url is { } url)
        {
            // App-relative: a leading slash would leave the base path the app is served from.
            Navigation.NavigateTo(url.TrimStart('/'));
        }
    }

    void SetLatency(ChangeEventArgs e)
    {
        Network.LatencyMs = int.TryParse(e.Value?.ToString(), out var ms) ? ms : Network.LatencyMs;
        Network.Notify();
    }

    void SetOffline(ChangeEventArgs e)
    {
        Network.Offline = e.Value is true;
        Network.Notify();
    }
}
