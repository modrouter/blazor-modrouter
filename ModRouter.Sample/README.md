# ModRouter.Sample

A standalone Blazor WebAssembly app that demonstrates and verifies `ModRouter` (slice 2: navigation
and rendering; slice 3a: guards, loaders, redirects and error states; slice 3b: route links, breadcrumbs, title, focus and
scroll; slice 4a: page discovery and the markup facade; slice 4b: authorization and the compatibility layer). It is a demonstration app and is not deployed. The router's own documentation starts at the
[router README](../ModRouter/README.md); how the sample's checks fit the rest of the development workflow is in
[Development](../ModRouter/docs/development.md).
`ModRouter.Sample.Extra` is a small class library beside it whose two pages are referenced by nothing
and found by reflection alone (see "Modes" and "Trimming check").

## What it shows

- Root `ModRouter` with a default layout and `NotFound` content (inside the layout).
- Nested routes with an index route: `/` Home, `/customers` (list), `/customers/{id:int}` (details),
  `/customers/{id:int}/orders/{orderId:guid}` (order), a second section `/vendors` (list) and `/vendors/{id:int}`
  (details) whose parent also hosts an outlet, plus `/query?tab=..&q=..&nums=..` (Blazor's built-in
  `[SupplyParameterFromQuery]`, which works under the router unchanged).
- Retention: each page prints `instance #N, parameters set Mx`. Moving between children never recreates the parent
  and updating `/customers/42` to `/customers/43` updates the same child instance in place.
- `INavigationService`: `NavigateTo`, `NavigateToRoute` by name (including ambient values) and `GetRouteUrl`.
- A state panel with the committed `RouteState` (path, query, trigger, failure, leaf, title, error, failed level, how
  many redirects led here).

Slice 3a (the buttons under the second nav row of the layout switch the fake session between signed out, user and admin):

- **Guard redirect:** `/account` is guarded by `SignedInGuard`; signed out, it redirects to `/login?returnUrl=...`, and
  signing in continues to the original page. Back from the login page returns to where the user came from.
- **Not authorized in place:** `/account/admin` is guarded by `AdminOnlyGuard`; a plain user gets the not-authorized
  content inside the (never remounted) account page, with a "Check again" button that runs the guard again.
- **Slow loader, streamed:** `/slow/{id}` keeps the old page for 1.2 s (the navigation indicator appears after 150 ms),
  then renders with its critical data; the orders arrive 1.5 s later (`ModDeferred`) and a feed streams in item by item
  (`ModStream`).
- **Loader failure and retry:** `/flaky/{id}` fails the first time per id; the error view renders inside the parent with
  a Retry button.
- **Redirect rule and aliases:** `/old/customers/{id}` forwards to the customer route; `/suppliers/...` is a canonical
  alias of `/vendors/...`.
- **Redirect loop and canceled guard:** `/loop-a` and `/loop-b` redirect to each other (a router-level error);
  `/blocked` always cancels, so the page and URL stay.

Slice 3b (the third nav row of the layout uses `RouteLink`; the breadcrumb and a search box sit below it):

- **Title and announcement:** every navigation sets `document.title` through `"{0} - Router sample"` (the state panel shows
  it) and a hidden live region announces it; back/forward set the title only.
- **Focus:** a link navigation moves focus to the first `h1`/`h2` in the content (`Focus.Selector = "#body h1, #body h2"`);
  `/late` renders its heading after 500 ms and focus waits for it; focus is never taken from the search box; the redirect-loop
  error content (a `role="alert"` element) takes focus when no heading matches. `MainLayout` carries a permanent
  `role="alert"` notice (`#layout-alert`), and `/no-heading` has no heading: the router never moves focus to the notice, only to its own
  error content, which it wraps in a `display: contents` error host.
- **Scroll:** `/long/1` and `/long/2` are tall pages: a new navigation starts at the top, back and forward restore each
  entry's own position. `/anchors#target` scrolls to a target that renders 400 ms after the navigation. Opening the sample with
  `?scroller=1` puts the content in a scrolling container and tells the router to manage that container instead of the window.
