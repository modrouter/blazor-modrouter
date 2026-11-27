using Microsoft.AspNetCore.Components;
using ModRouter.Crm.Data;

namespace ModRouter.Crm.Crm.Pages;

/// <summary>The settings layout. The route behind it carries SignedInGuard, so every settings page needs a sign-in.</summary>
public partial class SettingsPage : IDisposable
{
    [Inject]
    Session Session { get; set; } = default!;

    protected override void OnInitialized() => Session.Changed += OnChanged;

    public void Dispose() => Session.Changed -= OnChanged;

    void OnChanged() => _ = InvokeAsync(StateHasChanged);
}
