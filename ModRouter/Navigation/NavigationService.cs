using System.Diagnostics.CodeAnalysis;
using ModRouter.Core.Compilation;
using ModRouter.Core.Generation;

namespace ModRouter.Navigation;

/// <summary>The app-facing navigation API: navigate by URL or by route name, and build route URLs.</summary>
public interface INavigationService
{
    /// <summary>The committed route state, or null before the first navigation commits.</summary>
    RouteState? Current { get; }

    /// <summary>
    /// Raised after each commit with the new state, one subscriber at a time, from within the router's state store. An
    /// exception that escapes a subscriber is caught and logged by the store, and the other subscribers still run.
    /// </summary>
    event Action<RouteState>? RouteStateChanged;

    /// <summary>True from the moment a navigation is requested until it is prepared, canceled or superseded.</summary>
    bool IsNavigating { get; }

    /// <summary>Raised when <see cref="IsNavigating"/> changes (on any thread).</summary>
    event Action? NavigatingChanged;

    /// <summary>The error the current state shows, or null.</summary>
    RouteError? CurrentError { get; }

    /// <summary>Reruns every loader of the current route (guards run again too) and commits the result without changing the URL.</summary>
    Task ReloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the loaders of the current route again (the data of a cached level included) without running any guard and
    /// without a URL change, and replaces the data on screen when they succeed: refresh a page after a mutation. The old
    /// content stays while they run, and stays if one fails (<see cref="NavigationStatus.Failed"/>). Does nothing, and
    /// returns <see cref="NavigationStatus.Cancelled"/>, while a navigation is in flight, no router is mounted or the
    /// current state is not a plain page.
    /// </summary>
    Task<NavigationResult> RevalidateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the loaders of <paramref name="uri"/> that declare <c>CacheFor</c> into the cache, so a later navigation there
    /// finds its data. Never runs a guard, commits nothing and does not disturb a navigation in flight; a level whose cached
    /// data is still fresh is left alone. Does nothing without a mounted router.
    /// </summary>
    Task PreloadAsync(string uri, CancellationToken cancellationToken = default);

    /// <summary>Forgets every cached loader result of this router scope.</summary>
    void ClearLoaderCache();

    /// <summary>The browser title of the current state (see <see cref="RouteState.Title"/>), or null.</summary>
    string? Title { get; }

    /// <summary>Navigates like <see cref="NavigateTo(string, bool, bool)"/> with per-navigation choices (skip focus, scroll or the announcement, a history state).</summary>
    void NavigateTo(string uri, ModNavigationOptions options);

