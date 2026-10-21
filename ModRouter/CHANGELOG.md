# Changelog

All notable changes to ModRouter and ModRouter.AspNetCore. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the
versions follow [Semantic Versioning](https://semver.org); see [Versioning](docs/versioning.md) for what counts as a breaking change.

## Unreleased

## 0.1.0-preview.1

The first packaged preview. Until 1.0.0 a minor version may break the public API.

### Added

- **Routing core.** A compiled, precedence-based route table (literals, constrained and plain parameters, optional parameters, catch-alls, aliases, canonical
  aliases), nested routes with index routes, named routes with reverse URL generation, `Explain`, and startup errors with stable `RT####` codes for an invalid or
  ambiguous table. The core has no Blazor dependency.
- **Router.** `ModRouter` and `ModRouterOutlet` with layouts, loading, not-found and error content, a navigation indicator, `RouteLink`, `RouteBreadcrumb`,
  `ModDeferred` and `ModStream`, and a compatibility layer (a drop-in `Router`, `RouteView` and `AuthorizeRouteView`).
- **Navigation pipeline.** Route guards, loaders (critical, deferred and streamed data), redirects, error states, `[Authorize]` and `[AllowAnonymous]` as a guard
  that re-checks on sign-in and sign-out, route-declared leave guards, a component-level navigation lock, awaitable navigation results
  (`NavigateToAsync`, `NavigateToRouteAsync`) and history state.
- **Loader cache.** `CacheFor` with stale-while-revalidate, `RevalidateAsync`, `PreloadAsync`, `ClearLoaderCache` and `Preload` on the route links.
- **Layout.** Pathless groups (`Group`), named outlets filled by route slots (`Slot`, `[RouteSlot]`), keep-alive pages (`KeepAlive`, `[RouteKeepAlive]`,
  `IKeepAliveRoute`) and optional view transitions between pages.
- **Browser effects.** The document title, a screen-reader announcement, focus, scroll to the top or a fragment and scroll restoration on back and forward.
- **Route sources.** Fluent maps, `@page` discovery with the `Route*` attributes, and `<Routes>` markup entries.
- **Hosting.** Blazor WebAssembly, and Blazor Web App with interactive Server, WebAssembly or Auto rendering, with or without prerendering; the HTTP status of a
  server-rendered page (`ModRouter.AspNetCore`) and loader data carried across the prerender (`PersistLoaderData`).
- **Typed routes.** A source generator, shipped inside the package, that turns `[Route]` and `[RouteName]` into `RouteNames` constants and typed
  `Get...Url` and `NavigateTo...Async` helpers (`MRG001`, `MRG002`). The attributes can be written in C# or as `@page` and
  `@attribute [RouteName("...")]` in a `.razor` file; the generator reads the directives from the markup, because it cannot see the attributes Razor's own
  generator produces from them.
- **Observability.** Log events, `ActivitySource` tracing, a navigation history and the outlet warning.
- **Targets.** `net8.0`, `net9.0` and `net10.0`.