- **Route links and breadcrumb:** `aria-current="page"` on the link to the current page, `"true"` on a link to an ancestor, an
  exact `Href` link that is not current on a child page; the breadcrumb `Home › Customers › Customer 42` has real anchors and
  no link on the last crumb (vendors show `.Breadcrumb(...)` labels).

## Modes (slice 4a)

The route source is chosen by the page URL before the host is built (`?mode=...`, read like `?scroller=1` through a tiny
function in `index.html`); everything else (layout, links, state panel) is the same in every mode:

| URL | Routes come from |
|---|---|
| `/` (default) | the fluent map in `Program.cs`, as in the earlier slices |
| `/?mode=pages` | `@page` and the `Route*` attributes on every sample page (`[RouteName]`, `[RouteTitle]`, `[RouteParent]` + `[RouteIndex]` for the customers, vendors, account and flaky trees, `[RouteBreadcrumb]`, `[RouteCanonicalAlias]` with a second `@page "/suppliers"`, `@attribute [RouteGuard<SignedInGuard>]` and `[RouteGuard(typeof(AdminOnlyGuard))]`, `[RouteLoader<SlowCustomerLoader>]` and `[RouteLoader(typeof(FlakyLoader))]`), discovered with `routes.AddPages(typeof(Program).Assembly).AddPages(typeof(ExtraMarker).Assembly)`; only the redirect rule and the three guard routes that reuse the home component stay fluent. `/extra` and `/extra/{Id:int}` come from the Extra library, `/trim-probe` is an app page nothing references. |
| `/?mode=markup` | `AppMarkup.razor`: the customers tree (with `TitleFunc`), long, late, anchors, no-heading and about are `<ModRouteEntry>`s inside `<ModRouter><Routes>`; home, vendors, query and the slice 3a routes stay in `Program.cs`. Two buttons break the static-content rule on purpose: one renders a new entry after the freeze (an unhandled render exception in the console, the app goes on), one changes a registered entry's `Path` (ignored). The redirect rule is left out: its target is a markup route, which the startup compile cannot see. |
| `/?mode=markup-dup` | the same markup with the about entry named `home`, a duplicate of the startup route: the router renders the compile error (RT0010) through its `Error` fragment as `ConfigurationFailed`, bare, instead of an endless Loading. |
| `/?mode=compat` | `AppCompat.razor` (slice 4b): the `App.razor` of a typical site over the compatibility layer, with the fully-qualified tags `<ModRouter.Compatibility.Router>` and `<...AuthorizeRouteView>`, `CascadingAuthenticationState`, a `NotAuthorized` fragment that renders `RedirectToLogin` for an anonymous user, `NotFound` in its own `LayoutView`, and an `OnNavigate` that adds the Extra assembly (the "lazy" assembly of the sites). The pages are discovered by the Router itself; only the three guard routes that reuse the home component stay fluent (no redirect rule: its target would be a discovered page, RT0017). `Pages/Compat` shows the pattern: `_Imports.razor` stamps `[Authorize]` on every page there, `/reports` adds `[Authorize(Roles = "admin")]`, `/profile` has only the folder's attribute, `/notice` opts out with `[AllowAnonymous]`. The fake session doubles as the `AuthenticationStateProvider` (`SampleAuthenticationStateProvider`, registered with `AddAuthorizationCore` in every mode), so the session buttons sign in and out for the router's guard and its re-check too. `?slownav=1` makes `OnNavigateAsync` take 1.5 s. |

`index.html` also records the painted milestones (Loading, Navigating, layout, page, not-found, error) in `window.__domLog` so the
checks can prove that a deep link never shows not-found and that only a slow first navigation shows the bare Loading or Navigating.

## Run it

Open `ModRouter.Sample.slnx` in Visual Studio (the sample, the Extra library, the router library, its tests and the analyzer), or from the command line:

```bash
dotnet run --project ModRouter.Sample --urls http://localhost:5199
```

## Verify it in a real browser

Needs Node 22+ and Chrome (`CHROME_PATH` overrides the default location). With the sample running:

