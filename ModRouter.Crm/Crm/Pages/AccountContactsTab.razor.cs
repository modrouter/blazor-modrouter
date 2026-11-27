using Microsoft.AspNetCore.Components;
using ModRouter.Components;
using ModRouter.Crm.Crm.Routing;

namespace ModRouter.Crm.Crm.Pages;

/// <summary>The Contacts tab: the child route accounts/{id}/contacts.</summary>
public partial class AccountContactsTab
{
    /// <summary>The account page cascades what its loader returned, so every tab reads the same account.</summary>
    [CascadingParameter]
    AccountView View { get; set; } = default!;
}
