using Microsoft.AspNetCore.Components;

namespace ModRouter.Lab.Lessons.L1;

public partial class ShipPage
{
    [Parameter]
    public int Id { get; set; }

    Ship? Ship => Fleet.Find(Id);
}
