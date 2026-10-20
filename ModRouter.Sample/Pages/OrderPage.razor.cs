using Microsoft.AspNetCore.Components;

namespace ModRouter.Sample.Pages;

public partial class OrderPage
{
    [Parameter]
    public Guid OrderId { get; set; }

    // Declared on the grandchild too: route values of ancestors are bound by name.
    [Parameter]
    public int Id { get; set; }
}
