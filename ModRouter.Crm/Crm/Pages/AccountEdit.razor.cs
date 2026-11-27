using Microsoft.AspNetCore.Components;
using ModRouter.Components;
using ModRouter.Core.Guards;
using ModRouter.Crm.Crm.Routing;
using ModRouter.Crm.Data;

namespace ModRouter.Crm.Crm.Pages;

/// <summary>The edit page: the child route accounts/{id}/edit, guarded by UnsavedChangesGuard.</summary>
public partial class AccountEdit : IRouteNavigationLock
{
    [Inject]
    Ui.DialogService Dialogs { get; set; } = default!;

    /// <summary>
    /// The route's leave guard is not asked when only the id changes (accounts/5/edit to accounts/3/edit stays the same route),
    /// so the page asks itself in that case. A real leave is left to <see cref="UnsavedChangesGuard"/>, so the dialog shows once.
    /// </summary>
    public ValueTask<RouteLeaveResult> CanLeaveAsync(RouteLeaveContext context)
        => context.IsParameterChange ? new UnsavedChangesGuard(Draft, Dialogs).CanDeactivateAsync(context) : ValueTask.FromResult(RouteLeaveResult.Allow());

    /// <summary>The account page cascades what its loader returned, so every tab reads the same account.</summary>
    [CascadingParameter]
    AccountView View { get; set; } = default!;

    [Inject]
    EditDraft Draft { get; set; } = default!;

    bool Saved { get; set; }

    protected override void OnParametersSet() => Draft.Begin(View.Account.Id, View.Account.Name);

    void Save()
    {
        Draft.Save();
        Saved = true;
    }
}
