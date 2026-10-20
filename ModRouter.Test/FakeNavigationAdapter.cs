using ModRouter.Navigation;

namespace ModRouter.Test;

/// <summary>
/// A hand-rolled platform that behaves like Blazor's navigation for the router's purposes: <see cref="NavigateAsync"/>
/// runs the registered location-changing handlers, and if none prevents it, updates <see cref="Uri"/> and raises
/// <see cref="LocationChanged"/>.
/// </summary>
internal sealed class FakeNavigationAdapter : INavigationPlatformAdapter
{
    readonly List<Func<LocationChangingRequest, ValueTask<LocationChangingDecision>>> _handlers = [];
    readonly List<Task<bool>> _started = [];

    public FakeNavigationAdapter(string baseUri = "http://localhost/", string? uri = null)
    {
        BaseUri = baseUri;
        Uri = uri ?? baseUri;
        History = [Uri];
    }

    public event Action<LocationChangedNotification>? LocationChanged;

    public string Uri { get; private set; }

    public string BaseUri { get; }

    public string? HistoryEntryState { get; private set; }

    public int HandlerCount => _handlers.Count;

    /// <summary>Calls made through <see cref="NavigateTo"/> (the app-facing API); they are always recorded.</summary>
    public List<(string Uri, bool ReplaceHistory, bool ForceLoad, string? State)> NavigateCalls { get; } = [];

    /// <summary>
    /// When true, <see cref="NavigateTo"/> also performs the navigation like Blazor does (the handlers start right
    /// away and may finish later); tests await <see cref="SettledAsync"/>. A forced load only records the call.
    /// </summary>
    public bool ExecuteNavigateTo { get; set; }

    /// <summary>The history stack after the navigations performed so far: the initial URI, then pushes and replaces.</summary>
    public List<string> History { get; }

    /// <summary>When set, <see cref="NavigateTo"/> throws it after recording the call (a static server render ending the request with a redirect).</summary>
    public Exception? NavigateToFailure { get; set; }

    public void NavigateTo(string uri, bool replaceHistory = false, bool forceLoad = false, string? historyState = null)
    {
        NavigateCalls.Add((uri, replaceHistory, forceLoad, historyState));

        if (NavigateToFailure is { } failure)
        {
            throw failure;
        }

        if (ExecuteNavigateTo && !forceLoad)
        {
            _started.Add(NavigateAsync(uri, historyState: historyState, replace: replaceHistory));
        }
    }

    /// <summary>Completes when every navigation started by <see cref="NavigateTo"/> has finished (including ones they started).</summary>
    public async Task SettledAsync()
    {
        for (var seen = 0; seen < _started.Count; seen++)
        {
            await _started[seen];
        }
    }

    /// <summary>When set, <see cref="RegisterLocationChanging"/> throws it (a platform that cannot register the handler).</summary>
    public Exception? RegisterFailure { get; set; }

    public IDisposable RegisterLocationChanging(Func<LocationChangingRequest, ValueTask<LocationChangingDecision>> handler)
    {
        if (RegisterFailure is { } failure)
        {
            throw failure;
        }

        _handlers.Add(handler);

        return new Registration(() => _handlers.Remove(handler));
    }

    /// <summary>
    /// Simulates a navigation. Returns false when a handler prevented it. <paramref name="historyTraversal"/> mimics
    /// back/forward as observed in a real browser: the handler sees "not intercepted", the changed event "intercepted".
    /// </summary>
    public async Task<bool> NavigateAsync(string uri, bool intercepted = false, string? historyState = null, CancellationToken cancellationToken = default, bool historyTraversal = false, bool replace = false)
    {
        var target = new Uri(new Uri(BaseUri), uri).AbsoluteUri;

        foreach (var handler in _handlers.ToArray())
        {
            // Blazor hands the handler the target exactly as it was given (often relative to the base).
            var decision = await handler(new LocationChangingRequest(uri, historyState, intercepted, cancellationToken));

            if (decision == LocationChangingDecision.Prevent)
            {
                return false;
            }
        }

        if (replace)
        {
            History[^1] = target;
        }
        else if (!historyTraversal)
        {
            History.Add(target);
        }

        RaiseLocationChanged(target, intercepted || historyTraversal, historyState);

        return true;
    }

    /// <summary>Changes the URI and raises the changed event without running any handler (a navigation the router never saw).</summary>
    public void RaiseLocationChanged(string uri, bool intercepted = false, string? historyState = null)
    {
        var target = new Uri(new Uri(BaseUri), uri).AbsoluteUri;
        Uri = target;
        HistoryEntryState = historyState;
        LocationChanged?.Invoke(new LocationChangedNotification(target, intercepted, historyState));
    }

    sealed class Registration(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
