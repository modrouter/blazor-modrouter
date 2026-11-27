# Guided lessons

Nine short lessons that add one ModRouter feature at a time. They follow the chapters of the **[sample CRM](https://modrouter.github.io/blazor-modrouter/crm/)**, a small CRM in a browser-style frame, and quote its code. Back to the [README](../README.md).

By the end you will have used a route table, parameters, nested routes, the query string, guards, loaders with caching, leave guards, named links with redirects and the router's own route log.

## Before you start

- Do [Getting started](getting-started.md) first if you have not used ModRouter, or skim [Concepts](concepts.md) for the vocabulary. Unknown words are in the [glossary](glossary.md).
- Open the **[sample CRM](https://modrouter.github.io/blazor-modrouter/crm/)**. The guide on the right has the same eight chapters as lessons 1 to 8. The **route log** under it shows what the router did with every click: the stages, how each ended and how long it took. Watch it while you click.
- To change the code, run the CRM from the repository:

```bash
git clone https://github.com/modrouter/blazor-modrouter.git
cd blazor-modrouter
dotnet run --project ModRouter.Crm --urls http://localhost:5401
```

## How each lesson works

1. **See it.** Open the matching chapter of the CRM and follow the numbered steps. No code yet.
2. **Read it.** The lesson shows the real code from the CRM and explains the parts that matter.
3. **Change it.** A short exercise, "Your turn", asks you to change the CRM and watch the result.

The code in the lessons is checked against the CRM's source by the test suite, so it cannot drift away from what runs.

## The lessons

| Lesson | CRM chapter | You learn to | Features |
|---|---|---|---|
| [1. A URL is a screen](lessons/01-a-url-is-a-screen.md) | 1 | Declare a route table and give every screen an address | Routes, groups, layouts, startup checks |
| [2. Parameters](lessons/02-parameters.md) | 2 | Turn part of an address into typed data | `{id:int}`, `[Parameter]`, links by name, precedence |
| [3. Nested routes](lessons/03-nested-routes.md) | 3 | Put a page inside a page and see what survives a click | Child routes, index routes, outlets, the chain |
| [4. The query string](lessons/04-the-query-string.md) | 4 | Keep shareable state in the address | `SupplyParameterFromQuery`, links with a query |
| [5. Guards](lessons/05-guards.md) | 5 | Stop, redirect or refuse a navigation, and read how it ended | Guards, return addresses, `NavigateToAsync` |
| [6. Loaders](lessons/06-loaders.md) | 6 | Load data before the page shows, defer the slow part, cache it | Loaders, `CacheFor`, deferred values, errors |
| [7. Leave guards](lessons/07-leave-guards.md) | 7 | Ask before the visitor leaves unsaved work | Leave guards, `IRouteNavigationLock` |
| [8. Names and redirects](lessons/08-names-and-redirects.md) | 8 | Link by name and forward old addresses | Named routes, redirect rules, breadcrumb |
| [9. The route log](lessons/09-the-route-log.md) | all | Read back what the router did and how long each step took | Diagnostics history |

Each lesson uses what the earlier ones introduced, so follow them in order the first time.

## Beyond the CRM

Three features have no chapter in the CRM. They are covered in the reference pages:

- **Slots and named outlets**: a layout with a second place a route renders into. See [Routing](routing.md#pathless-groups-and-named-outlets).
- **Kept-alive pages and view transitions**: see [Navigation](navigation.md#keeping-pages-alive).
- **Typed route helpers**: generated methods the compiler checks. See [Typed routes](typed-routes.md).
