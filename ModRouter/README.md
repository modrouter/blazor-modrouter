# ModRouter

An alternative to the built-in Blazor `Router` with nested routes, named routes, reverse URL generation and a deterministic
precedence-based matcher: the route table is compiled once, precedence never depends on declaration order, and a table with
ambiguous or invalid routes fails at startup with a code and a message. It also ships a compatibility layer (a drop-in `Router`,
`RouteView` and `AuthorizeRouteView`), so an app can adopt it without rewriting its `App.razor` or its `@page` components.
It is for Blazor WebAssembly apps that need nested layouts, guards and loaders, named routes or readable routing
diagnostics. The router needs nothing but Blazor.

## Install and register

Reference the project and register the router in `Program.cs`:

```xml
<ProjectReference Include="..\ModRouter\ModRouter.csproj" />
```

```csharp
// Program.cs
builder.Services.AddModRouter(routes => routes
    .Route("/", typeof(Home), name: "home")
    .Route("/customers", typeof(CustomersPage), name: "customers", children: customers => customers
        .Index(typeof(CustomerList), name: "customer-list")
        .Route("{id:int}", typeof(CustomerDetails), name: "customer-details")));
```

## Quick start

```razor
@* App.razor: replaces <Router> *@
<ModRouter DefaultLayout="typeof(MainLayout)">
    <NotFound><p>Not found.</p></NotFound>
</ModRouter>
```

A route component places `<ModRouterOutlet />` where its child route should render. Navigate with `INavigationService`
(`NavigateTo`, `NavigateToRoute("customer-details", new { id = 42 })`, `GetRouteUrl`) or with plain links and `NavigationManager`:
all of them pass through the same pipeline. Routes can also come from `@page` components (`AddPages`, with the `Route*`
attributes) or from markup (`<ModRouter><Routes>`). To keep an app that already uses the built-in `Router`, swap its tag for the
compatibility `Router`.

## Documentation

| Page | What it covers |
|---|---|
| [Routing](docs/routing.md) | Templates, constraints, precedence, the compiled table, matching, URL generation, `Explain` and how the router renders a chain |
| [Navigation](docs/navigation.md) | Guards, loaders, redirects and error states; route links, breadcrumbs, the document title, focus, scroll and announcements |
| [Route sources](docs/route-sources.md) | `@page` discovery and the `Route*` attributes, the `<Routes>` markup facade, the route table holder, canonical templates and trimming |
| [Typed routes](docs/typed-routes.md) | The source generator that turns `[Route]` and `[RouteName]` into route-name constants and typed `Get...Url` and `NavigateTo...Async` helpers |
| [Authorization](docs/authorization.md) | `[Authorize]` and `[AllowAnonymous]` as a route guard, re-checked when the user signs in or out |
| [Compatibility](docs/compatibility.md) | Using the built-in `Router`, `RouteView` and `AuthorizeRouteView` shapes, and the compatibility matrix with the test behind every row |
| [Hosting](docs/hosting.md) | Render modes, prerendering, the HTTP status of a server-rendered page, carrying loader data to the interactive pass, the endpoint requirement |
| [Observability](docs/observability.md) | Log events, tracing, the navigation history and the outlet warning |
| [Configuration](docs/configuration.md) | `ModRouterOptions`, the options `AddModRouter` takes (the router reads no `appsettings` keys) |
| [Development](docs/development.md) | Running the tests, the sample and its browser checks, and the benchmarks |
| [Performance](docs/performance.md) | What matching, generation, `Explain` and compilation cost, measured over 10 to 10,000 routes |
| [Versioning](docs/versioning.md) | Semantic versioning, what counts as public API, deprecation, supported frameworks and how a release is cut |
| [Known limitations](docs/known-limitations.md) | Every open question from the slice reviews, each marked fixed, accepted or deferred, and the public API surface |

## Requirements

- .NET 8, 9 or 10: `ModRouter` and `ModRouter.AspNetCore` target all three (`net8.0;net9.0;net10.0`) and the test suite runs on each. Differences by framework: on .NET 8 a failed navigation's exception is recorded as an `exception` event on its activity rather than with `Activity.AddException`, and `[ExcludeFromInteractiveRouting]` is only honored from .NET 9 (the attribute does not exist before). Blazor WebAssembly is the main target; a Blazor Web App (interactive Server, WebAssembly or Auto, with or without prerendering) works too, see [Hosting](docs/hosting.md). Blazor Hybrid is not handled.
- Nothing else: `RouteLink` and `RouteBreadcrumb` are plain markup.
- Trimming: the default WebAssembly publish needs nothing, but an assembly that opts into trimming and holds pages found only by
  reflection must be rooted in the app project (see [Trimming](docs/route-sources.md#trimming)).

## Status

Slices 1 to 4d are built: the Blazor-free core engine, the navigation pipeline and root components, guards, loaders and
redirects, links, breadcrumbs, title, focus and scroll, route sources, authorization, the compatibility layer, observability, and
performance and documentation. What is still open, and what was decided not to change, is on the
[known limitations](docs/known-limitations.md) page.

## Develop

```bash
dotnet test ModRouter.Test
dotnet run --project ModRouter.Sample --urls http://localhost:5199
dotnet run -c Release --project ModRouter.Benchmarks -- --filter "*MatchBenchmarks*" --job short
```

The suite, the sample's browser checks and the benchmarks are described in [Development](docs/development.md); the figures are on
the [performance](docs/performance.md) page.

## Author

Paul Van Gundy

## License

Apache-2.0, see [LICENSE](../LICENSE).
