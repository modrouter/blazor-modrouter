# Guided lessons

Nine short lessons that add one ModRouter feature at a time, each with a live demo you can click and code you can change. Back to the [README](../README.md).

The lessons go past the basics. By the end you will have used nested routes, named and typed addresses, guards, loaders with caching, leave guards, groups and slots, kept-alive pages and the router's own flight recorder.

## Before you start

- Do [Getting started](getting-started.md) first if you have not used ModRouter, or skim [Concepts](concepts.md) for the vocabulary. Unknown words are in the [glossary](glossary.md).
- Every lesson has a live page in the **[interactive lab](https://modrouter.github.io/blazor-modrouter/lab/)**. The strip across the top of the lab shows what the router sees: the address, the levels that matched, and a light for each stage of the navigation. Watch it while you click.
- To change the code, run the lab from the repository:

```bash
git clone https://github.com/modrouter/blazor-modrouter.git
cd blazor-modrouter
dotnet run --project ModRouter.Lab --urls http://localhost:5301
```

## How each lesson works

1. **See it.** Open the live lesson and follow the numbered steps. No code yet.
2. **Read it.** The lesson shows the real code from the lab and explains the parts that matter.
3. **Change it.** A short exercise, "Your turn", asks you to change the lab and watch the result.

The code in the lessons is checked against the lab's source by the test suite, so it cannot drift away from what runs.

## The lessons

| Lesson | You learn to | Features |
|---|---|---|
| [1. Pages inside pages](lessons/01-pages-inside-pages.md) | Nest routes and see which parts of the screen survive a click | Child routes, index routes, outlets, route values |
| [2. Names instead of URLs](lessons/02-names-instead-of-urls.md) | Build addresses from names and let the compiler check them | Named routes, `[Route]` pages, typed helpers, optional values |
| [3. Which route wins?](lessons/03-which-route-wins.md) | Predict, and check, which route an address gets | Precedence, constraints, catch-all, `Explain` |
| [4. Gates](lessons/04-gates.md) | Stop, redirect or refuse a navigation, and read how it ended | Guards, redirect rules, `NavigateToAsync` |
| [5. Data in flight](lessons/05-data-in-flight.md) | Load data before the page shows, stream the slow parts, cache it | Loaders, deferred values, streams, `CacheFor`, preload |
| [6. Are you sure?](lessons/06-are-you-sure.md) | Ask before the visitor leaves unsaved work | Leave guards |
| [7. Shared layouts and side panels](lessons/07-shared-layouts-and-side-panels.md) | Share a layout across unrelated paths and fill a side panel from the route | Groups, named outlets, slots |
| [8. Pages that stay alive](lessons/08-pages-that-stay-alive.md) | Keep a page's state while the visitor is elsewhere | Keep-alive, view transitions |
| [9. The flight recorder](lessons/09-the-flight-recorder.md) | Read back what the router did and how long each step took | Diagnostics history |

Each lesson uses what the earlier ones introduced, so follow them in order the first time.
