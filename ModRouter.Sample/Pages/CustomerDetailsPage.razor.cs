using Microsoft.AspNetCore.Components;
using ModRouter.Discovery;

namespace ModRouter.Sample.Pages;

// The route is declared on the class, not with @page, so the typed-route generator can read it.
[Route("/customers/{Id:int}")]
[RouteParent(typeof(CustomersPage))]
[RouteName("customer-details")]
[RouteTitle("Customer")]
public partial class CustomerDetailsPage
{
    [Parameter]
    public int Id { get; set; }
}
