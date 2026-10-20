using ModRouter.Core.Loading;
using ModRouter.Navigation;

namespace ModRouter.Components;

/// <summary>
/// The loaded data of a route level, cascaded by <see cref="ModRouterOutlet"/> to the component it renders. The
/// instance stays the same while the level's data is reused. A level without a loader sees its nearest ancestor's
/// data as its own; <see cref="Empty"/> is cascaded when no level above has a loader either.
/// </summary>
public sealed class ModRouteData
{
    readonly RouteLevelData? _level;

    internal ModRouteData(RouteLevelData? level, ModRouteData? parent)
    {
        _level = level;
        Parent = parent;
    }

    /// <summary>The nearest ancestor level with data. Set by the coordinator each time a state holding this level commits.</summary>
    internal ModRouteData? Parent { get; set; }

    /// <summary>
    /// The shared instance cascaded when neither the level nor any level above it has a loader: <see cref="Get{T}"/>
    /// and <see cref="GetFromAncestor{T}"/> return default, while <see cref="GetRequired{T}"/>,
    /// <see cref="Deferred{T}"/> and <see cref="Stream{T}"/> throw.
    /// </summary>
    public static ModRouteData Empty { get; } = new(null, null);

    /// <summary>The critical data when it is a <typeparamref name="T"/>, else default.</summary>
    public T? Get<T>() => _level?.Data is T data ? data : default;

    /// <summary>The critical data as a <typeparamref name="T"/>; throws when this level has none of that type.</summary>
    public T GetRequired<T>()
        => _level?.Data is T data
            ? data
            : throw new InvalidOperationException($"The route data does not contain {typeof(T).Name}. Check that the route has a loader returning it (RouteMap.Loader<T>()).");

    /// <summary>The nearest ancestor level's critical data that is a <typeparamref name="T"/>, else default.</summary>
    public T? GetFromAncestor<T>()
    {
        for (var ancestor = Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor._level?.Data is T data)
            {
                return data;
            }
        }

        return default;
    }

    /// <summary>The deferred value the loader registered under <paramref name="name"/>.</summary>
    public DeferredValue<T> Deferred<T>(string name) => Level().Deferred<T>(name);

    /// <summary>The stream the loader registered under <paramref name="name"/>.</summary>
    public RouteStream<T> Stream<T>(string name) => Level().Stream<T>(name);

    /// <summary>
    /// For components that look slots up by name while a navigation is replacing them: a cascading value can reach a
    /// component that is about to be removed (its route changed) before it is removed, and that component must not
    /// throw. A slot that is not there renders the component's fallback.
    /// </summary>
    internal DeferredValue<T>? FindDeferred<T>(string name) => _level?.TryDeferred<T>(name);

    /// <inheritdoc cref="FindDeferred{T}"/>
    internal RouteStream<T>? FindStream<T>(string name) => _level?.TryStream<T>(name);

    RouteLevelData Level()
        => _level ?? throw new InvalidOperationException("This route level has no loader, so it has no deferred values or streams.");
}
