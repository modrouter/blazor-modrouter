namespace ModRouter.Lab.Services;

/// <summary>
/// A tiny "are you sure?" dialog service. A leave guard awaits <see cref="AskAsync"/>; the <c>ConfirmHost</c> component in the
/// layout shows the question and answers it. If a newer navigation cancels the guard, the question closes by itself.
/// </summary>
public sealed class ConfirmDialog
{
    TaskCompletionSource<bool>? _pending;

    public string? Question { get; private set; }

    public bool IsOpen => _pending is not null;

    public event Action? Changed;

    public async ValueTask<bool> AskAsync(string question, CancellationToken cancellationToken)
    {
        _pending?.TrySetResult(false);

        var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = source;
        Question = question;
        Changed?.Invoke();

        await using var registration = cancellationToken.Register(() => source.TrySetResult(false));
        var answer = await source.Task;

        if (ReferenceEquals(_pending, source))
        {
            _pending = null;
            Question = null;
            Changed?.Invoke();
        }

        return answer;
    }

    public void Answer(bool answer) => _pending?.TrySetResult(answer);
}
