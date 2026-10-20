using ModRouter.Core.Definitions;
using ModRouter.Core.Matching;

namespace ModRouter.Navigation;

/// <summary>How a lookup in the <see cref="LoaderCache"/> went.</summary>
internal enum LoaderCacheState
{
    /// <summary>Nothing usable: load.</summary>
    Miss,

    /// <summary>Younger than the route's fresh window: use it, load nothing.</summary>
    Fresh,

    /// <summary>Past the fresh window but inside the stale window: use it now and refresh it in the background.</summary>
    Stale,
}

/// <summary>
/// Remembers the critical data of loaders that opted in with <c>CacheFor</c>, per route level, its inherited route
/// parameters and the query. One per router scope (a circuit, or the browser), so one user never sees another's data. Only
/// plain critical data is stored: deferred values and streams are live and cannot be reused.
/// </summary>
internal sealed class LoaderCache(ModRouterOptions options, TimeProvider time)
{
    readonly Dictionary<Key, Entry> _entries = [];

    /// <summary>What identifies a cached level: the route, its inherited parameters and the target's query.</summary>
    internal readonly record struct Key(RouteDefinition Route, RouteParameters Parameters, string Query);

    sealed record Entry(object? Data, long Stamp);

    /// <summary>The number of entries held.</summary>
    internal int Count => _entries.Count;

    /// <summary>Looks <paramref name="key"/> up against the route's windows; an entry past both is dropped.</summary>
    public LoaderCacheState Find(Key key, RouteLoaderCacheMetadata windows, out object? data)
    {
        data = null;

        if (!_entries.TryGetValue(key, out var entry))
        {
            return LoaderCacheState.Miss;
        }

        var age = time.GetElapsedTime(entry.Stamp);

        if (age >= windows.FreshFor + windows.StaleFor)
        {
            _entries.Remove(key);

            return LoaderCacheState.Miss;
        }

        data = entry.Data;

        return age < windows.FreshFor ? LoaderCacheState.Fresh : LoaderCacheState.Stale;
    }

    /// <summary>Stores <paramref name="data"/> for <paramref name="key"/>, evicting the oldest entries over the cap.</summary>
    public void Set(Key key, object? data)
    {
        _entries.Remove(key);

        while (_entries.Count >= options.LoaderCache.MaxEntries && _entries.Count > 0)
        {
            var oldest = _entries.MinBy(pair => pair.Value.Stamp).Key;
            _entries.Remove(oldest);
        }

        _entries[key] = new Entry(data, time.GetTimestamp());
    }

    /// <summary>Forgets everything.</summary>
    public void Clear() => _entries.Clear();
}
