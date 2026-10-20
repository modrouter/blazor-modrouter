using Microsoft.AspNetCore.Components;

namespace ModRouter.Sample.Pages;

public partial class QueryPage
{
    // Blazor's built-in query provider supplies these under ModRouter; the router never binds them from the path.
    [Parameter]
    [SupplyParameterFromQuery]
    public string? Tab { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "q")]
    public string? Search { get; set; }

    [Parameter]
    [SupplyParameterFromQuery]
    public int[]? Nums { get; set; }
}
