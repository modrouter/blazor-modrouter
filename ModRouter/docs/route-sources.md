# Route sources

How routes get into the table: page discovery, the Route attributes and the markup facade. Back to the [README](../README.md).
This page moves from the original README unchanged apart from slice labels.

## Route sources

Routes come from three sources, and every source produces the same `RouteDefinition`s through the same compiler, so
matching, URL generation, guards, loaders, titles and effects behave the same whichever declared the route:

| Source | Where | Compiled |
|---|---|---|
| the fluent map | `AddModRouter(routes => routes.Route(...))` | at startup, in `Program.cs` |
| discovered pages | `routes.AddPages(assembly, filter)` or `options.Pages.Add(assembly, filter)` | at startup, after the fluent routes |
| markup | `<ModRouter><Routes>` with `ModRouteEntry` and `ModRoutePages` | on the router's first render, after the startup definitions |

### Page discovery and the `Route*` attributes

```csharp
builder.Services.AddModRouter(routes => routes
    .Route("/", typeof(Home), name: "home")
    .AddPages(typeof(App).Assembly)                             // every @page component of the app
    .AddPages(typeof(Extra.Marker).Assembly, t => t.Namespace == "Extra.Pages"));   // a filter keeps the types it returns true for
```

`AddPages` (namespace `ModRouter`, the same `using` as `AddModRouter`; the `Route*` attributes and
`PageAssemblyCollection` are in `ModRouter.Discovery`) adds every routable component of the assembly: a public,
non-abstract, non-generic `IComponent` with at least one `[Route]` (what `@page` compiles to; it is matched by type, so an
imported `Microsoft.AspNetCore.Mvc` cannot confuse it) and without `[ExcludeFromInteractiveRouting]`. It uses
`Assembly.GetExportedTypes()` like the built-in router. The first `[Route]` is the route's template and the others are
its aliases (they render in place, the router default). A page without `[RouteName]` has no name: `Href` links and
`NavigateTo(uri)` work, `NavigateToRoute`, `GetRouteUrl` and `RouteLink Route=` need a name. `[Layout]`/`@layout` is
read from the depth-0 component as before; `[SupplyParameterFromQuery]` and `[Parameter]` binding are unchanged. Each
definition's `Source` is `page: <Namespace.Type> (<assembly>)`.

| Attribute | Fluent equivalent |
|---|---|
| `[RouteName("customer")]` | `name:` |
| `[RouteTitle("Customer")]` | `title:` (a static string; a computed title stays in the fluent map or markup) |
| `[RouteBreadcrumb("Customers")]`, `[RouteBreadcrumb(Hidden = true)]` | `.Breadcrumb(label)`, `.Breadcrumb(hidden: true)` |
| `[RouteGuard<SignedInGuard>]`, `[RouteGuard(typeof(SignedInGuard))]` (repeatable) | `.Guard<T>()`, in attribute order |
| `[RouteLoader<CustomerLoader>(RouteReloadPolicy.Never)]`, `[RouteLoader(typeof(CustomerLoader))]` | `.Loader<T>(policy)` |
| `[RouteFocus("#heading")]`, `[RouteNoAnnounce]` | `.Focus(selector)`, `.NoAnnounce()` |
| `[RouteCanonicalAlias]` | `.CanonicalAlias()` |
| `[RouteIndex]` | `.Index(...)`: the page is the index of its `[RouteParent]` |
| `[RouteParent(typeof(CustomersPage))]` | nesting (below) |

Both attribute forms work in Razor: `@attribute [RouteGuard<SignedInGuard>]` and `@attribute [RouteGuard(typeof(SignedInGuard))]`.

**Nesting.** Pages are flat by default (each `@page` is a top-level route, so an existing app works unchanged).
`[RouteParent(typeof(P))]` nests a page under `P`, which must be discovered in the same `AddPages` call or be a route the
fluent map declared (before the `AddPages` call) with that component type, at **exactly one** route (a component the map
declares at several routes is ambiguous, RT0024); the child renders in the parent's `ModRouterOutlet`. A parent that is
both discovered in the call and declared in the map is the discovered page. Razor's `@page` requires a template that
starts with `/` (RZ9988), so a nested page writes its **full URL** and discovery removes the parent's template: `@page "/customers/{Id:int}"` under `/customers` is the child
template `{Id:int}`, and an index page repeats the parent's template (`@page "/customers"` with `[RouteIndex]`). A
template without a leading `/` (possible only through `@attribute [Route("{Id:int}")]`) is taken as relative as it is.
On a **top-level** page (no `[RouteParent]`) a template or alias without a leading `/` is rooted (`[Route("tool")]`
becomes `/tool`, as the built-in router reads it); only the fluent map rejects that shape (RT0014).
`ModRoutePages` inside `<Routes>` resolves a `[RouteParent]` the same way, against the startup definitions plus the
`ModRouteEntry` entries of the same markup (its pages are collected after them), so a page can nest under a markup route.

