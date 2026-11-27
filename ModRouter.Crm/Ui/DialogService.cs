namespace ModRouter.Crm.Ui;

/// <summary>The wording of a confirm dialog.</summary>
public sealed class ConfirmOptions
{
    /// <summary>The label of the button that says yes.</summary>
    public string ConfirmText { get; set; } = "OK";

    /// <summary>The label of the button that says no.</summary>
    public string CancelText { get; set; } = "Cancel";

    /// <summary>True to colour the yes button red, for something that cannot be undone.</summary>
    public bool Destructive { get; set; }
}

/// <summary>One question waiting for an answer.</summary>
public sealed class ConfirmRequest(string title, string message, ConfirmOptions options)
{
    /// <summary>The heading.</summary>
    public string Title { get; } = title;

    /// <summary>The body text.</summary>
    public string Message { get; } = message;

    /// <summary>The button wording.</summary>
    public ConfirmOptions Options { get; } = options;

    /// <summary>Completed with the answer.</summary>
    public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>Asks the person a yes or no question in a dialog. <see cref="UiDialogHost"/> shows it.</summary>
public sealed class DialogService
{
    /// <summary>Raised when a question is asked.</summary>
    public event Action<ConfirmRequest>? Requested;

    /// <summary>Shows the dialog and completes with true for yes and false for no, Escape or a newer question.</summary>
    public Task<bool> ConfirmAsync(string title, string message, ConfirmOptions? options = null)
    {
        var request = new ConfirmRequest(title, message, options ?? new ConfirmOptions());

        if (Requested is null)
        {
            // Nothing is listening, so nobody can answer: say no rather than hang a navigation.
            return Task.FromResult(false);
        }

        Requested.Invoke(request);

        return request.Completion.Task;
    }
}
