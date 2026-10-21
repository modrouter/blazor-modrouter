using Microsoft.AspNetCore.Components;
using ModRouter.Lab.Services;
using ModRouter.Navigation;

namespace ModRouter.Lab.Lessons.L5;

public partial class Lesson5Page : IDisposable
{
    [Inject]
    LoaderKnobs Knobs { get; set; } = default!;

    [Inject]
    LoaderLedger Ledger { get; set; } = default!;

    [Inject]
    INavigationService Navigation { get; set; } = default!;

    protected override void OnInitialized()
    {
        Knobs.Changed += OnChanged;
        Ledger.Changed += OnChanged;
    }

    public void Dispose()
    {
        Knobs.Changed -= OnChanged;
        Ledger.Changed -= OnChanged;
    }

    static int ToInt(ChangeEventArgs e) => int.TryParse(e.Value?.ToString(), out var value) ? value : 0;

    void OnChanged() => _ = InvokeAsync(StateHasChanged);
}