```bash
node ModRouter.Sample/tools/verify.mjs http://localhost:5199/
```

104 assertions. 24 for navigation and rendering: link clicks, `NavigateTo` (relative and rooted, the latter from
`GetRouteUrl`), named-route navigation with ambient values, browser back/forward recognized as `HistoryRestore`,
query-only updates in place, not-found inside the layout (including leaving a page that hosts an outlet), navigation
between two sections that both have child routes and rapid clicks. 33 for slice 3a: every behavior listed above (the
history entry count of a guard redirect, the held old page and the indicator during a slow load, the deferred value and
the stream filling in, the parent surviving an error and its retry, a superseded slow navigation never rendering). 31 for
slice 3b: the title after every navigation, the announcement, focus on the heading, not from the search box, after a late
heading and on error content, scroll to the top, restore on back and forward and a late fragment target, the route link
states and hrefs and the breadcrumb, no full page reload across all of it, and two probes of the JavaScript module (an
immediately satisfied effect releases its listeners; scroll tracking tolerates a missing container). Then 6 with the
content in a scrolling container (`?scroller=1`): the scroll behavior again against the container, and a clean browser
console (no unhandled exceptions, no router warning such as "without a prepared navigation", no `fail:`/`crit:` log lines
other than the two the sample provokes on purpose: the flaky loader and the redirect loop). Slice 4e added 10 of the 104:
eight that call the module directly with a bad focus selector, a bad scroll container and a malformed fragment (no throw, the
element with the literal id is scrolled to, the error alert takes focus, a warning once per selector, listeners released at
once and at the deadlines, the other effect still runs), and two for the alert of the layout (below).

For the base-path run set `<base href="/myapp/" />` in `wwwroot/index.html`, serve with
`MSYS_NO_PATHCONV=1 dotnet run ... --urls http://localhost:5198 -- --pathbase=/myapp` (the dev server's `--pathbase`
does not rewrite the base tag) and pass `http://localhost:5198/myapp/`. Restore the base tag afterwards.

The pages and markup modes have their own script (the default mode's checks above are unchanged):

```bash
node ModRouter.Sample/tools/verify-modes.mjs http://localhost:5199/ all   # or: markup | pages
```

39 assertions per base path. 23 for the markup mode: the startup and markup routes in one table, one painted state at `/`
(no Loading, no not-found), `GetRouteUrl` and `RouteLink` by a markup route name, a deep link into the nested markup
chain as the first painted state with its `TitleFunc` title and breadcrumb, a three-level chain, bare Loading then the page
for a slow deep link, back/forward, `NavigateToRoute` with ambient values, not-found, a startup guard, a changed entry
parameter ignored (checked with in-app navigations, since a full page load would reboot the app and register the entry
afresh: the old path still matches and the new one does not), an entry rendered after the freeze throwing while the app
keeps navigating, and `?mode=markup-dup`
rendering the RT0010 error through `Error` with nothing else on screen and no unhandled exception. 16 for the pages mode:
one painted state at `/`, titles and links from the attributes, nested pages as a chain, the index page, breadcrumb labels,
the second `@page` as a canonical alias, the Razor `[RouteGuard<T>]` and `[RouteGuard(typeof(...))]` guards, a `[RouteLoader<T>]`
loader, the fluent redirect into a discovered route, the Extra library's pages, the unreferenced app page, and a clean console.

The compat mode has its own script too:

```bash
node ModRouter.Sample/tools/verify-compat.mjs http://localhost:5199/
```

30 assertions per base path: the discovered home page inside `MainLayout` in one painted state, `OnNavigateAsync` run once
before the first navigation with the built-in's path form (base-relative, query kept), a deep link rendered through
`Found > AuthorizeRouteView > outlet` as the first painted state, a page of the assembly `OnNavigateAsync` added routable on
the very first navigation, the layout and parent instances kept while the child updates in place, `OnNavigateAsync` on every
navigation, back/forward as `HistoryRestore`, not-found inside the app's own `LayoutView` (a new layout instance, as the
built-in), an anonymous visitor on `/reports` sent through `NotAuthorized` and `RedirectToLogin` to the login page with the
return URL, a signed-in user without the role shown the authenticated `NotAuthorized` branch, a sign-in as admin re-running
the guard and reloading into the page, a sign-out on the page sending the user to the login page, no `OnNavigate` loop, the
`/profile` page for any signed-in user, the `[AllowAnonymous]` `/notice` page for an anonymous user, a session change
without an outcome change reloading nothing, and with `?slownav=1` the bare `Navigating` for the slow first callback, the
old page plus the navigation indicator for a slow later one, a superseded callback's token canceled, and a clean console
throughout. The same script runs against the trimmed Release publish (below), which proves the `[UnsafeAccessor]` path to
the framework's `NavigationContext` on the trimmed Mono build.

