namespace ModRouter.Navigation;

/// <summary>
/// Per-navigation choices for <c>INavigationService.NavigateTo(uri, ModNavigationOptions)</c>. They travel with the
/// navigation to its committed <see cref="RouteState"/>, where the effects planner reads them. A redirect starts a
/// new navigation and does not carry them.
/// </summary>
public sealed class ModNavigationOptions
{
    /// <summary>False skips the focus move for this navigation.</summary>
    public bool Focus { get; init; } = true;

    /// <summary>False skips the scroll (top or fragment) for this navigation.</summary>
    public bool Scroll { get; init; } = true;

    /// <summary>False skips the screen-reader announcement for this navigation.</summary>
    public bool Announce { get; init; } = true;

    /// <summary>Replace the current history entry instead of adding one.</summary>
    public bool ReplaceHistory { get; init; }

    /// <summary>Do a full page load.</summary>
    public bool ForceLoad { get; init; }

    /// <summary>
    /// A string stored with the new history entry (<c>history.state</c> semantics): it survives Back and Forward and is read
    /// back as <see cref="RouteState.HistoryEntryState"/>. Serialize structured data yourself.
    /// </summary>
    public string? HistoryState { get; init; }

    internal NavigationCompletion? Completion { get; init; }

    /// <summary>A copy of these options that also settles <paramref name="completion"/> when the navigation ends.</summary>
    internal ModNavigationOptions WithCompletion(NavigationCompletion completion) => new()
    {
        Focus = Focus,
        Scroll = Scroll,
        Announce = Announce,
        ReplaceHistory = ReplaceHistory,
        ForceLoad = ForceLoad,
        HistoryState = HistoryState,
        Completion = completion,
    };
}
