namespace ModRouter.Navigation;

/// <summary>What a location-changing handler decides.</summary>
public enum LocationChangingDecision
{
    /// <summary>Let the navigation complete.</summary>
    Allow,
    /// <summary>Cancel the navigation; the platform keeps the current location.</summary>
    Prevent,
}

/// <summary>A navigation that is about to happen, as reported by the platform.</summary>
public readonly record struct LocationChangingRequest(
    string TargetLocation,
    string? HistoryEntryState,
    bool IsNavigationIntercepted,
    CancellationToken CancellationToken);

/// <summary>A navigation that has happened: the platform's URI has already changed.</summary>
public readonly record struct LocationChangedNotification(string Location, bool IsNavigationIntercepted, string? HistoryEntryState);

/// <summary>
/// The only seam between the router and the hosting platform. <c>BlazorNavigationAdapter</c> implements it over
/// <c>NavigationManager</c>; other hosts (Server, Hybrid, tests) provide their own.
/// </summary>
public interface INavigationPlatformAdapter
{
    /// <summary>The current absolute URI.</summary>
    string Uri { get; }

    /// <summary>The application base URI, ending with '/'.</summary>
    string BaseUri { get; }

    /// <summary>
    /// The state string stored with the current history entry (the <c>historyState</c> given to
    /// <see cref="NavigateTo"/>), or null when the entry has none.
    /// </summary>
    string? HistoryEntryState { get; }

    /// <summary>
    /// Asks the platform to navigate to <paramref name="uri"/> (absolute, or relative to <see cref="BaseUri"/>).
    /// <paramref name="replaceHistory"/> replaces the current history entry instead of adding one,
    /// <paramref name="forceLoad"/> does a full page load, and <paramref name="historyState"/> is stored with the new
    /// entry. The platform runs the registered location-changing handlers first, which is where the router's pipeline
    /// runs; the call itself returns without waiting for them.
    /// </summary>
    void NavigateTo(string uri, bool replaceHistory = false, bool forceLoad = false, string? historyState = null);

    /// <summary>Registers a handler that runs before a navigation completes and may prevent it.</summary>
    IDisposable RegisterLocationChanging(Func<LocationChangingRequest, ValueTask<LocationChangingDecision>> handler);

    /// <summary>Raised after the URI has changed.</summary>
    event Action<LocationChangedNotification>? LocationChanged;
}
