using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using ModRouter.Core.Loading;

namespace ModRouter.Sample.Features;

public sealed record SlowCustomer(int Id, string Name);

/// <summary>
/// A slow page done the fast-feeling way: the critical data takes 1.2 s (the old page stays up and the navigation
/// indicator shows), then the page renders at once; the orders arrive 1.5 s later and a feed streams in every 300 ms.
/// </summary>
public sealed class SlowCustomerLoader : IRouteLoader
{
    public async ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context)
    {
        var id = context.Parameters.Get<int>("id");

        await Task.Delay(TimeSpan.FromMilliseconds(1200), context.CancellationToken);

        return RouteLoadResult.Create(new SlowCustomer(id, $"Customer {id}"))
            .Defer("orders", LoadOrdersAsync(id, context.LifetimeToken))
            .Stream("feed", FeedAsync(id));
    }

    static async Task<string[]> LoadOrdersAsync(int id, CancellationToken lifetime)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(1500), lifetime);

        return [$"order {id}-1", $"order {id}-2"];
    }

    static async IAsyncEnumerable<int> FeedAsync(int id, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 1; i <= 5; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);

            yield return id * 10 + i;
        }
    }
}

/// <summary>Fails the first time each id is loaded and succeeds afterwards, so the error view and its retry can be tried.</summary>
public sealed class FlakyLoader : IRouteLoader
{
    static readonly ConcurrentDictionary<int, int> Attempts = new();

    public async ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context)
    {
        var id = context.Parameters.Get<int>("id");
        var attempt = Attempts.AddOrUpdate(id, 1, (_, count) => count + 1);

        await Task.Delay(TimeSpan.FromMilliseconds(200), context.CancellationToken);

        return attempt == 1
            ? throw new InvalidOperationException($"The service is warming up (attempt {attempt}).")
            : RouteLoadResult.Create($"Flaky item {id} loaded on attempt {attempt}");
    }
}