```razor
@page "/customers/{Id:int}"
@attribute [RouteParent(typeof(CustomersPage))]
@attribute [RouteName("customer")]
@attribute [RouteTitle("Customer")]
```

Discovery validates the attributes and reports every problem at once as a `RouteCompilationException`, from
`AddModRouter` for a startup source:

| Code | Meaning |
|---|---|
| RT0019 | `[RouteParent]` names a type that is neither a page of the set nor a route of the map |
| RT0020 | `[RouteParent]` cycle |
| RT0021 | `[RouteIndex]` without `[RouteParent]`, or with a template that is not the parent's |
| RT0022 | an attribute is invalid for the page: a guard or loader type that does not implement its interface, a `[RouteBreadcrumb]` with both or neither of a label and `Hidden`, an empty name, title or selector, or a nested page whose template does not extend its parent's |
| RT0023 | an `ModRouteEntry` is invalid (no `Component`, `Index` with a `Path`, a guard or loader type that does not implement its interface, both `Breadcrumb` and `BreadcrumbHidden`, both `Title` and `TitleFunc`, `AllowAnonymous` together with `Authorize`, `Roles` or `Policy`), or an `ModRoutePages` has no `Assembly` or its discovery threw (the assembly's exported types could not be loaded, or its `Filter` threw; the cause is the exception's `InnerException`) |
| RT0024 | `[RouteParent]` names a component the map declares at more than one route, so the nesting would depend on declaration order; give the parent a route of its own, name a parent declared once, or declare the child in the fluent map |

The existing codes apply to discovered and markup routes unchanged: a page whose name a fluent route already uses is
RT0010, two components with the same template are RT0012 (as in the built-in router).

### The markup facade

```razor
<ModRouter DefaultLayout="typeof(MainLayout)">
    <Routes>
        <ModRouteEntry Path="/" Component="typeof(HomePage)" Name="home" Title="Home" />
        <ModRouteEntry Path="/customers" Component="typeof(CustomersPage)" Name="customers" Title="Customers">
            <ModRouteEntry Index Component="typeof(CustomerListPage)" Name="customer-list" />
            <ModRouteEntry Path="{id:int}" Component="typeof(CustomerPage)" Name="customer" TitleFunc="@(p => $"Customer {p["id"]}")" Guard="typeof(SignedInGuard)" />
        </ModRouteEntry>
        <ModRoutePages Assembly="typeof(App).Assembly" />
    </Routes>
    <Loading><p>Loading...</p></Loading>
    <NotFound><p>Not found.</p></NotFound>
    <Error Context="error"><p role="alert">@error.Kind: @error.Message</p></Error>
</ModRouter>
```

- `ModRouteEntry` mirrors the fluent map: `Path` (rooted for a top-level entry, relative under a parent), `Component`,
  `Name`, `Title` or `TitleFunc`, `Aliases`, `Index`, `Breadcrumb` or `BreadcrumbHidden`, `Guard` and `Loader` (a `Type` each,
  validated when the router compiles), `ReloadPolicy`, `Focus`, `NoAnnounce`, `CanonicalAlias`, `Reuse`, and nested entries
  as child routes. `ModRoutePages` is the markup form of `AddPages` (`Assembly`, `Filter`); its pages come after the entries.
  Both render nothing. A markup route has no file and line: its `Source` is the reconstructed tag,
  `markup: <ModRouteEntry Path="/customers" Component="CustomersPage" Name="customers">`.
- An app whose routes are all in markup calls `AddModRouter()` without a routes delegate.
- **The first pass.** With `Routes`, the router's first render shows the entries (registering themselves) and a **bare**
  `Loading`, outside the layout: a layout that renders a `RouteLink` or calls `GetRouteUrl` for a markup route would
  otherwise ask for a table that is not final. In its first after-render callback the router compiles the startup
  definitions plus the markup ones, validates the bindings, freezes the table and only then starts the initial navigation,
  so a deep link is the first committed state and not-found never flashes. At `/` with synchronous stages the bare pass is
  never painted (both renders land in one task); with a slow guard or loader on a deep link the user sees `Loading` without
  the layout for that long. Without `Routes` nothing changes: one render, inside the layout. **Not under prerendering:**
  server-side prerendering never runs `OnAfterRenderAsync`, so a markup-routes app would prerender only the bare `Loading`
  and the holder would never be frozen; use fluent or `@page` routes in a prerendered app (see [Hosting](hosting.md)). Interactive
  rendering without prerendering runs it as usual.
