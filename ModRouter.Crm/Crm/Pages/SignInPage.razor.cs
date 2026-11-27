using Microsoft.AspNetCore.Components;
using ModRouter.Crm.Data;
using ModRouter.Navigation;

namespace ModRouter.Crm.Crm.Pages;

/// <summary>The sign-in page, declared outside the CRM layout so it shows no navigation.</summary>
public partial class SignInPage
{
    /// <summary>The address the guard turned the visitor away from.</summary>
    [Parameter, SupplyParameterFromQuery(Name = "returnUrl")]
    public string? ReturnUrl { get; set; }

    [Inject]
    Session Session { get; set; } = default!;

    [Inject]
    INavigationService Navigation { get; set; } = default!;

    void SignIn(Role role)
    {
        Session.SignInAs(role);

        // Only a path inside the app is a safe place to return to.
        Navigation.NavigateTo(ReturnUrl is { Length: > 0 } && ReturnUrl.StartsWith('/') && !ReturnUrl.StartsWith("//") ? ReturnUrl : "/");
    }
}
