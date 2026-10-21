# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

ModRouter is a replacement for the built-in Blazor `Router` (nested and named routes, a deterministic matcher, guards, loaders, typed routes, a compatibility layer). The projects sit side by side at the repository root; there is no solution file for the library, so build and test by project path. Author and copyright: Paul Van Gundy; Apache-2.0. Documentation is in `ModRouter/docs/` (start with `ModRouter/README.md`).

## Commands

```bash
dotnet test ModRouter.Test                                    # all three target frameworks (net8.0, net9.0, net10.0)
dotnet test ModRouter.Test -f net10.0 --filter "FullyQualifiedName~CoordinatorTests"          # one fixture on one framework
dotnet test ModRouter.Test -f net10.0 --filter "Name~A_throwing_subscriber"                    # one test by name
MODROUTER_TEST_TELEMETRY=listener dotnet test ModRouter.Test   # also run in `history` mode; both must pass like the plain run
dotnet run --project ModRouter.Sample --urls http://localhost:5199
dotnet run --project ModRouter.Lab --urls http://localhost:5301   # the interactive lessons; also published to GitHub Pages under /lab/
node ModRouter.Sample/tools/verify-all.mjs http://localhost:5199/  # all browser checks; need Node 22+, Chrome and the running sample
dotnet run -c Release --project ModRouter.Benchmarks -- --filter "*MatchBenchmarks*" --job short
dotnet tool restore && dotnet docfx docs-site/docfx.json      # builds the docs site into docs-site/_site (guide + generated API reference)
```

- `TreatWarningsAsErrors` is on in the library projects, and `GenerateDocumentationFile` is on, so a public member without an XML comment fails the build (CS1591). CA1848/CA1873 are errors for logging (see below).
- `ModRouter.Sample/tools/verify-*.mjs` are the only checks of real-browser behavior (Blazor navigation hooks, focus, scroll, trimming); bUnit cannot reproduce them. They are not part of `dotnet test`; CI's `browser` job publishes the sample trimmed, serves it with `tools/serve.mjs` and runs `verify-all.mjs`, so a browser regression fails the build.
- CI (`.github/workflows/ci.yml`) builds and tests each framework, then packs `ModRouter` and `ModRouter.AspNetCore` and checks the package contents. `.github/workflows/docs.yml` builds the DocFX site and deploys it to GitHub Pages from `main`. The site reuses `ModRouter/docs/*.md` in place (`docs-site/` holds only `docfx.json`, the tocs, the logo and the landing page); a new doc page needs an entry in `docs-site/ModRouter/toc.yml` (the guide sidebar) as well as a README row.

## Architecture

**Layers.** `ModRouter/Core/` is pure and must stay free of Blazor and JS interop: template parser → `RouteMap` (fluent definitions) → `RouteCompiler` → immutable `CompiledRouteTable` (a trie; `Match`, `GenerateUrl`, `Explain`). Everything Blazor-specific lives in `Navigation/` (the pipeline), `Components/` (`ModRouter`, outlets, `RouteLink`, `RouteBreadcrumb`, loading and stream components), `Authorization/`, `Compatibility/` and `Discovery/`. `ModRouter/wwwroot/mod-router.js` is the browser half (title, focus, scroll restore). `ModRouter.Generators` is a netstandard2.0 Roslyn source generator that ships inside the `ModRouter` package as an analyzer (typed route names and `Get...Url`/`NavigateTo...Async` helpers); `ModRouter.AspNetCore` adds HTTP status for server-rendered pages.

**Navigation pipeline.** `NavigationCoordinator` (internal, ~1,400 lines) owns navigation. It runs each navigation through an ordered `INavigationStage` list while the platform holds the navigation: Normalize → Recognize → Bind → LeaveGuard → Redirect → Guard → Loader → any `additionalStages`. Commit is two-phase: the prepared `RouteState` is committed only when the platform reports the location change, and a newer navigation supersedes an unfinished one so a stale result can never commit. A stage returns `StageResult.Continue`, `Cancel` or `Redirect(url)`. `INavigationPlatformAdapter` is the seam over Blazor's `NavigationManager` (`BlazorNavigationAdapter`); tests use `FakeNavigationAdapter`. `RouteStateStore` holds committed state and outlives a router, so a remounted router must not trust the previous router's state (`_committedSinceAttach`).

**Where routes come from.** Four sources feed the same compiler: the fluent `RouteMap` in `AddModRouter`, `@page` components via `Discovery/` (`AddPages`, the `Route*` attributes), markup (`<ModRouter><Routes>` entries, collected by `RouteEntryCollector`), and the compatibility `Router`, which discovers at its first navigation. `RouteTableHolder` serves the table and is frozen by `ModRouter` before its first navigation. Page discovery is the only code allowed to enumerate assembly types (trimming).

**Compatibility layer.** `Compatibility/` provides a drop-in `Router`, `RouteView` and `AuthorizeRouteView` shaped like the framework's, so an app swaps only the tag names. `docs/compatibility.md` has the matrix; `CompatibilityMatrixTests` checks that every supported row names an existing test.

## Rules enforced by tests

`ArchitectureTests` pins several rules, so a violation fails the suite rather than review:

- `Core/` references nothing from `Microsoft.AspNetCore.Components` or `Microsoft.JSInterop`; only `Authorization/` and `Compatibility/` may use the authorization packages.
- The public surface is exactly the reviewed list (`The_public_surface_is_exactly_the_reviewed_list`): making a type public, or internal, means editing `ReviewedPublicTypes` (sorted ordinally) and documenting it.
- All log entries go through `Diagnostics/RouterLog` (`[LoggerMessage]`, stable EventIds). The `docs/observability.md` log-event table is compared with the code (`RouterLogTests`), so a new event needs a table row.
- Docs are tested: `DocumentationLinkTests` checks that relative links and anchors resolve, that `ModRouter/README.md` lists every page in `docs/`, and that no heading keeps a "slice" label. Renaming a heading, adding a page or moving one can fail the suite.
- Error codes (`RT####`, `MRG###`) are stable and never reused; message text is not API.

## Conventions

- `ModRouter.Lab` is the teaching app behind `ModRouter/docs/lessons/`. The lessons quote its code in fenced blocks whose first line names the file (`// ModRouter.Lab/LabRoutes.cs`), and `LabSnippetTests` fails when a quoted line is not in that file: change the lab and the lesson together. The lab's design lives in `wwwroot/css` (violet brand, amber signal, mint pass, rose stop); keep its user-facing words plain, short and active.

- Every `.razor` component must have a matching `.razor.cs` code-behind (`Foo.razor` + `Foo.razor.cs`) that declares its public surface: parameters, injected services, public methods and their XML comments. Keep logic out of `@code` blocks. The API reference is generated by DocFX from the C# sources and cannot load the Razor compiler, so a public member declared only in a `.razor` file is missing from the published API docs.

- Tests are NUnit with bUnit for components; shared helpers are `PipelineKit.cs`, `RouteTestKit` and `ComponentTestKit`. Test names are sentence-style (`A_stage_that_throws_prevents_the_navigation`).
- Keep the two telemetry modes (`listener`, `history`) in mind when adding activities: in listener mode a started activity that is never stopped fails the run.
- Documented limitations and the reasoning behind choices that look like gaps are in `ModRouter/docs/known-limitations.md`; check it before "fixing" surprising behavior.
