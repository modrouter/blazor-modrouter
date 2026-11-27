using Microsoft.AspNetCore.Components;
using ModRouter.Crm.Guide;
using ModRouter.Crm.Data;
using ModRouter.Navigation;

namespace ModRouter.Crm.Crm;

/// <summary>The CRM's app bar and navigation. The guide's chapter decides which parts are switched on.</summary>
public partial class CrmLayout : IDisposable
{
    static readonly Role[] Roles = [Role.Rep, Role.Manager, Role.Admin, Role.SignedOut];

    [Inject]
    Session Session { get; set; } = default!;

    [Inject]
    Tour Tour { get; set; } = default!;

    [Inject]
    INavigationService Navigation { get; set; } = default!;

    protected override void OnInitialized()
    {
        Session.Changed += OnChanged;
        Tour.Changed += OnChanged;
    }

    public void Dispose()
    {
        Session.Changed -= OnChanged;
        Tour.Changed -= OnChanged;
    }

    static string Label(Role role) => role switch { Role.Admin => "Admin", Role.Manager => "Manager", Role.Rep => "Sales rep", _ => "Nobody (signed out)" };

    void OnChanged() => _ = InvokeAsync(StateHasChanged);

    void OnRole(ChangeEventArgs e)
    {
        if (Enum.TryParse<Role>(e.Value?.ToString(), out var role))
        {
            Session.SignInAs(role);
        }
    }
}
