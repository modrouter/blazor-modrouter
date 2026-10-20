using ModRouter.Navigation;
using ModRouter.Sample.Features;
using Microsoft.AspNetCore.Components;

namespace ModRouter.Sample.Pages;

public partial class LoginPage
{
    [Parameter]
    [SupplyParameterFromQuery]
    public string? ReturnUrl { get; set; }

    [Inject]
    SampleSession Session { get; set; } = default!;

    [Inject]
    INavigationService Navigation { get; set; } = default!;

    void SignInAsUser() => SignIn(admin: false);

    void SignInAsAdmin() => SignIn(admin: true);

    // The guard's return URL is rooted (it includes the base path), so it resolves against the host, not the base.
    void SignIn(bool admin)
    {
        Session.SignIn(admin);
        Navigation.NavigateTo(ReturnUrl ?? string.Empty);
    }
}
