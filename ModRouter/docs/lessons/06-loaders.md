# Lesson 6: Loaders

Fetch a page's data before it shows, defer the slow part, and cache the answer. Back to the [guided lessons](../lessons.md).

**Live demo:** the [sample CRM](https://modrouter.github.io/blazor-modrouter/crm/), chapter 6, Loaders

## See it

1. Open **account 1**. The page appears with its data already there.
2. Slow the network down with the control in the guide, and open **account 4**. The old page stays on screen until the data arrives. The CRM never shows half a page.
3. Open the account's **Activity** tab. The tab showed first, and the feed fills in after: the activity was deferred.
4. Go back to the list and open the *same* account within ten seconds. The route log shows no loader time: the answer came from the cache.
5. Wait until the page says **stale** and reopen it. It shows at once, and a refresh runs in the background.
6. Open **account 99**. There is no such account, so the loader fails, and the page shows an error with **Retry**. Go offline and the same happens.

## Read it

A **loader** fetches the data a route needs before its page shows. The navigation waits for it, and the old page stays on screen meanwhile. Like a guard, it is a class created with the app's services:

```csharp
// ModRouter.Crm/Crm/Routing/RoutingClasses.cs
public sealed class AccountLoader(Network network) : IRouteLoader
var id = context.Parameters.Get<int>("id");
await network.WaitAsync(context.CancellationToken);
```

Pass `context.CancellationToken` to everything you await. If the visitor clicks somewhere else, a newer navigation cancels the token and the work stops.

### Attaching a loader, and caching its answer

```csharp
// ModRouter.Crm/Crm/CrmRoutes.cs
.Loader<AccountLoader>()
.CacheFor(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)))
```

`CacheFor(fresh, stale)` keeps what the loader returned. The age of the cached value decides what a visit does:

| Age of the cached value | What a visit does |
|---|---|
| Younger than `fresh` (10 s here) | Uses it. The loader does not run. |
| Up to `stale` more (30 s here) | Shows it at once, then runs the loader in the background and replaces the data when it returns. |
| Older, or not cached | Runs the loader, as before. |

The router keeps one value per route level and its values, so account 1 and account 3 are separate entries. The account page shows its own age and a badge that says what a visit would do right now.

### Reading what the loader returned

The page gets the data through the route's data, typed:

```csharp
// ModRouter.Crm/Crm/Pages/AccountPage.razor.cs
AccountView View => Data.GetRequired<AccountView>();
```

The loader is on the account route, so the tabs below it read the same data. The page cascades it, as [lesson 3](03-nested-routes.md) showed.

### Deferred values

A loader can return plain data and promise more. The activity feed is slow, and the tab should not wait for it, so the Activity tab has a loader of its own that defers it:

```csharp
// ModRouter.Crm/Crm/CrmRoutes.cs
.Route("activity", typeof(AccountActivityTab), name: "account-activity", title: "Activity").Loader<ActivityLoader>()
```

```csharp
// ModRouter.Crm/Crm/Routing/RoutingClasses.cs
return ValueTask.FromResult(RouteLoadResult.Create(id).Defer("activity", LoadActivityAsync(id, network, context.LifetimeToken)));
```

The tab shows at once. `LifetimeToken` is canceled when the level is discarded, so the deferred work stops when the visitor leaves.

That is also why the feed has its own route and loader. A result that carries deferred values (or streams) is never cached, so putting `Defer` on the account loader would switch its `CacheFor` off. Keep the plain data in one loader and the deferred part in another.

The tab reads the deferred value from its own route's data, and a component shows a fallback until it arrives:

```csharp
// ModRouter.Crm/Crm/Pages/AccountActivityTab.razor.cs
DeferredValue<IReadOnlyList<Activity>> Feed => Data.Deferred<IReadOnlyList<Activity>>("activity");
```

```razor
@* ModRouter.Crm/Crm/Pages/AccountActivityTab.razor *@
<ModDeferred T="IReadOnlyList<Activity>" Value="Feed">
```

A loader can also `Stream` values that keep arriving, and a link can start a loader early with `Preload`. See [Navigation](../navigation.md).

### When a loader fails

A loader that throws commits the navigation with an error at that level. The levels above stay on screen, and the failed level shows what the root declares:

```csharp
// ModRouter.Crm/Crm/Routing/RoutingClasses.cs
var account = CrmData.Account(id) ?? throw new KeyNotFoundException($"There is no account {id}. The URL matched a route, but the loader found nothing.");
```

```razor
@* ModRouter.Crm/App.razor *@
<Error Context="error">
```

`error.Retry` runs the loader again.

### Refreshing

```csharp
// ModRouter.Crm/Crm/Pages/AccountPage.razor.cs
async Task Refresh() => await Navigation.RevalidateAsync();
```

`RevalidateAsync` reruns the loaders of the page on screen, without a guard and without changing the address.

## Your turn

1. Shorten the cache in `CrmRoutes.cs` to `CacheFor(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10))`, and revisit an account after a few seconds.
2. Take out `.CacheFor(...)`. Click between an account's tabs: the loader does not rerun, because the account did not change. Now pass `RouteReloadPolicy.EveryNavigation` to `.Loader<AccountLoader>(...)` and watch the route log: it runs on every tab click. See [Navigation](../navigation.md) for the four policies.
3. Give the Contacts tab a loader of its own that defers the contacts, and decide what the tab should show while they load. Why does the account loader stay plain?

## What you learned

- A loader runs before the page shows, and the old page stays on screen while it runs.
- Critical data is awaited; a deferred value is not, and a result with one is never cached.
- `CacheFor` answers a visit instantly while data is fresh, and refreshes it quietly while it is stale.
- A failing loader shows error content at its level, with a retry.

Next: [Lesson 7, Leave guards](07-leave-guards.md).
