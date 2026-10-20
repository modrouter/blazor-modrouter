using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;

namespace ModRouter.Sample;

public partial class StatePanel
{
    [Inject]
    INavigationService Navigation { get; set; } = default!;

    RouteState? State => Navigation.Current;

    protected override void OnInitialized() => Navigation.RouteStateChanged += OnStateChanged;

    public void Dispose() => Navigation.RouteStateChanged -= OnStateChanged;

    void OnStateChanged(RouteState state) => _ = InvokeAsync(StateHasChanged);
}