    /// <summary>
    /// Navigates and tells how it ended: <see cref="NavigationStatus.Succeeded"/> (also when the user is already there),
    /// not found, not authorized or failed (those commit, so the URL changed), canceled by a guard (the old page and URL
    /// stay), redirected (see <see cref="NavigationResult.RedirectedTo"/>; that navigation goes on by itself) or
    /// superseded by a newer one. <see cref="ModNavigationOptions.ForceLoad"/> hands the navigation to the browser and
    /// returns <see cref="NavigationStatus.Succeeded"/> at once. A navigation another handler prevents (a
    /// <c>NavigationLock</c>) gives the router no signal and settles as superseded when the next navigation starts.
    /// </summary>
    /// <exception cref="InvalidOperationException">No router is mounted, so no navigation can run.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled while waiting.</exception>
    Task<NavigationResult> NavigateToAsync(string uri, ModNavigationOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Generates the URL of the named route (as <see cref="GetRouteUrl"/> does) and navigates to it like <see cref="NavigateToAsync"/>.</summary>
    /// <exception cref="RouteUrlGenerationException">The route is unknown or a value is invalid; no navigation starts.</exception>
    Task<NavigationResult> NavigateToRouteAsync(
        string routeName,
        object? values = null,
        IReadOnlyDictionary<string, object?>? query = null,
        string? fragment = null,
        ModNavigationOptions? options = null,
        bool useAmbientValues = false,
        CancellationToken cancellationToken = default);

    /// <summary>The application base URI, ending with '/' (for example <c>http://localhost/myapp/</c>).</summary>
    string BaseUri { get; }

    /// <summary>The platform's current absolute URI.</summary>
    string GetLocation();

    /// <summary>Raised with the new absolute URI after the platform's location changes.</summary>
    event EventHandler<string>? OnLocationChanged;

    /// <summary>
    /// Navigates to <paramref name="uri"/> (absolute, or relative to <see cref="BaseUri"/>) through the platform, so
    /// the full pipeline (guards, loaders, redirects) runs. <paramref name="replaceHistory"/> replaces the current
    /// history entry; <paramref name="forceLoad"/> does a full page load. Returns without waiting for the navigation to
    /// finish.
    /// </summary>
    void NavigateTo(string uri, bool replaceHistory = false, bool forceLoad = false);

    /// <summary>
    /// Generates the URL of the named route (see <see cref="GetRouteUrl"/> for the arguments) and navigates to it as
    /// <see cref="NavigateTo(string, bool, bool)"/> does. The URL is built before anything happens, so an unknown route
    /// or an invalid value throws and no navigation starts. <paramref name="useAmbientValues"/> fills parameters you
    /// leave out from the current leaf's inherited route parameters.
    /// </summary>
    /// <exception cref="RouteUrlGenerationException">The route is unknown or a value is invalid.</exception>
    void NavigateToRoute(
        string routeName,
        IReadOnlyDictionary<string, object?>? parameters = null,
        IReadOnlyDictionary<string, object?>? query = null,
        string? fragment = null,
        bool replaceHistory = false,
        bool forceLoad = false,
        bool useAmbientValues = false);

    /// <summary>As above, taking parameters from an anonymous object such as <c>new { id = 42 }</c>.</summary>
    void NavigateToRoute(
        string routeName,
        object? values,
        IReadOnlyDictionary<string, object?>? query = null,
        string? fragment = null,
        bool replaceHistory = false,
        bool forceLoad = false,
        bool useAmbientValues = false);

    /// <summary>The base-prefixed URL path for a named route (for example <c>/myapp/customers/42</c>), suitable for an href.</summary>
    string GetRouteUrl(
        string routeName,
        IReadOnlyDictionary<string, object?>? parameters = null,
        IReadOnlyDictionary<string, object?>? query = null,
        string? fragment = null,
        bool useAmbientValues = false);

    /// <summary>
    /// <see cref="GetRouteUrl"/> without throwing: the base-prefixed URL, or the reason it cannot be built (an unknown
    /// route, a missing parameter, a value its constraint rejects). <paramref name="values"/> is an anonymous object or a
    /// dictionary, as in <see cref="NavigateToRoute(string, object?, IReadOnlyDictionary{string, object?}?, string?, bool, bool, bool)"/>;
    /// text values are accepted where the constraint parses them (<c>"42"</c> for <c>{id:int}</c>).
    /// </summary>
    bool TryGetRouteUrl(
        string routeName,
        object? values,
        [NotNullWhen(true)] out string? url,
        [NotNullWhen(false)] out RouteUrlGenerationError? error,
        IReadOnlyDictionary<string, object?>? query = null,
        string? fragment = null,
        bool useAmbientValues = false);
}

/// <summary>Default <see cref="INavigationService"/>. Ambient values come from the current leaf's inherited parameters.</summary>
internal sealed class NavigationService : INavigationService, IDisposable
{
    readonly INavigationPlatformAdapter _adapter;
    readonly RouteTableHolder _holder;
    readonly RouteStateStore _store;

    readonly LoaderCache? _cache;

    public NavigationService(INavigationPlatformAdapter adapter, RouteTableHolder holder, RouteStateStore store, LoaderCache? cache = null)
    {
        _adapter = adapter;
        _holder = holder;
        _store = store;
        _cache = cache;
        _adapter.LocationChanged += OnAdapterLocationChanged;
    }

    // Each subscriber is added to the store itself, so the store's per-handler isolation applies to it.
    public event Action<RouteState>? RouteStateChanged
    {
        add => _store.Changed += value;
        remove => _store.Changed -= value;
    }

    public event EventHandler<string>? OnLocationChanged;

    public event Action? NavigatingChanged
    {
        add => _store.NavigatingChanged += value;
        remove => _store.NavigatingChanged -= value;
    }

    public RouteState? Current => _store.Current;

    public bool IsNavigating => _store.IsNavigating;

    public RouteError? CurrentError => _store.Current?.Error;

    public Task ReloadAsync(CancellationToken cancellationToken = default)
        => _store.ReloadAll?.Invoke(cancellationToken) ?? Task.CompletedTask;

    public Task<NavigationResult> RevalidateAsync(CancellationToken cancellationToken = default)
        => _store.Revalidate?.Invoke(cancellationToken) ?? Task.FromResult(new NavigationResult(NavigationStatus.Cancelled));

    public Task PreloadAsync(string uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);

        return _store.Preload?.Invoke(uri, cancellationToken) ?? Task.CompletedTask;
    }

    public void ClearLoaderCache() => _cache?.Clear();

    public string GetLocation() => _adapter.Uri;

    public string? Title => _store.Current?.Title;

    public string BaseUri => _adapter.BaseUri;

    public void NavigateTo(string uri, bool replaceHistory = false, bool forceLoad = false)
        => _adapter.NavigateTo(uri, replaceHistory, forceLoad);

