# Known limitations

Behavior that can surprise you, gaps that are known and not fixed, and the reasoning behind choices that look like gaps. Back to the
[README](../README.md). Things that were fixed are not listed here; the [changelog](../CHANGELOG.md) has them.

Each row has one status:

- **accept**: the router behaves this way on purpose or at an acceptable cost. The note gives the reason and what to expect.
- **open**: a known gap. The note gives the trigger that would reopen it.

## Routing and templates

| Limitation | Status | Note |
|---|---|---|
| RT0012 can reject a pair of templates that a per-segment rule would order (`/a/{x?}` against `/a/{y}/{z?}`) | **accept** | The terminal carries one optional flag and `Explain` shares the coarse rank. The stricter rule errs on the safe side: such a pair fails at startup with RT0012 and never silently picks a winner. Expect to rename or constrain one template. |
| `min( 1 )` and `min(1)` stay separate trie edges | **accept** | Whitespace inside a constraint argument is not trimmed, because a factory may treat it as significant (`regex`, a custom constraint). Two routes that differ only by that whitespace are ordered by key instead of being reported as ambiguous. |
| A throwing `TryMatch` or `TryFormat` in a custom constraint escapes `Match`, `Explain`, `TryGenerateUrl` and a `RouteLink` render | **accept** | A factory that throws is RT0004, and inside a navigation the coordinator fails that navigation and logs it. `IRouteConstraint` documents that implementations must not throw; wrapping every call would cost the match path for a contract violation. |
| A null route name throws `ArgumentNullException` from `TryGetRoute` | **accept** | A null route name is a caller bug, so it throws instead of returning false. |
| RT0015 compares parameter **names** only, so a declared `CanonicalAlias()` can still fail at run time | **open** | An alias `{id:alpha}` against a canonical `{id:int}` compiles, and `/people/abc` reaches the throw in the redirect stage: the click is silently prevented and `NavigationFailed` (1004) is logged; a first load commits a `NavigationFailed` state. Reopen when an app declares `CanonicalAlias()` on a route whose alias constrains a parameter differently; compare the constraints in RT0015, or render in place for a declared alias too. |
| `RouteMatchChain` is a record whose equality is reference equality on the level list | **accept** | Two chains from two matches are never equal even when their contents are. Compare `Leaf.Parameters`, which has value equality. |

## URL generation

| Limitation | Status | Note |
|---|---|---|
| Ambient values after an omitted optional parameter produce `OptionalGap` | **accept** | An ambient value after an omitted optional parameter is reported as `OptionalGap` with a message that names both parameters; ASP.NET would drop the ambient value instead. Expect an error, never a wrong URL; pass an explicit `null` for the ambient name to omit it. |

## Navigation and coordinator

