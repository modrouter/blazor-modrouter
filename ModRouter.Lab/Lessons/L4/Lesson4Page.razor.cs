using Microsoft.AspNetCore.Components;
using ModRouter.Lab.Services;
using ModRouter.Navigation;

namespace ModRouter.Lab.Lessons.L4;

public partial class Lesson4Page : IDisposable
{
    [Inject]
    INavigationService Navigation { get; set; } = default!;

    [Inject]
    Clearance Clearance { get; set; } = default!;

    NavigationResult? Last { get; set; }

    protected override void OnInitialized() => Clearance.Changed += OnClearanceChanged;

    public void Dispose() => Clearance.Changed -= OnClearanceChanged;

    void OnClearanceChanged() => _ = InvokeAsync(StateHasChanged);

    // NavigateToAsync navigates like NavigateTo and tells how it ended: Succeeded, Redirected, NotAuthorized, Cancelled...
    async Task Try(string routeName)
        => Last = await Navigation.NavigateToRouteAsync(routeName);

    async Task TryOld()
        => Last = await Navigation.NavigateToAsync("lessons/guards/old-bridge");
}
