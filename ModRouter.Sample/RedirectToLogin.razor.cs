using Microsoft.AspNetCore.Components;

namespace ModRouter.Sample;

public partial class RedirectToLogin
{
    [Inject]
    NavigationManager Navigation { get; set; } = default!;

    // The return URL is rooted and includes the base path ("/myapp/reports" under "/myapp/"), like the sample's SignedInGuard:
    // the login page hands it to NavigateTo, which resolves a rooted URL against the host, not the base.
    protected override void OnInitialized()
        => Navigation.NavigateTo("login?returnUrl=" + Uri.EscapeDataString(new Uri(Navigation.Uri).PathAndQuery), replace: true);
}
