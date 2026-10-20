using ModRouter.Core.Matching;

namespace ModRouter.Core.Loading;

/// <summary>When a level's loader runs again for a navigation that keeps the level.</summary>
public enum RouteReloadPolicy
{
    /// <summary>Reload when this level's inherited route parameters changed (the default).</summary>
    WhenParametersChange,

    /// <summary>Reload when the route parameters or the query string changed.</summary>
    WhenParametersOrQueryChange,

    /// <summary>Reload on every navigation that keeps the level.</summary>
    EveryNavigation,

    /// <summary>Load once; only a fresh activation of the level or an explicit reload loads again.</summary>
    Never,
}

/// <summary>
/// Loads the data of a route level. Loaders are attached with <c>RouteMap.Loader&lt;T&gt;()</c>, at most one per
/// level, and resolved from the navigation scope's DI container. The returned critical data is awaited before the
/// navigation commits; deferred values and streams are started after it.
/// </summary>
public interface IRouteLoader
{
    /// <summary>
    /// Loads the data of <paramref name="context"/>'s level. Called when the level is first activated and again as the
    /// level's <see cref="RouteReloadPolicy"/> requires; not called for a level whose previous data is reused, nor for
    /// levels at or below a guard error. Loaders of different levels start at once, and the navigation waits for each
    /// result. An exception that is not the navigation's own cancellation becomes a
    /// <see cref="Navigation.RouteErrorKind.LoadFailed"/> error at that level and cancels the loaders of deeper levels;
    /// the navigation still commits.
    /// </summary>
    ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context);
}

/// <summary>Everything a loader may need. Public so loaders can be unit tested.</summary>
public sealed class RouteLoadContext(
    RouteContext level,
    string query,
    bool isReload,
    IServiceProvider services,
    Func<Type, Task<object?>> parentData,
    CancellationToken cancellationToken,
    CancellationToken lifetimeToken)
{
    /// <summary>The matched route level this loader belongs to.</summary>
    public RouteContext Level { get; } = level;

    /// <summary>This level's inherited route parameters.</summary>
    public RouteParameters Parameters => Level.InheritedParameters;

    /// <summary>The raw query of the target including '?', or empty.</summary>
    public string Query { get; } = query;

    /// <summary>True for a reload of a level that was already showing (not a first activation).</summary>
    public bool IsReload { get; } = isReload;

    /// <summary>The navigation scope's services, the same container that created the loader.</summary>
    public IServiceProvider Services { get; } = services;

    /// <summary>Canceled when the navigation is superseded or canceled. Pass it to everything awaited here.</summary>
    public CancellationToken CancellationToken { get; } = cancellationToken;

    /// <summary>
    /// Canceled when the level is discarded after it was committed. Use it for deferred tasks and streams, which
    /// outlive <see cref="CancellationToken"/>'s navigation.
    /// </summary>
    public CancellationToken LifetimeToken { get; } = lifetimeToken;

    /// <summary>
    /// The critical data an ancestor level's loader returned, when it is of type <typeparamref name="T"/>. Waits for
    /// the ancestor's loader to finish; returns default when no ancestor loads that type.
    /// </summary>
    public async Task<T?> GetParentDataAsync<T>()
        => await parentData(typeof(T)).ConfigureAwait(false) is T data ? data : default;
}

/// <summary>A loader's result: the critical data plus optional deferred values and streams.</summary>
public sealed class RouteLoadResult
{
    readonly Dictionary<string, object> _deferred = new(StringComparer.Ordinal);
    readonly Dictionary<string, object> _streams = new(StringComparer.Ordinal);

    RouteLoadResult(object? data)
    {
        Data = data;
    }

    /// <summary>The critical data, available to the component as soon as it renders.</summary>
    public object? Data { get; }

    /// <summary>True when the result holds nothing but its critical data (no deferred values, no streams): it can be cached or persisted.</summary>
    internal bool IsPlain => _deferred.Count == 0 && _streams.Count == 0;

    internal IReadOnlyDictionary<string, object> Deferred => _deferred;

    internal IReadOnlyDictionary<string, object> Streams => _streams;

    /// <summary>
    /// Starts a result with <paramref name="data"/> as the critical data (null when the loader only defers values or
    /// streams). Chain <see cref="Defer{T}"/> and <see cref="Stream{T}"/> to add the rest.
    /// </summary>
    public static RouteLoadResult Create(object? data = null) => new(data);

    /// <summary>
    /// Adds a value that arrives later. The task starts now (the loader should already have started it) and its
    /// failure never fails the navigation: it surfaces through <see cref="DeferredValue{T}.Status"/>.
    /// </summary>
    public RouteLoadResult Defer<T>(string name, Task<T> task)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(task);

        _deferred[name] = new DeferredValue<T>(task);

        return this;
    }

    /// <summary>
    /// Adds a stream of items. Enumeration starts when the navigation commits (so a navigation that is only
    /// prepared never pulls items) and stops when the level is discarded.
    /// </summary>
    public RouteLoadResult Stream<T>(string name, IAsyncEnumerable<T> source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(source);

        var stream = new RouteStream<T>(source);
        _streams[name] = stream;

        return this;
    }
}
