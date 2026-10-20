using Microsoft.AspNetCore.Components;

namespace ModRouter.Sample.Pages;

public partial class CustomerDetailsPage
{
    [Parameter]
    public int Id { get; set; }
}
