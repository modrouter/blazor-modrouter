using ModRouter.Core.Definitions;
using ModRouter.Navigation;

namespace ModRouter;

/// <summary>The fluent loader-state declaration (in the same namespace as <c>AddModRouter</c>, so no extra <c>using</c> is needed).</summary>
public static class RouteMapPersistenceExtensions
{
    /// <summary>
    /// Carries the critical data of the loader of the route declared last from the prerender to the interactive pass, so
    /// the loader does not run twice (the data is stored as JSON in the persistent component state). The loader must
    /// return a <typeparamref name="TData"/> and no deferred values or streams, which cannot travel: a level that has any
    /// loads again on the interactive pass. Needs a loader on the same route (RT0025).
    /// </summary>
    public static RouteMap PersistLoaderData<TData>(this RouteMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        return map.WithLast(new RoutePersistedDataMetadata(typeof(TData), new RouteDataPersistence<TData>()), nameof(PersistLoaderData));
    }
}