| Limitation | Status | Note |
|---|---|---|
| A handler-less `LocationChanged` can commit a stale prepared state | **accept** | Unreachable under Blazor (the only handler-less changed event is the same-page fragment case). Under Blazor the platform's cancellation token for a location change is canceled when the navigation ends whether it was allowed, prevented or superseded, so the router cannot tell that another handler prevented a navigation, and .NET 10 offers no other signal. Reopen if a non-Blazor adapter appears or .NET adds a signal. |
| `NavigateTo(sameUrl, state)` does not run the pipeline | **accept** | `HistoryEntryState`-only changes to the current location are not navigations, so the store keeps the earlier history state. |
| `RouteState.Fragment` and `AbsoluteUri` are stale after a fragment-only change | **accept** | A fragment-only change on the current location is let through without running the pipeline, so the committed state keeps the fragment it committed with; the browser performs the same-page jump itself. Read `NavigationManager.Uri` for the live fragment. |
| The static layout-type cache does not observe a hot reload that changes `[Layout]` | **accept** | The built-in router caches the same way; restart the app after changing a layout attribute. |
| `ReloadAsync` and `Retry` supersede an in-flight user navigation | **accept** | A reload is a navigation like any other and the newest wins. Expect a reload on a timer to cancel a link click in flight; call it from user actions. |
| A scroll save can overwrite a position the restore could not reach yet | **accept** | Content that arrives after the timeout cannot be restored. A replaced history entry (`ReplaceHistory`) restores its predecessor's position. The [navigation](navigation.md) page describes late content. |
| A redirect that starts on back or forward counts as `Programmatic` | **accept** | The origin is right for the first load; a guard that redirects a Back navigation announces and focuses like a new page. .NET 10's `LocationChangingContext` cannot tell a traversal from `NavigateTo`; the only signal would come from the browser's Navigation API, which is large and unevenly supported. Reopen if every supported browser has it, or .NET exposes a traversal signal. |
| A redirect that differs from the current page only by its fragment still pushes a history entry | **accept** | A same-page jump keeps working through a redirect; the cost is one extra history entry. |
| The location-changing handler's same-location shortcut ignores whether this router has committed | **open** | After a remount (a second `ModRouter` over the same store) the store's current state is the previous router's. A link that supersedes the new router's first navigation and redirects to the previous router's committed URL is let through with no pipeline, and nothing commits. Reopen when a remount shows it. |
| A detach during a first load still commits a `NavigationFailed` state | **open** | The canceled navigation returns `Cancelled`, and the first-load path commits a `NavigationFailed` state into the store when nothing was shown yet. Reopen if a stale `NavigationFailed` page shows after a router is disposed during its first load. |
| A first load or a reload during `OnNavigateAsync` throws when the platform's own current URI does not parse | **accept** | Blazor's `NavigationManager.Uri` is always an absolute URI, so only a broken custom adapter reaches it, and an exception is the right signal for that. |
| `Focus.Selector` is not validated on the server | **accept** | There is no CSS parser in .NET, so `RouteMap.Focus` and `[RouteFocus]` only check for white space. A selector the browser rejects is ignored and warned about once per selector in the console (see the focus text on the [navigation](navigation.md) page). |
| The error `role="alert"` of `ModDeferred` and `ModStream`, and a `role="alert"` in the `NotFound` content, do not take focus as the fallback | **accept** | The fallback chooses only router error content, so an alert that belongs to a layout or a page is never picked. `ModDeferred` and `ModStream` render theirs inside the page, and the `NotFound` content is not wrapped because a compatibility-mode `NotFound` brings its own `LayoutView`. The stock Blazor template's `<p role="alert">` with no heading therefore does not move focus. Put a heading on the page or the not-found content, or set `Focus.Selector` to something it matches. |
| Error content sits inside a `display: contents` wrapper | **accept** | Every error rendering (and the compatibility `AuthorizeRouteView`'s `NotAuthorized` fragment, which the built-in renders bare) is wrapped in `<div data-mod-route-error-host style="display:contents">`. Layout is unaffected; a CSS child combinator on error content (`.panel > p`) sees the wrapper and needs a descendant combinator. Documented on the [navigation](navigation.md) and [compatibility](compatibility.md) pages. |

## Guards, loaders and stages

| Limitation | Status | Note |
|---|---|---|
| A stage that returns `default` returns null | **accept** | `StageResult` is a class. The coordinator catches the resulting `NullReferenceException` and reports a failed navigation and logs it, with no custom message. The contract is on the [routing](routing.md) page ("Navigation stages"). |
| The bind stage does not put the bound values on the stage context | **accept** | Guards and loaders read `RouteContext.InheritedParameters`. |

## Route sources

| Limitation | Status | Note |
|---|---|---|
| Blazor Hybrid and the Auto render mode are not verified | **accept** | Blazor WebAssembly is the main target and a server host with prerendering is described on the [hosting](hosting.md) page (`ModRouter.AspNetCore`, `PersistLoaderData`). The route table holder takes a lock on `Freeze` and the binder's cache is a `ConcurrentDictionary`, so shared use is safe by construction. |
| The freeze signature ignores `ModRoutePages.Filter` and `TitleFunc` identity | **accept** | A WebAssembly app has one router and one markup, so a remount with different closures that keeps the first table is harmless. Revisit before any hot-reload scenario. |
| `<ModRoutePages>` has no RT0024 test of its own | **open** | `RouteEntryCollector` shares `PageDiscovery` and `RouteMap.FullTemplatesOf` with `AddPages`, and `PageDiscoveryTests` covers that path; no test drives an ambiguous `[RouteParent]` through the markup collector. Reopen with the next change to the collector. |

## Authorization and compatibility

| Limitation | Status | Note |
|---|---|---|
| A hook that never completes holds the waiting navigation (and a prepared navigation prevented by a `NavigationLock`) | **open** | `IsNavigating` stays true and authentication events are deferred until the next navigation, so protected content can outlive a sign-out until then. A redirect to another page that a lock prevents has the same effect. There is no platform signal to wait for (see the first row of Navigation and coordinator); a bounded wait would close the hook case. Reopen when an `OnNavigateAsync` can hang or the platform signals a prevented navigation. |
| The re-check key is `Kind@Level` only | **accept** | A second guard on the same level that denies with the same kind after a state change does not reload, so the error keeps the first guard's message. Adding the guard type to the key is a separate change. |
| An event raised while a follow-up check's guards run is treated as that read's own | **accept** | This is the feedback-loop fix. A genuinely external event in that window (one follow-up check long) is dropped; a provider notifying from another thread at that moment would need a different rule. |
| A reload while `OnNavigateAsync` runs reloads the platform's current URI now | **accept** | Matches the built-in, which renders the current location after its hook (see the `Router.OnNavigateAsync` rows of the [compatibility matrix](compatibility.md#compatibility-matrix)). |
| An intercepted link click to a URL no route matches renders `NotFound` where the built-in does a full page load | **accept** | A difference row of the [compatibility matrix](compatibility.md#compatibility-matrix). Sites that rely on `NotFound` for dead links would reload if the behavior changed; an option could add it later. |
| The compatibility `Router` discovers at its first navigation | **accept** | A startup `Redirect(...)` or guard redirect by name to a discovered route fails with RT0017. A difference row of the matrix, pinned by a test. |
| A not-authorized fragment cannot name the built-in `[Authorize]` guard | **open** | `RouteError.GuardType` is the internal `AuthorizeAttributeGuard`. Compare `GuardType` with your own guard types and treat any other as the built-in denial (see [authorization](authorization.md)). Reopen when a fragment must word the built-in denial differently; add a public marker on `RouteError`. |

## Observability

| Limitation | Status | Note |
|---|---|---|
| A prepared navigation prevented by another `LocationChanging` handler gives the router no signal | **open** | Its activity stays open and its record unpublished until the next navigation records it as `Superseded`; documented on the [observability](observability.md) page. Same platform gap as the hook row above; reopen with a platform signal. |
| An activity can be left ambient in `PrepareAsync`'s `finally` | **accept** | Only the compatibility `Router` with `OnNavigateAsync`: a commit of an earlier navigation can run its loader streams and subscribers under the wrong trace. No behavior changes, only trace parentage. |
| A navigation still preparing when the router is disposed records `Canceled` with no detail | **accept** | A stage that ignores its token keeps its activity open until it returns (documented). |
| After an initial-load failure the record says `Failed` with `CommittedAs` null | **accept** | A `NavigationFailed` state is committed under the same id without a record of that commit; the failure itself is recorded. |
| The history can hold up to 1,000 records per circuit under Blazor Server | **accept** | `Diagnostics.HistorySize` caps it. |
| The outlet warning's settle window can log a false positive | **accept** | A parent that loads for more than a second can trigger it, and states replaced within the window are never judged. Documented on the [observability](observability.md) page. |

## Public API surface

| Limitation | Status | Note |
|---|---|---|
| `ScrollOptions` collides with `Syncfusion.Blazor.Schedule.ScrollOptions` | **accept** | It bites only in a file that imports both namespaces and names the type (`options.Scroll` rarely does); qualify the name. Renaming is breaking for every consumer. |
| The option types live in the root namespace while `ModNavigationOptions` lives in `.Navigation` | **accept** | Moving them breaks every `using` and no clash is confirmed. |
| `TryGetRouteUrl` was added to the public `INavigationService` without a default implementation, and `RouteCandidateVerdict` defaults to `Matched` | **accept** | Read the candidates `Explain` returns; do not build them. A default implementation that throws `NotSupportedException` is not acceptable, so an application type that implements `INavigationService` itself must add the member. |

The public surface is pinned: `ArchitectureTests.The_public_surface_is_exactly_the_reviewed_list` compares the assembly's exported
types with a reviewed list, so a type cannot become public, or stop being public, without a deliberate edit to that list. A type
stays public only if it appears in the signature of a public member a consumer calls or implements, is an attribute or metadata item a
consumer applies, is a documented extension point, or is an option, outcome or notification type a consumer reads. These are kept
public for that reason:

| Type | Reason |
|---|---|
| `CompiledRoute`, `TemplateSegment`, `ConstraintSpec`, `TemplateSegmentKind` | Exposed by public members (`CompiledRouteTable.Routes`, `RouteCandidate.Route`, `CompiledRoute.Segments`, `TemplateSegment.Constraints` and `Kind`); narrowing fails with CS0053 or CS0051. |
| `StageOutcome`, `StageRecord` | `NavigationRecord.Stages`, read by diagnostics consumers (CS0053 when narrowed). |
| `INavigationStage`, `StageResult`, `RouteNavigationContext` | The documented stage extension point. |
| `PageAssembly` | `PageAssemblyCollection` enumerates it, and `options.Pages` is enumerable by consumers (CS0050 when narrowed). |
| `RouteLevelData`, `RouteOutletContext`, `RouteStateStore`, `RouteTableHolder` | Exposed by public members (`RouteState.LevelData`, the cascaded outlet context) or DI-resolved services the docs describe. |
| `BreadcrumbTrail`, `BreadcrumbEntry` | `BreadcrumbTrail.From(RouteMatchChain)` is the engine behind `RouteBreadcrumb` and is usable by a custom breadcrumb component. |
| `RouteTitleFormatter`, `RouteTitles` | Pure `Core/` helpers with no use outside the library, left public as a design call. Narrowing them later is a deliberate edit to the reviewed list. |

## Tests and tools

| Limitation | Status | Note |
|---|---|---|
| `ArchitectureTests` inspects member signatures for the Core Blazor-free rule, not method bodies | **open** | The rules for assembly enumeration, the authorization packages and log writes do read IL. Reopen on a known defect or a new forbidden dependency; scan IL type references with `System.Reflection.Metadata`. |
| The swallowed-exception assertion in the component tests can miss late exceptions | **accept** | `RouterComponentTests.TearDown` sees only exceptions raised before it runs. |
| Two tests lean on implementation details | **accept** | `CommitOwnershipTests` finds the awaiting method's continuation in a queue-only synchronization context by the compiler-generated state-machine type name, so renaming or restructuring `NavigateInitialAsync` or `ReloadAsync` breaks it with a clear assertion message. `UnparseableLocationTests.An_arrived_state_waiting_on_its_hook_is_released_when_an_unparseable_location_arrives` relies on two `Task.Yield` calls (best effort). |
| `tools/verify.mjs` uses fixed sleeps, a default `chrome.exe` path and a hand-edited base tag | **accept** | Chrome starts on a free port and is killed with its profile removed. What remains is the fixed 300 to 700 ms sleeps, the Windows default Chrome path (set `CHROME_PATH`) and editing `wwwroot/index.html` by hand for the base-path run. A developer tool. A hard kill of node on Windows bypasses the cleanup handlers in `cdp.mjs`; delete the leftover `modrouter-chrome-*` folder in the temp directory by hand. |
| `verify-compat.mjs` checks `onNavigate <= 6` as its loop bound | **accept** | The bound is weak but real loops are caught by the exact counts elsewhere in the script; the unit tests cover the superseded hook. |
