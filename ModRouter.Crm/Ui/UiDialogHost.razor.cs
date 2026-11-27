using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ModRouter.Crm.Ui;

/// <summary>Draws the question a <see cref="DialogService"/> asks. Place one in the layout.</summary>
public partial class UiDialogHost : IDisposable
{
    ElementReference _dialog;

    bool _openPending;

    [Inject]
    DialogService Dialogs { get; set; } = default!;

    [Inject]
    IJSRuntime Js { get; set; } = default!;

    ConfirmRequest? Current { get; set; }

    protected override void OnInitialized() => Dialogs.Requested += OnRequested;

    public void Dispose() => Dialogs.Requested -= OnRequested;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_openPending)
        {
            _openPending = false;
            await Js.InvokeVoidAsync("crmDialog.open", _dialog);
        }
    }

    void OnRequested(ConfirmRequest request) => _ = InvokeAsync(() =>
    {
        // A newer question replaces an older one that is still open: the older one is answered no.
        Current?.Completion.TrySetResult(false);
        Current = request;
        _openPending = true;
        StateHasChanged();
    });

    void Confirm() => Close(true);

    void Cancel() => Close(false);

    void Close(bool answer)
    {
        Current?.Completion.TrySetResult(answer);
        Current = null;
    }
}
