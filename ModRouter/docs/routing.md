# Routing

The routing core: templates, constraints, precedence, the compiled table and how the router renders it. Back to the [README](../README.md).
This page moves from the original README unchanged apart from slice labels.

## The routing core (`Core/`)

| Area | Types |
|---|---|
| Syntax | `TemplateParser`, `TemplateSegment` |
| Constraints | `IRouteConstraint`, `IConstraintRegistry`, `ConstraintRegistry` (int, long, bool, decimal, double, float, guid, datetime, alpha, alphanum, min(n), max(n), range(n,m), length(n[,m]), minlength(n), maxlength(n), file, nonfile, regex(p): every constraint of the built-in router plus alphanum; `BuiltInConstraintParityTests` checks the list and the values against the framework) |
| Definitions | `RouteDefinition`, `RouteMap` (fluent), `RouteMetadataCollection`, `RouteComponentReuse` |
| Compilation | `RouteCompiler`, `CompiledRouteTable`, `CompiledRoute`, `RouteCompilationException` |
| Matching | `CompiledRouteTable.Match`, `RouteMatchChain`, `RouteContext`, `RouteParameters`, `Explain` |
| Generation | `INamedRouteRegistry` (implemented by the table and, in DI, by `RouteTableHolder`), `RouteValues` |

## Usage

```csharp
var routes = RouteMap.Create()
    .Route("/", typeof(Home), name: "home", title: "Home")
    .Route("/customers", typeof(CustomersPage), name: "customers", title: "Customers", children: customers => customers
        .Index(typeof(CustomerList), name: "customer-list")
        .Route("{id:int}", typeof(CustomerDetails), p => $"Customer {p["id"]}", name: "customer-details", children: customer => customer
            .Route("orders/{orderId:guid}", typeof(OrderDetails), name: "customer-order-details")));

CompiledRouteTable table = RouteCompiler.Compile(routes.Build());

RouteMatchResult result = table.Match("/customers/42");           // chain: customers -> customer-details (id = 42)
string url = table.GenerateUrl("customer-details", RouteValues.From(new { id = 42 }));   // "/customers/42"
Console.WriteLine(table.Explain("/customers/new"));               // why a URL matched what it did
```

## Rules worth knowing

- Child templates are relative; a leading `/` on a child is a compile error. Root templates start with `/`.
- Precedence is decided by the table, not by declaration order: literal, then constrained parameter (more constraints
  first, ties by constraint key), then unconstrained parameter, then the omitted form of an optional parameter, then
  catch-all. A catch-all matches zero or more segments (`{*p}` and `{**p}` are the same).
- Path segments are percent-decoded once as strict UTF-8; a malformed or non-UTF-8 escape is `MalformedPath`.
- `RouteCompiler.Compile` reports every problem at once (`RouteCompilationException.Errors`, codes `RT0001`–`RT0018`, plus `RT0027` for groups, `RT0028` for slots and `RT0029` for keep-alive);
  the same exception carries `RT0019`–`RT0022` and `RT0024` from page discovery and `RT0023` from the markup entry collector (see
  [Route sources](route-sources.md)).
