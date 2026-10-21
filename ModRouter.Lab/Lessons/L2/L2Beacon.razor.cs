using Microsoft.AspNetCore.Components;
using ModRouter.Discovery;

namespace ModRouter.Lab.Lessons.L2;

[Route("/lessons/names/beacon/{Code}/{Channel:int?}")]
[RouteParent(typeof(Lesson2Page))]
[RouteName("beacon")]
[RouteTitle("Beacon")]
public partial class L2Beacon
{
    [Parameter]
    public string Code { get; set; } = string.Empty;

    [Parameter]
    public int? Channel { get; set; }
}
