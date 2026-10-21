using Microsoft.AspNetCore.Components;
using ModRouter.Lab.Services;

namespace ModRouter.Lab.Layout;

public partial class LessonLine : IDisposable
{
    [Inject]
    LessonProgress Progress { get; set; } = default!;

    protected override void OnInitialized() => Progress.Changed += OnProgressChanged;

    public void Dispose() => Progress.Changed -= OnProgressChanged;

    void OnProgressChanged() => _ = InvokeAsync(StateHasChanged);
}
