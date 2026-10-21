using Microsoft.AspNetCore.Components;
using ModRouter.Navigation;

namespace ModRouter.Lab.Pages;

public partial class HomePage
{
    [Inject]
    INavigationService Navigation { get; set; } = default!;
}
