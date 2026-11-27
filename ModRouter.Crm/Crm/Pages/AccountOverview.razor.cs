using Microsoft.AspNetCore.Components;
using ModRouter.Components;
using ModRouter.Crm.Crm.Routing;

namespace ModRouter.Crm.Crm.Pages;

/// <summary>The index child of an account: shown at /accounts/{id} when the address says nothing more.</summary>
public partial class AccountOverview
{
    /// <summary>The account page cascades what its loader returned, so every tab reads the same account.</summary>
    [CascadingParameter]
    AccountView View { get; set; } = default!;
}
