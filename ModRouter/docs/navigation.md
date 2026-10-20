# Navigation

Guards, loaders, redirects and error states, route links, breadcrumbs, the document title, focus and scroll. Back to the [README](../README.md).

## Guards, loaders, redirects and error states

The old page stays on screen, and the URL stays put, until every guard has allowed the navigation and every loader's
critical data has arrived. Only then does the new page commit. Fast feel comes from streaming: critical data first, the
rest filling in afterwards.

```csharp
builder.Services.AddModRouter(routes => routes
    .Route("/login", typeof(LoginPage), name: "login")
    .Route("/account", typeof(AccountPage), name: "account", children: account => account
        .Index(typeof(AccountHome), name: "account-home")
        .Route("admin", typeof(AdminPage), name: "account-admin").Guard<AdminOnlyGuard>())
    .Guard<SignedInGuard>()                                  // attaches to the route declared just before it
    .Route("/customers/{id:int}", typeof(CustomerPage), name: "customer").Loader<CustomerLoader>()
    .Redirect("/clients/{id:int}", "customer"));             // a URL that only forwards to a named route
```

**Guards** implement `IRouteGuard` and are created from the navigation scope's services, so they can take dependencies
(an `AuthenticationStateProvider`, your own session service). They run parent-first, each level's guards in declaration
order, one at a time, and must pass `context.CancellationToken` to anything they await.

| Result | Effect |
|---|---|
| `RouteGuardResult.Allow()` | continue |
| `Cancel()` | the old page and URL stay (link click, `NavigateTo`, back and forward alike) |
| `Redirect(url)` / `RedirectToRoute(name, parameters, query, fragment)` | navigate somewhere else instead (an absolute URL outside the app is a full page load) |
| `NotAuthorized(message)` | the navigation commits; levels above render, the denied level shows its not-authorized content |

A guard that throws is a bug in the app: it commits a `GuardFailed` error at that level and is logged.

**Leave guards** implement `IRouteLeaveGuard` and ask before a level that is on screen is left: the "unsaved changes" check.
They are declared like guards (`.LeaveGuard<T>()`, `[RouteLeaveGuard<T>]`, `<ModRouteEntry LeaveGuard="typeof(T)">`) and run
before the redirect rules and every enter guard, deepest level first, each level's guards in declaration order. The page's own
state reaches the guard through DI: a scoped service the page fills in and the guard reads.

```csharp
public sealed class UnsavedChangesGuard(FormState form, IConfirmDialog dialog) : IRouteLeaveGuard
{
    public async ValueTask<RouteLeaveResult> CanDeactivateAsync(RouteLeaveContext context)
        => !form.IsDirty || await dialog.ConfirmAsync("Discard your changes?", context.CancellationToken)
            ? RouteLeaveResult.Allow()
            : RouteLeaveResult.Cancel();      // or RouteLeaveResult.Redirect("drafts")
}

routes.Route("/customers/{id:int}/edit", typeof(EditCustomer), name: "customer-edit").LeaveGuard<UnsavedChangesGuard>();
```

A level is *left* when the target does not show the same route at that depth: another page, a sibling, a parent, or a URL that
matches nothing. It is not left by a parameter change on the same route (`/customers/1` to `/customers/2`: the page is updated in
place, so a page that keeps editable state per record has to look after that itself) or by opening a child. While a guard
waits for the user the old page and URL stay, `IsNavigating` is true, and a newer navigation cancels
`context.CancellationToken`. Cancel keeps the old page and URL for a link, `NavigateTo`, and Back and Forward alike. A guard
that throws fails the navigation (`NavigationStatus.Failed` with the exception) and the page stays. A leave guard does not run for
the first load, a reload, the later hops of a redirect chain (the first hop asked), a level that showed an error instead of its
page, or a navigation out of the application: the router cannot ask there, so use a `NavigationLock` with
`ConfirmExternalNavigation` for the browser's own prompt.

