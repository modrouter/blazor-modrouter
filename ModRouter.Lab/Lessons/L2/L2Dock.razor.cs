using Microsoft.AspNetCore.Components;
using ModRouter.Discovery;
using ModRouter.Navigation;

namespace ModRouter.Lab.Lessons.L2;

// A child page nests under its parent with [RouteParent]. The address carries a typed value: {Id:int}.
[Route("/lessons/names/dock/{Id:int}")]
[RouteParent(typeof(Lesson2Page))]
[RouteName("dock")]
[RouteTitle("Dock")]
public partial class L2Dock
{
    [Parameter]
    public int Id { get; set; }

    [Inject]
    INavigationService Navigation { get; set; } = default!;
}
