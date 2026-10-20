namespace ModRouter.Navigation;

/// <summary>How a navigation requested through <c>NavigateToAsync</c> ended.</summary>
public enum NavigationStatus
{
    /// <summary>The page for the URL is shown, or the user was already there.</summary>
    Succeeded,

    /// <summary>The URL matched nothing: the URL changed and the not-found content is shown.</summary>
    NotFound,

    /// <summary>A guard denied a level: the URL changed and the denial is shown at that level.</summary>
    NotAuthorized,

    /// <summary>
    /// A guard or loader threw, or redirects looped: the URL changed and the error is shown
    /// (<see cref="NavigationResult.Error"/>); or a stage threw and nothing changed.
    /// </summary>
    Failed,

    /// <summary>A guard or stage canceled it, or the router was disposed: the old page and URL stay.</summary>
    Cancelled,

    /// <summary>A guard or redirect rule sent the user elsewhere (<see cref="NavigationResult.RedirectedTo"/>); that navigation goes on by itself.</summary>
    Redirected,

    /// <summary>A newer navigation or a reload replaced it before it committed.</summary>
    Superseded,
}

/// <summary>What became of a navigation requested through <c>NavigateToAsync</c>.</summary>
/// <param name="Status">How it ended.</param>
/// <param name="RedirectedTo">For <see cref="NavigationStatus.Redirected"/>: the absolute URL the navigation was sent to.</param>
/// <param name="Error">For a committed error state (<see cref="NavigationStatus.NotAuthorized"/>, <see cref="NavigationStatus.Failed"/>): the error shown.</param>
/// <param name="Exception">For a <see cref="NavigationStatus.Failed"/> navigation whose stage threw: the exception.</param>
public sealed record NavigationResult(NavigationStatus Status, string? RedirectedTo = null, RouteError? Error = null, Exception? Exception = null)
{
    /// <summary>True when the page for the URL is shown (<see cref="NavigationStatus.Succeeded"/>).</summary>
    public bool Succeeded => Status == NavigationStatus.Succeeded;
}

/// <summary>The awaitable behind one <c>NavigateToAsync</c>: completed once, by whichever end the navigation reaches first.</summary>
internal sealed class NavigationCompletion
{
    readonly TaskCompletionSource<NavigationResult> _source = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<NavigationResult> Task => _source.Task;

    public void Complete(NavigationResult result) => _source.TrySetResult(result);

    public void Cancel(CancellationToken token) => _source.TrySetCanceled(token);
}