**A component that guards itself.** A route component that implements `IRouteNavigationLock` is asked on the instance on screen,
so the page answers from its own state without a service in between:

```csharp
public partial class EditCustomer : ComponentBase, IRouteNavigationLock
{
    public async ValueTask<RouteLeaveResult> CanLeaveAsync(RouteLeaveContext context)
        => !_form.IsModified || await _dialog.ConfirmAsync("Discard your changes?", context.CancellationToken)
            ? RouteLeaveResult.Allow()
            : RouteLeaveResult.Cancel();
}
```

It is asked in the same cases as a leave guard, *and* when the same route stays but its route parameters change
(`context.IsParameterChange`: `/customers/1/edit` to `/customers/2/edit` updates the page in place, which would otherwise
carry the first record's edits over). A query-only navigation asks nobody. The component is asked first, then the route's declared
leave guards (those only when the level is really left), deepest level first; the same results, redirects and exception rules apply.
Only the component of a route level counts (a layout or a child component of the page is not asked), and a page that showed an error
instead of its content is not asked.

**Loaders** implement `IRouteLoader`, at most one per route level. `RouteLoadResult.Create(data)` is the critical data
(the navigation waits for it); `.Defer("orders", task)` and `.Stream("feed", asyncEnumerable)` are not awaited. A level
whose loader is slow never delays a sibling: all loaders of a navigation start together, and `GetParentDataAsync<T>()`
waits for the nearest ancestor whose data is a `T`. Which navigations reload a kept level is the reload policy:

| `RouteReloadPolicy` | The level loads again when |
|---|---|
| `WhenParametersChange` (default) | its inherited route parameters changed |
| `WhenParametersOrQueryChange` | the parameters or the query string changed |
| `EveryNavigation` | any navigation keeps the level |
| `Never` | never (only a retry or `INavigationService.ReloadAsync()`) |

Streams start when the navigation **commits**, never while it is only prepared, and stop when the level is discarded
(`context.LifetimeToken` is canceled then; deferred work should observe it). Deferred and stream failures stay in
their own slot and never fail the navigation.

In a route component, read the data from the cascaded `ModRouteData` (named `Mod...` because `RouteData` already means
the built-in router's type): `Data.GetRequired<Customer>()`, `Data.Get<T>()`, `Data.GetFromAncestor<T>()`,
`Data.Deferred<T>("name")`, `Data.Stream<T>("name")`. A level without a loader sees its nearest ancestor's data.

```razor
<ModDeferred T="Order[]" Name="orders">
    <Fallback>loading orders...</Fallback>
    <ChildContent Context="orders">@orders.Length orders</ChildContent>
    <Error Context="error">@error.Message</Error>
</ModDeferred>

<ModStream T="Notification" Name="feed">
    <Fallback>waiting...</Fallback>
    <ItemTemplate Context="item"><li>@item.Text</li></ItemTemplate>
    <InProgress>more coming...</InProgress>
</ModStream>
```

A `Name` the current route does not provide renders the fallback rather than throwing: while a navigation replaces a
page, Blazor can hand the old page the next route's data just before removing it.

**Redirects.** `routes.Redirect("/clients/{id:int}", "customer")` forwards the same-named parameters and keeps the query
and fragment (`preserveQuery:` / `preserveFragment:` turn that off). An alias opts into redirecting to the canonical URL
with `.CanonicalAlias()`, or for every alias with `ModRouterOptions.AliasBehavior = AliasBehavior.RedirectToCanonical`;
the alias must declare exactly the canonical template's parameter names (RT0015). A redirect **pushes** a history entry
(the page the user came from stays in history, the redirecting URL never enters it); only a chain that starts at the
initial load **replaces**, and a guard can override either way (`replaceHistory:`). A redirect to where the user
already is (the page on screen, with the same query and the fragment the address bar shows) goes nowhere: the
redirecting navigation is prevented and the history entry and its state are left alone, so Back still leaves the page.
The same page with a different query or fragment is navigated as usual. More than 10 redirects in a row, or a repeated
URL, commits a `RedirectLoop` error that lists the URLs (checked first, so a first load or reload redirected to its own
URL is a loop).

**Error states.** A not-authorized guard, a failing guard or a failing loader commits the navigation with a
`RouteState.Error`: the URL changes, the levels above the failed one keep rendering (and are never recreated), and the
failed level's outlet shows error content. Content comes from, in order: the outlet's own `NotAuthorized` / `Error`
fragment, the router's, then a plain `role="alert"` message. Whichever it is, the error content is wrapped in
`<div data-mod-route-error-host style="display:contents">` (the focus fallback below looks for an alert only inside it).
`display: contents` keeps the wrapper out of the layout, but an application's CSS child combinator on error content
(`.panel > p`) now sees the wrapper as the child and may need a descendant combinator or a rule for the host.
`RouteError.Retry()` runs the pipeline for the current URL again and reloads the failed level while reusing the others (no
new history entry). Redirect loops and a failed or
canceled first navigation are router-level errors (`RouteFailure.RedirectLoop` / `NavigationFailed`) shown by
`ModRouter.Error` inside the layout.

```razor
<ModRouter DefaultLayout="typeof(MainLayout)">
    <Loading><Spinner /></Loading>
    <NotFound><p>Not found.</p></NotFound>
    <NotAuthorized Context="error"><LoginRequired Reason="@error.Message" /></NotAuthorized>
    <Error Context="error"><ErrorView Error="@error" OnRetry="error.Retry" /></Error>
</ModRouter>
```

`ModNavigationIndicator` renders its content while a navigation runs, but only after `Delay` (150 ms by default, measured
with `TimeProvider`), so fast navigations never flash it: put it in the layout. `INavigationService` adds
`IsNavigating`, `NavigatingChanged`, `CurrentError` and `ReloadAsync()`.

## Caching loader data, revalidating and preloading

A loader runs on every navigation that activates its level. `CacheFor` keeps what a loader returned, so a link back to a page is
instant and a page can be warmed before the click:

```csharp
routes.Route("/customers/{id:int}", typeof(CustomerPage), name: "customer")
      .Loader<CustomerLoader>()
      .CacheFor(freshFor: TimeSpan.FromMinutes(1), staleFor: TimeSpan.FromMinutes(5));
```

(or `[RouteLoaderCache(60, 300)]` on the page, or `CacheFreshFor` and `CacheStaleFor` on a `<ModRouteEntry>`). The route needs a loader (RT0026).

| Age of the cached value | What a navigation does |
|---|---|
| younger than `freshFor` | uses it; the loader does not run |
| up to `staleFor` more | uses it at once, then runs the loader in the background and replaces the data on screen when it returns (the page re-renders with the new `ModRouteData`) |
| older, or not cached | runs the loader, as before |

- **What is cached.** One value per route level, its inherited route parameters and the query (`/customers/1`, `/customers/2` and
  `/customers/1?tab=a` are three entries), for the life of the router's scope: a circuit, or the browser. One user never sees another's.
  `LoaderCache.MaxEntries` (default 50) caps the scope; the oldest entry goes first. Only plain critical data is cached: a result that also
  returns deferred values or streams is not, and the data object is shared, so treat it as read-only.
- **The background refresh** never disturbs a navigation (it does nothing while one is in flight), shows no navigation indicator,
  runs no guard, and leaves the stale page alone if a loader throws. A reload (`ReloadAsync`, a retry) always asks the loader.
- **`INavigationService.RevalidateAsync()`** reruns the loaders of the page on screen without any guard and without a URL change,
  cache or not, and replaces the data when they succeed: refresh a page after a mutation. The old content stays while they run and
  stays if one fails (`NavigationStatus.Failed`). It does nothing (`Cancelled`) while a navigation is in flight or when the state is
  not a plain page. `ClearLoaderCache()` drops every cached value, for data of *other* pages.
- **`INavigationService.PreloadAsync(uri)`** runs the cacheable loaders of a URL into the cache: no guard, no commit, and a navigation
  in flight is untouched. A level whose value is still fresh is left alone, a stale one is refreshed, and a level without `CacheFor`
  loads nothing. A target that matches nothing, or is outside the application, does nothing.
- **`<RouteLink Preload="Intent">`** preloads after `PreloadDelay` (50 ms) of hover, focus or touch and cancels when the pointer or
  focus leaves first; `Preload="Render"` preloads once when the link first renders. The handlers are attached only for `Intent`
  (a handler on every link would cost a round trip per hover on a server circuit).

## Keeping pages alive

A route that declares `KeepAlive` keeps its page when the user leaves it, so a half-filled form, a scrolled list inside the page or a running
timer is still there on return:

```csharp
routes.Route("/reports/{id:int}", typeof(ReportPage), name: "report").KeepAlive(max: 3);
```

(or `[RouteKeepAlive(3)]` on a discovered page, or `KeepAlive="3"` on a `<ModRouteEntry>`). The component stays rendered in a wrapper that is `display: none`
and `inert` while another page is current, and is shown again, with the same instance and state, when the same route returns.

- **What counts as the same page.** The route plus its route values: `/reports/1` and `/reports/2` are two kept pages (each its own instance), up to
  `max` hidden ones per route in one outlet (default 3), the least recently shown dropped first; a dropped page's component is disposed and starts
  over when it returns. `KeepAlive` replaces the level's `RouteComponentReuse` policy: a kept page is never reused for other values.
- **Where the cache lives.** In the `ModRouterOutlet` that renders the page, for as long as that outlet lives. Leaving the section whose layout
  holds the outlet drops its kept pages; put `KeepAlive` on routes whose outlet stays (the router's own layout, or a parent route that is common
  to the pages you move between). A kept page that has child routes keeps its nested outlet's content too; coming back to a different child
  changes only the child.
- **Work in a hidden page.** It keeps running: a timer or subscription continues unless the page implements `IKeepAliveRoute`, whose
  `OnDeactivatedAsync` (hidden) and `OnActivatedAsync` (shown again, never on the first show) let it pause and resume. An exception from either is an
  unhandled exception of the outlet, like one from a lifecycle method. A hidden page re-renders only for its own events and for `[SupplyParameterFromQuery]`
  parameters, which Blazor updates on every URL change.
- **Loaded data.** Returning to a kept page is a navigation like any other: its loader runs (or finds the data in the
  [cache](#caching-loader-data-revalidating-and-preloading)) and the page receives the new `ModRouteData` through the cascade while it keeps the
  rest of its state. Deferred values and streams stop when the user leaves, so a hidden page shows what it had received.
- **Focus, announcements and the leave guard** look only at the shown page: the focus selector skips hidden pages, and `IRouteNavigationLock` is asked of
  the shown one.
- RT0029 reports `KeepAlive` twice on a route, a `Max` below 1, or on a group or redirect. Checked in a browser: a counter page kept its value
  across a visit to two other pages, was hidden and inert meanwhile, and focus went to the visible heading each time.

## Route links, breadcrumbs, title, focus and scroll

The router package ships `RouteLink` (an anchor) and `RouteBreadcrumb` (a `nav` with an ordered list) as plain markup with no
styling; `RouteLink` takes any other attribute (a `title`, a `data-testid`) straight onto the anchor.

```razor
<RouteLink Route="customer" Parameters="@(new { id = 42 })" Fragment="orders">Customer 42</RouteLink>
<RouteLink Href="customers" Match="RouteLinkMatch.Exact">All customers</RouteLink>

<RouteBreadcrumb Home="Home" MaxItems="4" />
```

- **`RouteLink`** takes a `Route` name with `Parameters` (an anonymous object or a dictionary), `Query`, `Fragment` and
  `UseAmbientValues`, or a plain `Href`; the generated href includes the base path. When it points at the current page it
  renders `aria-current="page"` and the class `mod-route-link-active` (a link to the parent of an index route counts: the index
  renders at the parent's URL); a link to an ancestor (`Match="Prefix"`, the default for a route name) renders
  `aria-current="true"`. An `Href` link is `Exact` by default and ignores query and a trailing slash. It
  follows navigation by itself. `Disabled`, `Target`, `Class`, `Title` and other attributes pass through to
  the anchor. An unknown route name throws while rendering, like `GetRouteUrl`.
- **`RouteBreadcrumb`** builds the trail from the committed chain: a level contributes its route title, or its
  `.Breadcrumb(...)` label; index routes and untitled or hidden levels contribute nothing; every crumb but the last is a link
  (base-prefixed matched path) and the last is the current page. `Home` adds a leading crumb (not when a titled root route
  already starts the trail), `MaxItems` (at least 3) keeps the
  first and the last crumbs and replaces the middle with an ellipsis. It renders nothing when no route matched.

Route metadata, attached to the route declared just before (each at most once per route, RT0018):

```csharp
.Route("{id:int}", typeof(CustomerPage), p => $"Customer {p["id"]}", name: "customer")
    .Breadcrumb("Customers")             // fixed label, or .Breadcrumb(p => ...) from the level's parameters, or .Breadcrumb(hidden: true)
    .Focus("#customer-heading")          // the element to focus after navigating here
    .NoAnnounce()                        // no screen-reader announcement for this route
```

**After a navigation commits and renders**, `ModRouter` runs the effects the trigger calls for, once per committed state
(a navigation that was superseded before it rendered runs none):

| Trigger | Title | Announce | Focus | Scroll |
|---|---|---|---|---|
| first load | yes | no | no | to the fragment |
| link, `NavigateTo`, redirect | yes | yes | yes | to the fragment, else the top |
| back / forward | yes | no | no | restore the saved position |
| retry / reload | no | no | no | no |

A redirect chain that started at the first load or at back/forward takes that row instead (the user did not navigate: the
target is simply the page that load or traversal ends on); `RouteState.RedirectOrigin` carries the trigger that started the
chain. A redirect from a link, `NavigateTo` or a retry is a new page.

- **Title.** The leaf's route title through `Title.Format` (`"{0} - Acme"`); an untitled index route uses its parent's
  title; another untitled route uses `Title.Default` verbatim, or leaves the document title alone when that is null; no match
  uses `Title.NotFound`. `RouteState.Title` and `INavigationService.Title` carry the result.
- **Announcement.** A visually hidden polite `role="status"` region owned by `ModRouter` (or `AnnounceTemplate`) receives the
  title; the text alternates by a trailing no-break space so the same title twice in a row is announced again.
- **Focus.** After a new navigation focus moves to the first element matching `Focus.Selector` (default `h1`; use
  `"main h1"` when the layout has its own heading), made focusable with `tabindex="-1"`, else to a `role="alert"` element inside the router's own error content (the
  error host above): an alert that belongs to a layout or a page, such as a banner, a message of the page, the error alert of `ModDeferred` or `ModStream`, or a `role="alert"` in the `NotFound` content (not an error: it renders outside the host, so give it a heading), is never chosen. It waits up to `Focus.Timeout` (1 s) for a heading that renders after an `await`, and gives up when a newer page
  commits or the user presses a mouse button, touches the screen, turns the mouse wheel or presses a key (dragging the
  window's scrollbar does not count). It never takes focus from a text field, select or other editable element. A
  `Focus.Selector` the browser rejects as invalid CSS is ignored (the alert fallback still applies) and the browser console
  warns about it once per selector.
- **Scroll.** A new navigation scrolls the container (the window by default; `Scroll.Container` is a CSS selector) to the
  top or to the `#fragment` target once it exists (a fragment that is not valid percent-encoding, such as `#%E0%A4`, is
  matched as written, as the browser does); back and forward restore the position that history entry had. Positions are
  kept in memory by the entry's `history.state._index`, saved by a throttled scroll listener. The router sets
  `history.scrollRestoration = "manual"`: left on `auto` the browser restores its own position too, at a moment of its
  choosing, and the two fight. Late-arriving content (deferred values, streams) can leave a restored position unreachable;
  the router retries as the page grows until the position is reachable, the user presses a mouse button, touches the screen,
  turns the mouse wheel or presses a key (as for focus), or `Focus.Timeout` passes. A reload is not restored.
- **View transitions.** With `o.ViewTransitions.Enabled = true` (off by default) a navigation that changes the page runs inside the browser's
  `document.startViewTransition`: the browser keeps a picture of the old page while the new one renders, then cross-fades to it (style it with
  `::view-transition-old(root)` and `::view-transition-new(root)`). The router starts the transition, renders, sets the scroll and
  focus, and only then lets the browser take the picture of the new page. `ViewTransitions.Timeout` (default 300 ms) bounds how long the
  old page is held while the new one renders (browsers do not paint during that wait), so a slow circuit gets the page without the
  animation instead of a frozen screen. It is skipped for the first load, a change of query or fragment only, an error or not-found page
  on either side, a browser without the API and a user who prefers reduced motion; the first navigation after the module loads is
  never delayed by its import. Checked in a browser: the transition started on the click, its update step finished in a few
  milliseconds and the animation ran to its end.
- **Per navigation:** `INavigationService.NavigateTo(uri, new ModNavigationOptions { Focus = false, Scroll = false, Announce = false })`
  skips effects for that navigation (`ModNavigationOptions` is named with the `Mod` prefix because `NavigationOptions` is the
  built-in router's type); a redirect starts a new navigation and does not carry them.
- If the JavaScript module cannot be loaded the effects are skipped (a warning is logged) and navigation goes on.


## Awaiting a navigation and history state

`INavigationService.NavigateToAsync(uri, options, cancellationToken)` (and `NavigateToRouteAsync(routeName, values, ...)`) navigate
like `NavigateTo` and tell how the navigation ended, so a save-then-navigate flow knows whether it landed:

```csharp
var result = await navigation.NavigateToAsync("customers/42");

if (result.Status == NavigationStatus.NotAuthorized) { ... }
```

| `NavigationStatus` | What happened | The URL |
|---|---|---|
| `Succeeded` | The page is shown, or the user was already there (nothing ran). | the target |
| `NotFound` | Nothing matched; the not-found content is shown. | the target |
| `NotAuthorized` | A guard denied a level; `result.Error` is the denial. | the target |
| `Failed` | A guard or loader threw, or redirects looped (`result.Error`); or a stage threw (`result.Exception`) and nothing changed. | the target, or unchanged for a throwing stage |
| `Cancelled` | A guard or stage canceled it, or the router was disposed. | unchanged |
| `Redirected` | A guard or redirect rule sent the user elsewhere; `result.RedirectedTo` is the absolute URL, and that navigation goes on by itself (its own outcome is not reported here). | the redirect target |
| `Superseded` | A newer navigation or a reload replaced it before it committed. | whatever the newer one does |

- `ForceLoad = true` hands the navigation to the browser and returns `Succeeded` at once; nothing is left to wait for.
- Without a mounted router there is no navigation to wait for: the call throws `InvalidOperationException`. A route name that cannot
  build a URL throws `RouteUrlGenerationException` through the returned task, and nothing navigates.
- The token stops the wait (`OperationCanceledException`), not the navigation.
- A navigation that another location-changing handler prevents (a `NavigationLock` that says no) gives the router no signal. It settles as
  `Superseded` when the next navigation starts, so a caller that awaits it should pass a token with a timeout.

**History state.** `ModNavigationOptions.HistoryState` stores a string with the new history entry (`history.state` semantics): it survives Back and
Forward and is read back as `RouteState.HistoryEntryState`. It is accepted by `NavigateTo(uri, options)` and `NavigateToAsync`. Serialize
structured data yourself. Route links do not carry a state: an anchor click cannot, so use `NavigateToAsync` from a handler.
