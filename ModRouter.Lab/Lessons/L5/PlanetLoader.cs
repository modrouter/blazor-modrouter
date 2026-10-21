using System.Runtime.CompilerServices;
using ModRouter.Core.Loading;
using ModRouter.Lab.Services;

namespace ModRouter.Lab.Lessons.L5;

public sealed record PlanetReading(int Id, string Name, string Surface, DateTime LoadedAt);

public sealed record LiveFeed(int PlanetId);

/// <summary>How long a loaded planet stays fresh, and how long after that it may still be shown while a refresh runs.</summary>
public static class PlanetCache
{
    public static readonly TimeSpan Fresh = TimeSpan.FromSeconds(8);

    public static readonly TimeSpan Stale = TimeSpan.FromSeconds(30);
}

/// <summary>
/// A loader gets the data for a route before its page appears: the navigation waits for it, and the old page stays on screen
/// meanwhile. This one returns plain data, which is what <c>CacheFor</c> can keep.
/// </summary>
public sealed class PlanetLoader(LoaderKnobs knobs, LoaderLedger ledger) : IRouteLoader
{
    static readonly string[] Names = ["Aster", "Boreal", "Cinder", "Drift", "Ember", "Fathom"];
    static readonly string[] Surfaces = ["glass dunes", "methane seas", "basalt plains", "ice ridges", "salt flats", "cloud forests"];

    public async ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context)
    {
        var id = context.Parameters.Get<int>("id");
        ledger.Record($"planet {id}" + (context.IsReload ? " (reload)" : string.Empty));

        // Pass the token to everything awaited: a newer navigation cancels it, and the work stops.
        await Task.Delay(knobs.CriticalMs, context.CancellationToken);

        if (knobs.Fail)
        {
            throw new InvalidOperationException($"Planet {id}: the survey archive timed out.");
        }

        var index = Math.Abs(id) % Names.Length;

        return RouteLoadResult.Create(new PlanetReading(id, Names[index], Surfaces[index], DateTime.Now));
    }

    public static string NameOf(int id) => Names[Math.Abs(id) % Names.Length];
}

/// <summary>
/// The loader of the live feed, a child route of the planet. Its result has three kinds of data: a critical value (awaited),
/// a deferred value (the page shows now and the value fills in later) and a stream (items keep arriving).
/// A result that carries deferred values or streams is never cached.
/// </summary>
public sealed class LiveFeedLoader(LoaderKnobs knobs, LoaderLedger ledger) : IRouteLoader
{
    public async ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context)
    {
        var id = context.Parameters.Get<int>("id");
        ledger.Record($"live feed {id}");

        await Task.Delay(250, context.CancellationToken);

        return RouteLoadResult.Create(new LiveFeed(id))
            .Defer("moons", LoadMoonsAsync(id, context.LifetimeToken))
            .Stream("telemetry", TelemetryAsync(id));
    }

    async Task<string[]> LoadMoonsAsync(int id, CancellationToken lifetime)
    {
        // LifetimeToken is canceled when the level is discarded, so deferred work stops when the user leaves.
        await Task.Delay(knobs.MoonsMs, lifetime);

        return Enumerable.Range(1, 1 + id % 3).Select(n => $"{PlanetLoader.NameOf(id)}-{n}").ToArray();
    }

    static async IAsyncEnumerable<int> TelemetryAsync(int id, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var random = new Random(id);

        for (var i = 0; i < 12; i++)
        {
            await Task.Delay(350, cancellationToken);

            yield return 20 + random.Next(0, 60);
        }
    }
}
