# Lesson 5: Data in flight

Load data before the page shows, stream the slow parts, and cache the result. Back to the [guided lessons](../lessons.md).

**Live demo:** [open lesson 5 in the lab](https://modrouter.github.io/blazor-modrouter/lab/lessons/loaders)

## See it

1. Open a planet. The old page stays while the data loads, then the page appears at once.
2. Open its **Live feed**. The page shows first. The moon count arrives later, and the telemetry keeps streaming in.
3. Go back to the list and open the *same* planet again. The **ledger** does not grow: the answer came from the cache.
4. Wait until the page says **stale**, then reopen it. It shows at once, and the ledger adds one run: a refresh in the background.
5. Turn on **Make the loader fail**, open a planet you have not seen, and use **Try again** when it breaks.
6. Hover a planet in the list for a moment before clicking. That link preloads the data, so the click is instant.

Use the sliders to make the loader slower and watch the strip: the **Loader** light takes longer, and the old page stays put meanwhile.

## Read it

A **loader** fetches the data a route needs before its page shows. The navigation waits for it, and the old page stays on screen meanwhile. This one returns plain data:

```csharp
// ModRouter.Lab/Lessons/L5/PlanetLoader.cs
public sealed class PlanetLoader(LoaderKnobs knobs, LoaderLedger ledger) : IRouteLoader
{
    public async ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context)
    {
        var id = context.Parameters.Get<int>("id");
        ledger.Record($"planet {id}" + (context.IsReload ? " (reload)" : string.Empty));

        // Pass the token to everything awaited: a newer navigation cancels it, and the work stops.
        await Task.Delay(knobs.CriticalMs, context.CancellationToken);
```

Pass `context.CancellationToken` to everything you await. If the visitor clicks somewhere else, a newer navigation cancels the token and the work stops.

### Attaching a loader, and caching its answer

```csharp
// ModRouter.Lab/LabRoutes.cs
.Loader<PlanetLoader>()
.CacheFor(PlanetCache.Fresh, PlanetCache.Stale));
```

`CacheFor(fresh, stale)` keeps what the loader returned. The age of the cached value decides what a visit does:

| Age of the cached value | What a visit does |
|---|---|
| Younger than `fresh` (8 s in the lab) | Uses it. The loader does not run. |
| Up to `stale` more (30 s in the lab) | Shows it at once, then runs the loader in the background and replaces the data when it returns. |
| Older, or not cached | Runs the loader, as before. |

The lab's ledger makes this visible: a visit answered from the cache adds nothing to it. The router keeps one value per route level and its values, so planet 1 and planet 2 are separate entries. Only plain critical data is cached.

### Deferred values and streams

A loader can return three kinds of data. The first is awaited. The other two are not, so they never delay the page:

```csharp
// ModRouter.Lab/Lessons/L5/PlanetLoader.cs
return RouteLoadResult.Create(new LiveFeed(id))
    .Defer("moons", LoadMoonsAsync(id, context.LifetimeToken))
    .Stream("telemetry", TelemetryAsync(id));
```

That is why the live feed is a separate child route with its own loader: a result that carries deferred values or streams is never cached. `LifetimeToken` is canceled when the level is discarded, so deferred work stops when the visitor leaves.

The page reads them with two components. Each shows a fallback until its data arrives:

```razor
@* ModRouter.Lab/Lessons/L5/PlanetLive.razor *@
<ModDeferred T="string[]" Name="moons">
    <Fallback><p class="pending">Counting moons...</p></Fallback>
    <ChildContent Context="moons">
        <p class="moons">@foreach (var moon in moons) { <span class="chip">@moon</span> }</p>
    </ChildContent>
    <Error Context="error"><p class="dim">@error.Message</p></Error>
</ModDeferred>
```

### When a loader fails

A loader that throws commits the navigation with an error at that level. The levels above stay on screen, and the failed level shows error content with a retry. That content is the router's `Error` fragment in `App.razor`, and `error.Retry` runs the loader again.

### Refreshing and warming

```csharp
// ModRouter.Lab/Lessons/L5/PlanetPage.razor.cs
async Task Revalidate() => await Navigation.RevalidateAsync();
```

`RevalidateAsync` reruns the loaders of the page on screen without a guard and without changing the address, so you can refresh a page after a change. A link can start a loader early:

```razor
@* ModRouter.Lab/Lessons/L5/PlanetList.razor *@
<RouteLink Route="planet" Parameters="@(new { id })" Preload="RoutePreload.Intent" Class="ship-card">
```

`Preload="Intent"` starts the loader when the pointer rests on the link, or it gets focus, so the click finds the data ready.

## Your turn

1. Shorten the cache in `LabRoutes.cs` to `CacheFor(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10))` and look at the ledger when you revisit a planet after a few seconds.
2. Take out the `.CacheFor(...)` line and pass a reload policy to the loader instead: `.Loader<PlanetLoader>(RouteReloadPolicy.EveryNavigation)`. Click between the **Summary** and **Live feed** tabs of one planet and watch the ledger: the planet loader now runs on every click, even though the planet did not change. Put the default back and it stops. See [Navigation](../navigation.md) for the four policies.
3. Make the loader fail for odd ids only, and decide where a retry button should live.

## What you learned

- A loader runs before the page shows, and the old page stays on screen while it runs.
- Critical data is awaited; a deferred value and a stream are not.
- `CacheFor` answers a visit instantly while data is fresh, and refreshes it quietly while it is stale.
- A failing loader shows error content at its level with a retry, and a link can preload.

Next: [Lesson 6, Are you sure?](06-are-you-sure.md).
