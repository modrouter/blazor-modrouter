using Microsoft.AspNetCore.Components;
using ModRouter.Lab.Services;

namespace ModRouter.Lab.Layout;

public partial class LessonFrame
{
    [Parameter, EditorRequired]
    public int Number { get; set; }

    [Parameter]
    public RenderFragment? Try { get; set; }

    [Parameter]
    public RenderFragment? Stage { get; set; }

    [Parameter]
    public RenderFragment? Code { get; set; }

    [Inject]
    LessonProgress Progress { get; set; } = default!;

    Lesson Lesson => LessonCatalog.ByNumber(Number)!;

    Lesson? Previous => LessonCatalog.ByNumber(Number - 1);

    Lesson? Next => LessonCatalog.ByNumber(Number + 1);

    protected override void OnParametersSet() => Progress.Visit(Number);
}
