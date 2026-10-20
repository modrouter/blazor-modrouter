using ModRouter.Sample.Features;
using ModRouter.Components;
using Microsoft.AspNetCore.Components;

namespace ModRouter.Sample.Pages;

public partial class SlowPage
{
    [Parameter]
    public int Id { get; set; }

    [CascadingParameter]
    ModRouteData Data { get; set; } = default!;
}
