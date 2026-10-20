using ModRouter.Components;
using ModRouter.Core.Definitions;
using ModRouter.Core.Loading;
using ModRouter.Core.Matching;

namespace ModRouter.Navigation;

/// <summary>
/// What a loader produced for one route level: the critical data, the deferred values and the streams, plus the
/// lifetime that ends when the level is discarded. Reused across navigations that keep the level.
/// </summary>
public sealed class RouteLevelData
{
    readonly IReadOnlyDictionary<string, object> _deferred;
    readonly IReadOnlyDictionary<string, object> _streams;
    readonly CancellationTokenSource _lifetime;
    int _activated;

    internal RouteLevelData(
        RouteDefinition route,
        RouteParameters parameters,
        string query,
        RouteLoadResult result,
        CancellationTokenSource lifetime)
    {
        Route = route;
        Parameters = parameters;
        Query = query;
        Data = result.Data;
        _deferred = result.Deferred;
        _streams = result.Streams;
        _lifetime = lifetime;
        Accessor = new ModRouteData(this, null);
    }

    /// <summary>The route definition that loaded this data.</summary>
    public RouteDefinition Route { get; }

    /// <summary>The level's inherited parameters at load time.</summary>
    public RouteParameters Parameters { get; }

    /// <summary>The raw query at load time.</summary>
    public string Query { get; }

    /// <summary>The critical data.</summary>
    public object? Data { get; }

    internal ModRouteData Accessor { get; }

    /// <summary>True when the loader returned no deferred values and no streams, so the data can be persisted on its own.</summary>
    internal bool HasOnlyCriticalData => _deferred.Count == 0 && _streams.Count == 0;

    /// <summary>
    /// The deferred value the loader added under <paramref name="name"/> (names are compared exactly, ordinal).
    /// </summary>
    /// <exception cref="KeyNotFoundException">
    ///   The loader returned no deferred value with that name; the message lists the names it did return.
    /// </exception>
    /// <exception cref="InvalidCastException">
    ///   The value under that name is not a <see cref="DeferredValue{T}"/> of <typeparamref name="T"/>.
    /// </exception>
    public DeferredValue<T> Deferred<T>(string name)
        => Find<DeferredValue<T>>(_deferred, name, "deferred value");

    /// <summary>The stream the loader added under <paramref name="name"/> (names are compared exactly, ordinal).</summary>
    /// <exception cref="KeyNotFoundException">
    ///   The loader returned no stream with that name; the message lists the names it did return.
    /// </exception>
    /// <exception cref="InvalidCastException">
    ///   The stream under that name is not a <see cref="RouteStream{T}"/> of <typeparamref name="T"/>.
    /// </exception>
    public RouteStream<T> Stream<T>(string name)
        => Find<RouteStream<T>>(_streams, name, "stream");

    /// <summary>The deferred value named <paramref name="name"/> when it exists and is a <typeparamref name="T"/>, else null.</summary>
    internal DeferredValue<T>? TryDeferred<T>(string name)
        => _deferred.TryGetValue(name, out var value) ? value as DeferredValue<T> : null;

    /// <summary>The stream named <paramref name="name"/> when it exists and is a <typeparamref name="T"/>, else null.</summary>
    internal RouteStream<T>? TryStream<T>(string name)
        => _streams.TryGetValue(name, out var value) ? value as RouteStream<T> : null;

    /// <summary>Starts the streams. Called once, when the navigation that owns this data commits.</summary>
    internal void Activate()
    {
        if (Interlocked.Exchange(ref _activated, 1) == 1)
        {
            return;
        }

        foreach (var stream in _streams.Values)
        {
            ((IRouteStream)stream).Start(_lifetime.Token);
        }
    }

    /// <summary>Cancels the lifetime token: deferred work observing it stops and streams stop enumerating.</summary>
    internal void Discard() => _lifetime.Cancel();

    T Find<T>(IReadOnlyDictionary<string, object> source, string name, string what)
        where T : class
    {
        if (!source.TryGetValue(name, out var value))
        {
            var known = source.Count == 0 ? "(none)" : string.Join(", ", source.Keys.Order(StringComparer.Ordinal));

            throw new KeyNotFoundException($"The loader of route '{Route.Name ?? Route.Template}' returned no {what} named '{name}'. It returned: {known}.");
        }

        return value as T
            ?? throw new InvalidCastException($"The {what} '{name}' of route '{Route.Name ?? Route.Template}' is a {value.GetType().Name}, not a {typeof(T).Name}.");
    }
}
