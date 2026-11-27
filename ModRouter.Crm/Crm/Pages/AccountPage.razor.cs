using Microsoft.AspNetCore.Components;
using ModRouter.Crm.Guide;
using ModRouter.Components;
using ModRouter.Core.Loading;
using ModRouter.Crm.Crm.Routing;
using ModRouter.Crm.Data;
using ModRouter.Navigation;

namespace ModRouter.Crm.Crm.Pages;

/// <summary>
/// The account level of the route tree: the layout around the account's tabs. The router hands it what AccountLoader returned.
/// </summary>
public partial class AccountPage : IDisposable
{
    Timer? _tick;

    /// <summary>What the router knows about this level, including the loader's data.</summary>
    [CascadingParameter]
    ModRouteData Data { get; set; } = default!;

    [Inject]
    Tour Tour { get; set; } = default!;

    [Inject]
    INavigationService Navigation { get; set; } = default!;

    AccountView View => Data.GetRequired<AccountView>();

    TimeSpan Age => DateTime.Now - View.LoadedAt;

    // What a visit would do right now, given how old the cached answer is (CacheFor: fresh for 10 s, usable for 30 s more).
    (string Kind, string Class) Freshness => Age.TotalSeconds < 10
        ? ("fresh", "bg-statusSuccessBackground2 text-statusSuccessForeground2")
        : Age.TotalSeconds < 40 ? ("stale", "bg-statusWarningBackground2 text-statusWarningForeground2") : ("expired", "bg-background3 text-foreground3");

    protected override void OnInitialized()
        => _tick = new Timer(_ => _ = InvokeAsync(StateHasChanged), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

    public void Dispose() => _tick?.Dispose();

    // RevalidateAsync runs the loaders of the page on screen again, without a URL change.
    async Task Refresh() => await Navigation.RevalidateAsync();
}