- **Static content.** `<Routes>` must render the same entries on the first render and never change. An entry rendered
  after that (behind an `@if` that becomes true later, or added by a re-render) throws an `InvalidOperationException` from
  its initialization ("The route table is already compiled..."); Blazor WebAssembly logs it and keeps running. A parameter
  that changes on a registered entry is ignored, with a debug log.
- **Errors are rendered.** A `RouteCompilationException` or `RouteBindingValidationException` from the markup compile is
  logged and shown bare through `Error` as a router-level `RouteError` of kind `ConfigurationFailed` (message = the
  exception text, `Retry()` does nothing), never an endless `Loading`. The router then gives navigation up: a link click or
  `NavigateTo` neither commits a state nor changes what is on screen (the startup table is not what the app declared), and
  a failed name lookup no longer carries the not-final hint. The same mistake in a startup source still throws
  from `AddModRouter`.

### The route table holder

`AddModRouter` registers a `RouteTableHolder` (also the `INamedRouteRegistry`) instead of the `CompiledRouteTable`,
and every consumer (the stages, the coordinator, `INavigationService`, and through them the outlet, route links and the
breadcrumb) reads `holder.Table` per use. `AddModRouter` compiles the startup table; `ModRouter` freezes the
holder exactly once before its first navigation, with that table or with the startup-plus-markup table. Before the
freeze the holder serves the startup table, so a fluent-only app may resolve routes before the router mounts, and a
failed name lookup then ends with "The route table is not final yet: markup `<Routes>` entries are compiled after
ModRouter's first render." A second freeze throws, except a remount with the same markup (a no-op); a remount with
different markup or a second router is an error. Resolve markup routes after the router's first render (an event
handler, `OnAfterRender`, a page), not from a root component rendered beside it. The holder and the binder are
singletons shared by every circuit of a server host: the freeze takes a lock, the table is read with a volatile read and
the per-type parameter metadata is a concurrent dictionary.

### Canonical template and generation across templates

A multi-`@page` component usually lists the parameterless template first (`@page "/org-chart"` then
`@page "/org-chart/{EmployeeId:int}"`). The first `[Route]` stays the route's template, as in the built-in router, and
URL generation **falls back across the route's templates**: when the values cannot build the canonical template, the
route's own aliases (under the canonical ancestor path) are tried in declaration order and the first they satisfy is
used. `GenerateUrl("org-chart")` is `/org-chart`, `GenerateUrl("org-chart", new { employeeId = 5 })` is `/org-chart/5`.
It is a fallback only: a generation the canonical template satisfies is unchanged (a property test over random tables
checks that), a route with `CanonicalAlias()` never falls back (its aliases are not places to send people), and when no
template fits, the canonical template's error is reported.

### Trimming

The default Blazor WebAssembly publish (`TrimMode=partial`) needs nothing: pages in the app assembly and in any
referenced project that is not `IsTrimmable` survive with their attributes, even when nothing references them. An
assembly that opts into trimming (`<IsTrimmable>true</IsTrimmable>`, or a NuGet package marked trimmable) whose pages
are found only by reflection must be rooted in the **app** project:

```xml
<ItemGroup>
  <TrimmerRootAssembly Include="My.Pages.Assembly" />
</ItemGroup>
```

After adding a root, delete the app's `obj/` before publishing: ILLink is incremental and the root item is not one of
its inputs, so a root added after a previous publish is silently ignored until then (the sample's trimming check
reproduces this). `AddPages` and `options.Pages.Add` are `[RequiresUnreferencedCode]`, so a trimmable caller gets the
analyzer's warning (IL2026); `AddModRouter` itself suppresses it, since the requirement is the caller's. `<ModRoutePages>`
in markup gets **no** warning: the requirement sits on a component parameter, which the analyzer cannot see, so a
markup-routes app must remember the root itself.
`TrimMode=full` is unsupported (the after-navigation interop payload is an anonymous type the trimmer breaks); the
`Type`-taking APIs (`ModRouter.DefaultLayout`, `RouteMap.Route(Type)`, `RouteDefinition.Component`, `ModRouteEntry.Component`,
the guard, loader and parent attributes) carry `[DynamicallyAccessedMembers(All)]` as hygiene.