`tools/cdp.mjs` gives every CDP call a deadline (`CDP_TIMEOUT_MS`, default 30 s), so a stalled Chrome fails a run instead of
hanging it.

## Trimming check

A Release publish is trimmed (`TrimMode=partial`): the app assembly and ordinary references are kept whole, but the Extra
library is `IsTrimmable` and nothing references its pages, so the app roots it (`TrimmerRootAssembly` in the sample's
`.csproj`). Serve a publish with the static SPA server and run the pages checks against it:

```bash
dotnet publish ModRouter.Sample/ModRouter.Sample.csproj -c Release -o /tmp/pub
node ModRouter.Sample/tools/serve.mjs /tmp/pub/wwwroot 5177
node ModRouter.Sample/tools/verify-modes.mjs http://localhost:5177/ pages      # /extra/3 found
```

Then without the root, after deleting the sample's `obj/` (ILLink is incremental and does not treat the root as an input;
a root added or removed without that is silently ignored, which the check reproduces):

```bash
rm -rf ModRouter.Sample/obj
dotnet publish ModRouter.Sample/ModRouter.Sample.csproj -c Release -p:RootExtra=false -o /tmp/pub-noroot
node ModRouter.Sample/tools/serve.mjs /tmp/pub-noroot/wwwroot 5178
EXPECT_EXTRA=missing node ModRouter.Sample/tools/verify-modes.mjs http://localhost:5178/ pages   # /extra/3 not found, /trim-probe still found
```

## Results

Slice 4e (the browser module): 104/104 default checks, 39/39 mode checks and 30/30 compat checks at `/` and at `/myapp/`. Six of the eight module checks fail on the old module (a bad selector,
container or fragment); with the old document-wide alert fallback the layout's alert takes focus from the late heading, so
the existing check for it fails as well.

Slice 4b: 30/30 compat checks, 94/94 default checks and 39/39 mode checks at `/` and at `/myapp/`; 30/30 compat checks on a
trimmed Release publish served by `tools/serve.mjs` (`Microsoft.AspNetCore.Components.Authorization.*.wasm` trimmed to 7,957
bytes, the Extra assembly 6,421 bytes with its root). The first `/myapp/` run found the sample's `RedirectToLogin` building a
return URL without the base path (a full page load out of the app); it now uses `new Uri(uri).PathAndQuery`, as the guard does.

Slice 4a: 94/94 default checks and 39/39 mode checks at `/` and at `/myapp/`; trimmed publish with the root
`ModRouter.Sample.Extra.*.wasm` 6,421 bytes and `/extra/3` found (16/16), without the root 4,373 bytes
(only `ExtraMarker` left) and `/extra/3` not found while `/trim-probe` is (16/16); re-adding the root without cleaning `obj/`
left the 4,373-byte assembly in place.

Recorded after the slice 3b prototype: 88/88 checks at `/` and 88/88 at `/myapp/` (headless Chrome, .NET 10.0.10). The
first browser run of slice 3a found an unhandled render exception (a page with named deferred or stream slots received
the next route's data just before it was removed); it is covered by a bUnit regression test and the components now
render their fallback for a name the route does not provide. The first run of slice 3b found that forward navigation
restored the wrong scroll position because the browser's own restoration (`history.scrollRestoration = "auto"`) ran after the
router's; the router now sets it to `"manual"`.
