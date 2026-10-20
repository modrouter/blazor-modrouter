# Hosting

Render modes, prerendering, the HTTP status of a server-rendered page and carrying loader data to the interactive pass. Back to the [README](../README.md).

## Render modes

| Host | Status |
|---|---|
| Blazor WebAssembly (standalone or hosted) | The main target; everything in the other pages applies. |
| Blazor Web App, interactive Server **without** prerendering | Works unchanged. Checked in a browser on a throw-away host: link interception, back and forward, a route declared only in the fluent map and the router's not-found content all work inside the circuit. |
| Blazor Web App, interactive Server **with** prerendering (static SSR, then interactive) | Supported with the pieces below: `ModRouter.AspNetCore` for the status code, `PersistLoaderData` for loader data, `[Route]` or `@page` on every routed component. Checked on the same host. |
| Interactive WebAssembly and Auto render modes in a Blazor Web App | Supported; see [Auto and WebAssembly](#auto-and-webassembly). Checked in a browser on a throw-away Web App (global `InteractiveAuto`): the first visit runs in the circuit, later visits in WebAssembly, and both behaved the same. |
| Blazor Hybrid | Not handled. |

What makes a server host safe: the route table holder (`Freeze`) takes a lock and publishes the table with a volatile write, the
parameter binder's cache is a `ConcurrentDictionary`, and everything else the router keeps (`RouteStateStore`, the coordinator,
`INavigationService`) is scoped, so one circuit never sees another's state.

## Prerendering: three requirements

**1. Every routed component needs an endpoint.** A Blazor Web App answers the first request of a URL through endpoint routing,
and `MapRazorComponents` only creates endpoints for components with a `[Route]` (what `@page` generates). A route declared only
in the fluent map (`.Route("/fluent", typeof(Page))` on a component without `@page`) works for in-app navigation once the
circuit or the WebAssembly runtime is running, but a direct request for that URL is answered by the framework with a 404 before
the router renders. Put `@page` (or `[Route]`) on the component, and `AddAdditionalAssemblies` on `MapRazorComponents` for
components in other assemblies.

**2. Use fluent or `@page` routes, not `<Routes>` markup.** The markup entries are compiled after the router's first render, and
prerendering never runs that step, so a prerendered page would show `Loading` and nothing else (see [route sources](route-sources.md#the-route-table-holder)).

**3. A static render cannot hold a navigation lock.** The router registers a location-changing handler when it mounts; the
framework's static-render `NavigationManager` rejects that. The router catches it and carries on without the handler: nothing
can navigate during a static render, so there is nothing to intercept. The interactive pass registers it as usual.

## Auto and WebAssembly

In a Blazor Web App with `InteractiveAuto` (or `InteractiveWebAssembly`) the routed components, the loaders, the guards and the route table live in the
`.Client` project, and both containers register the same table:

```csharp
// AutoApp.Client: AppRoutes.cs
public static class AppRoutes { public static void Configure(RouteMap routes) => routes.Route(...); }

// AutoApp.Client/Program.cs                         // AutoApp/Program.cs
builder.Services.AddModRouter(AppRoutes.Configure);  builder.Services.AddModRouter(AppRoutes.Configure);
                                                      builder.Services.AddModRouterHttpStatus();   // server only
```

`Routes.razor` (in the client project, rendered with `@rendermode="InteractiveAuto"`) holds the `<ModRouter>`; every routed component carries `@page` or `[Route]`
for the first request's endpoint (see above). What was checked, in a cold browser profile and again after the runtime was cached:

- A direct request for a page answers 200 from the prerender, a guard redirect answers 302 (`/secret` to `/about`) and an unmatched URL answers 404 with the
  router's not-found content once the client takes over.
- With `PersistLoaderData` the loader ran once, on the server, during the prerender; the circuit and the WebAssembly runtime both showed that data
  without running it again. A navigation inside the page runs the loader in whichever container is interactive (`OperatingSystem.IsBrowser()` told them apart).
- Link clicks, a guard redirect, back and an unmatched in-app URL behaved identically in the circuit and in WebAssembly.

Loader and guard classes are created in the client container too, so they can use only services registered there, and persisted loader data must
serialize with `System.Text.Json` on both sides. A loader that needs a server-only service belongs behind an HTTP call the client can make.

## The HTTP status of a server-rendered page

The router tells an optional `IRouteStatusSink` what each committed state means, and `ModRouter.AspNetCore` provides the sink
that sets the response status:

```csharp
// Program.cs of the server project
builder.Services.AddModRouter(routes => ...);
builder.Services.AddModRouterHttpStatus();   // ModRouter.AspNetCore

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
```

| The committed state | Status | Body |
|---|---|---|
| A normal page | 200 | the page |
| No route matched, a malformed path or values that cannot bind | 404 | see below |
| A guard denied a level | 401 for an anonymous user, else 403 | the router's not-authorized content |
| A guard or a loader threw, the pipeline failed or redirects looped | 500 | the router's error content |
| A guard redirected | 302 to the target | none |

- **404.** The framework sends an empty body when a component sets 404 during the render, so that the status code pages
  middleware can render the host's own page; `UseStatusCodePagesWithReExecute("/not-found")` (the Blazor Web App template's
  default) does that. Declare the `/not-found` page in the router's table as well, or the re-executed request would itself
  be a not-found.
- **302.** The redirect becomes a real HTTP redirect on every supported framework. When the framework's `NavigateTo` throws
  `NavigationException` to end the request (.NET 8 and 9, and .NET 10 unless `BlazorDisableThrowNavigationException` is true, which is
  the .NET 10 template's default), the router has already recorded the redirect and lets the exception reach the framework instead of
  treating it as a failed navigation.
- **Nothing is set** once the response has started (a streamed page) or when there is no request (an interactive circuit after
  the prerender, WebAssembly).

A host that wants a different mapping registers its own `IRouteStatusSink`; the router calls it for every committed state that is
not a normal page, with `RouteStatusKind.NotFound`, `NotAuthorized` or `Failed`.

## Loader data across the prerender

A prerendered page runs its loaders on the server and again when the circuit or the runtime takes over, which flashes the page
and fetches twice. A level that declares `PersistLoaderData` stores its loader's critical data in the persistent component
state during the prerender and takes it back on the interactive pass, so the loader runs once:

```csharp
routes.Route("/customer/{id:int}", typeof(CustomerPage), name: "customer")
      .Loader<CustomerLoader>()
      .PersistLoaderData<Customer>();
```

or on the page: `[RouteLoader<CustomerLoader>] [RoutePersistLoaderData<Customer>]`.

- The data is stored as JSON (the persistent component state's own serializer), keyed by the level and the URL (path and
  query); it is used for the first navigation to that URL and taken once.
- It must be plain critical data. A level whose loader also returns deferred values or streams (live tasks that cannot travel)
  is not persisted and loads again.
- The route needs a loader (RT0025 otherwise), and `PersistLoaderData` can be declared once per route.
- The markup `<ModRouteEntry>` has no `PersistLoaderData` parameter; use the fluent or the attribute form.
- Checked on the throw-away host: with `PersistLoaderData` a page loaded once on the server and showed the same data after the
  circuit started; without it the loader ran a second time.
