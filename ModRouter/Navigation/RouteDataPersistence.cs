using ModRouter.Core.Definitions;
using ModRouter.Core.Loading;
using Microsoft.AspNetCore.Components;

namespace ModRouter.Navigation;

/// <summary>Reads and writes one level's loader data in the persistent component state, typed so no reflection is needed.</summary>
internal interface IRouteDataPersistence
{
    /// <summary>The type of the data it carries.</summary>
    Type DataType { get; }

    /// <summary>Stores <paramref name="data"/> (a <see cref="DataType"/>) under <paramref name="key"/>.</summary>
    void Persist(PersistentComponentState state, string key, object data);

    /// <summary>Takes the value stored under <paramref name="key"/>; false when there is none.</summary>
    bool TryRestore(PersistentComponentState state, string key, out object? data);
}

/// <summary>The JSON codec of loader data of type <typeparamref name="T"/>.</summary>
internal sealed class RouteDataPersistence<T> : IRouteDataPersistence
{
    public Type DataType => typeof(T);

    public void Persist(PersistentComponentState state, string key, object data) => state.PersistAsJson(key, (T)data);

    public bool TryRestore(PersistentComponentState state, string key, out object? data)
    {
        if (state.TryTakeFromJson<T>(key, out var value) && value is not null)
        {
            data = value;

            return true;
        }

        data = null;

        return false;
    }
}

/// <summary>
/// Carries critical loader data from the prerender to the interactive pass, so a loader that already ran on the server
/// does not run again in the browser or the circuit. Opt-in per route level, and only for a level whose loader returned
/// plain critical data: deferred values and streams are live tasks that cannot travel, so such a level loads again.
/// </summary>
internal static class LoaderStatePersistence
{
    /// <summary>The key of one level's data: the same on the server and on the interactive pass for the same URL.</summary>
    public static string Key(int depth, string path, string query) => $"ModRouter.Loader:{depth}:{path}{query}";

    /// <summary>The codec a level opted into, or null.</summary>
    public static IRouteDataPersistence? CodecOf(RouteDefinition route)
        => route.Metadata.Get<RoutePersistedDataMetadata>()?.Persistence as IRouteDataPersistence;

    /// <summary>The restored result of a level, or null when it did not opt in or nothing was persisted for it.</summary>
    public static RouteLoadResult? TryRestore(IServiceProvider services, RouteDefinition route, int depth, string path, string query)
    {
        if (CodecOf(route) is not { } codec || services.GetService(typeof(PersistentComponentState)) is not PersistentComponentState state)
        {
            return null;
        }

        return codec.TryRestore(state, Key(depth, path, query), out var data) ? RouteLoadResult.Create(data) : null;
    }

    /// <summary>
    /// Stores the data of every opted-in level of <paramref name="committed"/> that has plain critical data. Called by the
    /// router when the prerender finishes.
    /// </summary>
    public static void Persist(RouteState committed, PersistentComponentState state)
    {
        for (var depth = 0; depth < committed.LevelData.Count; depth++)
        {
            if (committed.LevelData[depth] is not { Data: { } data, HasOnlyCriticalData: true } level
                || CodecOf(level.Route) is not { } codec
                || !codec.DataType.IsInstanceOfType(data))
            {
                continue;
            }

            codec.Persist(state, Key(depth, committed.Path, committed.Query), data);
        }
    }
}
