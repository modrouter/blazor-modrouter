# Why ModRouter

What ModRouter adds to Blazor's built-in `Router`, what it costs, and when to keep the built-in one. Back to the [README](../README.md).

## The short version

Blazor's built-in `Router` does one job well: it finds the `@page` component for an address and shows it. Everything else around a navigation (who may enter, what data the page needs, whether to ask before leaving, how to build a link without typing a URL) is left to you, so each app solves it again.

ModRouter makes those parts first-class and puts them in one place, the route, so a route says everything about its page: where it lives, what it is called, who may open it, what it loads and how it nests.

## Side by side

The built-in column describes the `Router` in .NET 8 to 10.

| You want | Built-in `Router` | ModRouter |
|---|---|---|
| A child page that shows inside its parent's page | Pages are flat; sharing a frame means choosing a layout per page | Nested routes with outlets, and the parent stays on screen when only the child changes |
| A link built from a route's name and values | You write the address yourself | Named routes, `GetRouteUrl`, `RouteLink`, and typed helpers the compiler checks |
| To know why this route matched | You read the templates and think | `Explain` lists every candidate, its verdict and why a match lost |
| An unclear route table caught early | You find out when the address is visited | A table with routes that cannot be told apart fails at startup with a code and a message |
| A check before a page appears | `AuthorizeRouteView` for sign-in; anything else is code in the page | Guards: allow, cancel, redirect or "not authorized", per route level |
| Data ready before the page shows | The page appears first and loads in `OnInitializedAsync` | Loaders: the old page stays until the data is ready, with deferred values, streams and a cache |
| A question before leaving unsaved work | `NavigationLock` in the page | Leave guards declared on the route, plus a lock a page can implement itself |
| A redirect from an old address | A page whose only job is to navigate | A redirect rule, no component |
| A layout shared by routes with different paths | Repeat `@layout` on each page | A group |
| A page that keeps its state while the visitor is elsewhere | Not available | `KeepAlive` |
| What the router just did | Not available | A history of navigations with the time each stage took, log events and tracing |

## What stays the same

ModRouter replaces only the `Router` component. You keep your `@page` components, `@layout`, `NavLink`, `NavigationManager`, `[SupplyParameterFromQuery]` and `NavigationLock`. A compatibility layer accepts the built-in `Router`, `RouteView` and `AuthorizeRouteView` shapes, so an existing app can swap the four tag names in `App.razor` and change nothing else. See [Compatibility](compatibility.md).

## A way in that does not need a rewrite

1. Swap the router with the compatibility layer, or follow [Getting started](getting-started.md) and call `AddPages`. Your app behaves as before.
2. Name a few routes and replace hand-typed addresses with `RouteLink` and `NavigateToRoute`.
3. Add a guard where you repeat a check. Add a loader where you load in `OnInitializedAsync`.
4. Use the rest as you need it: leave guards, groups, caching, keep-alive.

Each step stands alone. You can stop after any of them.

## When to keep the built-in router

- Your app has a handful of pages, no guards, no shared data loading and no nested pages. The built-in router is enough, and one less moving part is a real benefit.
- You build a Blazor Hybrid app. ModRouter does not handle it.
- You cannot take a preview dependency. ModRouter is at `0.1.0-preview.1`: the API can still change before 1.0 (see [Versioning](versioning.md)), and it is published as a pre-release package, which some tools only show when you ask for pre-releases.

## What it costs

- **A new set of ideas** to learn: routes with children, outlets, guards, loaders. The [guided lessons](lessons.md) exist to make that quick.
- **One router per app.** ModRouter replaces the built-in `Router`; you cannot mount both.
- **Some things are deliberately not supported**, such as address-addressable parallel routes. The [known limitations](known-limitations.md) page lists them with the reasoning.

## Where to go next

Read [Concepts](concepts.md) for the picture, or open the [interactive lab](https://modrouter.github.io/blazor-modrouter/lab/) and start the first lesson.
