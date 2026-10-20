using ModRouter.Components;
using Microsoft.AspNetCore.Components;

namespace ModRouter.Sample.Pages;

public partial class FlakyDetailsPage
{
    [Parameter]
    public int Id { get; set; }

    [CascadingParameter]
    ModRouteData Data { get; set; } = default!;
}