    public void NavigateTo(string uri, ModNavigationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var absolute = BaseUriHelper.ToAbsolute(_adapter.BaseUri, uri);
        var fragment = absolute.IndexOf('#');
        _store.ExpectOptions(fragment < 0 ? absolute : absolute[..fragment], options);
        _adapter.NavigateTo(uri, options.ReplaceHistory, options.ForceLoad, options.HistoryState);
    }

    public async Task<NavigationResult> NavigateToAsync(string uri, ModNavigationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        cancellationToken.ThrowIfCancellationRequested();

        if (_store.ReloadAll is null)
        {
            throw new InvalidOperationException("No ModRouter is mounted, so there is no navigation to wait for.");
        }

        options ??= new ModNavigationOptions();

        // A full page load leaves the application: there is nothing to wait for.
        if (options.ForceLoad)
        {
            _adapter.NavigateTo(uri, options.ReplaceHistory, forceLoad: true, options.HistoryState);

            return new NavigationResult(NavigationStatus.Succeeded);
        }

        var completion = new NavigationCompletion();
        var absolute = BaseUriHelper.ToAbsolute(_adapter.BaseUri, uri);
        var fragment = absolute.IndexOf('#');

        _store.ExpectOptions(fragment < 0 ? absolute : absolute[..fragment], options.WithCompletion(completion));
        _adapter.NavigateTo(uri, options.ReplaceHistory, forceLoad: false, options.HistoryState);

        await using var registration = cancellationToken.Register(() => completion.Cancel(cancellationToken)).ConfigureAwait(false);

        return await completion.Task.ConfigureAwait(false);
    }

    public async Task<NavigationResult> NavigateToRouteAsync(
        string routeName,
        object? values = null,
        IReadOnlyDictionary<string, object?>? query = null,
        string? fragment = null,
        ModNavigationOptions? options = null,
        bool useAmbientValues = false,
        CancellationToken cancellationToken = default)
    {
        var url = _holder.GenerateUrl(routeName, RouteValues.From(values), query, fragment, AmbientValues(useAmbientValues));

        // NavigationManager resolves a relative URI against the base, so drop the leading slash.
        return await NavigateToAsync(url.TrimStart('/'), options, cancellationToken).ConfigureAwait(false);
    }

    public void NavigateToRoute(
        string routeName,
        IReadOnlyDictionary<string, object?>? parameters = null,
        IReadOnlyDictionary<string, object?>? query = null,
        string? fragment = null,
        bool replaceHistory = false,
        bool forceLoad = false,
        bool useAmbientValues = false)
    {
        var url = _holder.GenerateUrl(routeName, parameters, query, fragment, AmbientValues(useAmbientValues));

        // NavigationManager resolves a relative URI against the base, so drop the leading slash.
        _adapter.NavigateTo(url.TrimStart('/'), replaceHistory, forceLoad);
    }

    public void NavigateToRoute(
        string routeName,
        object? values,
        IReadOnlyDictionary<string, object?>? query = null,
        string? fragment = null,
        bool replaceHistory = false,
        bool forceLoad = false,
        bool useAmbientValues = false)
        => NavigateToRoute(routeName, RouteValues.From(values), query, fragment, replaceHistory, forceLoad, useAmbientValues);

    public string GetRouteUrl(
        string routeName,
        IReadOnlyDictionary<string, object?>? parameters = null,
        IReadOnlyDictionary<string, object?>? query = null,
        string? fragment = null,
        bool useAmbientValues = false)
        => WithBasePath(_holder.GenerateUrl(routeName, parameters, query, fragment, AmbientValues(useAmbientValues)));

    public bool TryGetRouteUrl(
        string routeName,
        object? values,
        [NotNullWhen(true)] out string? url,
        [NotNullWhen(false)] out RouteUrlGenerationError? error,
        IReadOnlyDictionary<string, object?>? query = null,
        string? fragment = null,
        bool useAmbientValues = false)
    {
        if (!_holder.TryGenerateUrl(routeName, RouteValues.From(values), query, fragment, AmbientValues(useAmbientValues), out var generated, out error))
        {
            url = null;

            return false;
        }

        url = WithBasePath(generated);

        return true;
    }

    /// <summary>The app-relative <paramref name="url"/> under the base path (<c>/customers/42</c> → <c>/myapp/customers/42</c>).</summary>
    string WithBasePath(string url)
    {
        var basePath = new Uri(_adapter.BaseUri).AbsolutePath;

        return (basePath.EndsWith('/') ? basePath : basePath + "/") + url.TrimStart('/');
    }

    public void Dispose()
    {
        _adapter.LocationChanged -= OnAdapterLocationChanged;
    }

    IReadOnlyDictionary<string, object?>? AmbientValues(bool use)
        => use ? _store.Current?.Chain?.Leaf.InheritedParameters.Values : null;

    void OnAdapterLocationChanged(LocationChangedNotification notification)
        => OnLocationChanged?.Invoke(this, notification.Location);
}