- `Match` never throws; `RouteMatchResult.Failure` is `NotFound` or `MalformedPath`.
- `GenerateUrl` generates from the canonical template, validates values against constraints, percent-encodes as
  UTF-8, sorts query keys ordinally, and can fall back to caller-supplied ambient values. When the canonical template
  cannot be built from the values, the route's own aliases are tried in declaration order and the first they satisfy is
  used (see [Canonical template and generation across templates](route-sources.md#canonical-template-and-generation-across-templates)); a `CanonicalAlias()` route never falls back.
- Under the router-wide `AliasBehavior.RedirectToCanonical`, an alias redirects to the canonical URL only when its values
  can build that URL. Otherwise (the alias has parameters the canonical template lacks or omits, or a constraint rejects a
  value) it renders in place, and a Debug event (1102) says so. A route that declares `CanonicalAlias()` is not relaxed: RT0015
  checks at compile time that its aliases name the canonical template's parameters, and a value its canonical template
  rejects fails the navigation instead of rendering the alias.
- In `Explain` output, an index route's `RouteCandidate.Template` is its parent's template (the index shares its
  parent's path); the route name tells the two apart. Beside the English verdicts each candidate carries its
  `CompiledRoute` (`Route`), `IsAlias`, a `RouteCandidateVerdict` (`Matched`, `LostOnPrecedence`, `SegmentMismatch`,
  `ConstraintFailed`) and, for a matching candidate that was not selected, `PrecedenceReason` (the first path position where
  the selected route ranks before it). Only routes whose first segment matches are candidates (`/nope` has none), and
  `Explain` takes an app-relative path: strip the base path, query and fragment first. `Matched` is the enum's value 0, so a
  `RouteCandidate` built by hand with the positional constructor reads as `Matched` even when `IsMatch` is false: read the
  candidates `Explain` returns (whose `Verdict`, `Route` and `PrecedenceReason` are always set) rather than building them.
- `Core/` must not reference Blazor or JS interop types; `ArchitectureTests` enforces it.

## Pathless groups and named outlets

**A group** shares a layout, a guard or a loader between routes that have no common path prefix, without adding a segment to their URLs:

```csharp
routes
    .Route("/", typeof(Home), name: "home")
    .Group(typeof(AccountLayout), account => account          // AccountLayout contains a <ModRouterOutlet />
        .Route("overview", typeof(AccountOverview), name: "account")   // /overview
        .Route("profile", typeof(Profile), name: "profile")            // /profile
        .Route("billing", typeof(Billing), name: "billing"))           // /billing
    .Guard<SignedInGuard>();                                  // runs for every route in the group
```

In markup, `<ModRouteEntry Group Component="typeof(AccountLayout)">` with nested entries. A group is a level of the chain (its layout component
renders, and the chain has one more level, so it holds one `ModRouterOutlet`), but it is never matched by itself, has no name, aliases or title
(RT0027), and breadcrumbs and titles pass over it. The children's templates are relative (no leading `/`), like any child's. An index route inside a
group is the default page of the group's parent path.

**A named outlet** shows a different component per route beside the page, such as a contextual sidebar or toolbar:

```razor
<aside><ModRouterOutlet Name="side"><EmptyContent>Nothing selected</EmptyContent></ModRouterOutlet></aside>
<main>@Body</main>
```

```csharp
.Route("/customers/{id:int}", typeof(CustomerPage), name: "customer").Slot("side", typeof(CustomerSidebar))
```

(or `[RouteSlot("side", typeof(CustomerSidebar))]` on a discovered page, or `Slots` on a `<ModRouteEntry>`). The outlet renders the slot of the
deepest matched level that declares that name (the name is case-insensitive), inside that level's loaded data, with its route parameters bound like
the page's; it renders `EmptyContent` when no level declares one, or when the state is an error or a not-authorized page. It takes no depth of the
chain, so it can sit next to the page's own outlet, and it works in the router's layout. Declaring a name twice on one level, or a component that
is not an `IComponent`, is RT0028. A slot is not part of the URL: a link cannot address "this sidebar with that page" independently, and two
slots do not navigate separately. URL-addressable parallel or intercepting routes (Angular's auxiliary routes, Next.js's `@slot` folders) are not supported.

## Using the router

```csharp
// Program.cs
builder.Services.AddModRouter(routes => routes
    .Route("/", typeof(Home), name: "home")
    .Route("/customers", typeof(CustomersPage), name: "customers", children: customers => customers
        .Index(typeof(CustomerList), name: "customer-list")
        .Route("{id:int}", typeof(CustomerDetails), name: "customer-details")));
```

```razor
@* App.razor: replaces <Router> *@
<ModRouter DefaultLayout="typeof(MainLayout)">
    <NotFound><p>Not found.</p></NotFound>
</ModRouter>
```

A route component places `<ModRouterOutlet />` where its child route should render (`EmptyContent` is shown when the
chain has no level at that depth). Navigate with `INavigationService` (`NavigateTo`, `NavigateToRoute("customer-details",
new { id = 42 })`, `GetRouteUrl`, or `TryGetRouteUrl`, which returns the generation error instead of throwing) or plain
links and `NavigationManager`; all of them pass through the same pipeline,
including a root-relative `NavigateTo("/customers/42")` (or a `GetRouteUrl` result such as `/myapp/customers/42`),
which is resolved against the base and prepared before it commits like any other navigation.

- **Mounting:** one root `ModRouter` per app scope; a second throws. It enables Blazor's link interception itself.
- **Scope:** `RouteStateStore` and `INavigationService` are registered per DI scope (the same scope as `ModRouter`).
  Code that runs in a different scope, for example `OwningComponentBase.ScopedServices` or an `IHttpClientFactory`
  message handler, resolves its own, empty store and never sees a committed state; resolve them from the component's
  own scope (`[Inject]`) instead.
- **Layouts:** `DefaultLayout`, or `@layout`/`[Layout]` on the depth-0 route component. `NotFound` renders inside the same
  layout.
- **Lifetime:** a parent route component is not recreated when only its child changes. A component whose route
  parameters change is updated in place (`SetParametersAsync`, `OnParametersSet`); `RouteDefinition.ReusePolicy` can
  recreate it per route, per parameter set, or always. Query-only navigation touches only components that declare query
  parameters. Blazor skips `SetParametersAsync` only for parameter types it knows to be immutable (numbers, strings,
  `Guid`, `DateTime`, enums): array-typed query values, and route parameters of type `DateTimeOffset` or of a custom
  constraint's value type, are treated as changed on every render, so a retained component bound to one receives
  `SetParametersAsync` on every navigation that re-renders its outlet.
- **Parameters:** a route component receives the route values of itself and its ancestors for the `[Parameter]`s it
  declares (case-insensitive, type-converted; numeric conversions widen only, so `2.5` never binds to an `int`). A
  parameter **the previous URL supplied and this one omits** (`/list/3` then `/list` with `{page:int?}`, or `/a/5` then a
  second route `/b` to the same component without `{id}`) returns to its type default on the reused instance, so it never
  keeps a stale value. A parameter no URL supplied is never touched: a property initializer (`[Parameter] public bool?
  DeleteFile { get; set; } = true;`) stays `true` at `/download/abc`, through a query-only navigation and when a child
  route opens in the parent's outlet. The caveat: a reset goes to the type default (`null` here), not back to the
  initializer. An optional
  parameter must bind to a nullable property. `[SupplyParameterFromQuery]` properties are query-only: Blazor's built-in provider supplies them,
  the router never binds them from the path. Impossible conversions (a `{id:int}` bound to a `Guid` property, a
  `{x:double}` bound to an `int`) fail at startup in `AddModRouter`; values that fail at runtime (a string that is not a
  number) render `NotFound` and log a warning.
- **Base path:** the core matches base-relative paths; `<base href="/myapp/">` just works, and `GetRouteUrl` returns a
  base-prefixed path for `href`s.
- **Navigation stages:** `INavigationStage` (registered in DI) runs after the built-in normalize, recognize, bind,
  leave-guard, redirect, guard and loader stages, in registration order. A stage is skipped when the navigation already failed (not
  found, binding failure) or recorded an error, unless it returns `true` from `RunsOnFailure`. A stage returns
  `StageResult.Continue`, `StageResult.Cancel` or `StageResult.Redirect(url)`. Breaking since slice 3a for stages
  written against slice 2: `StageResult` is a class, not an enum (returning `default` is a null reference, not
  `Continue`), `RouteNavigationContext.From` is nullable (null before the first commit) and `RouteNavigationContext.Services` is
  required. Breaking since slice 4b: the context is named `RouteNavigationContext` (it was `NavigationContext`, which is
  CS0104-ambiguous with `Microsoft.AspNetCore.Components.Routing.NavigationContext` in a file that imports both namespaces).
- **Verification:** `ModRouter.Sample` plus `tools/verify.mjs` check all of this in a real browser;
  bUnit alone cannot reproduce Blazor's navigation hooks.

