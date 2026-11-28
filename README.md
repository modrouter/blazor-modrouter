<p align="center"><img src="docs-site/images/logo-128.png" alt="ModRouter logo" width="96" height="96"></p>

# ModRouter

An alternative to the built-in Blazor `Router` with nested routes, named routes, reverse URL generation and a deterministic
precedence-based matcher. The route table is compiled once, precedence never depends on declaration order, and a table with
ambiguous or invalid routes fails at startup with a code and a message. A compatibility layer (a drop-in `Router`,
`RouteView` and `AuthorizeRouteView`) lets an app adopt it without rewriting its `App.razor` or its `@page` components.

[![NuGet](https://img.shields.io/nuget/vpre/ModRouter?label=ModRouter)](https://www.nuget.org/packages/ModRouter) [![NuGet](https://img.shields.io/nuget/vpre/ModRouter.Server?label=ModRouter.Server)](https://www.nuget.org/packages/ModRouter.Server)

**Which package?** One install per project, chosen by where the project runs:

| Your project | Install |
|---|---|
| Blazor WebAssembly app, or the `.Client` project of a Blazor Web App | `dotnet add package ModRouter --prerelease` |
| The server project of a Blazor Web App (Server, WebAssembly or Auto rendering, with or without prerendering) | `dotnet add package ModRouter.Server --prerelease` (includes `ModRouter`) |

Targets .NET 8, 9, 10 and 11. Read the [documentation site](https://modrouter.github.io/blazor-modrouter/) (guide and API reference), or start with the
[ModRouter README](ModRouter/README.md) for installation, a quick start and the documentation index.

## Repository layout

| Project | What it is |
|---|---|
| [`ModRouter`](ModRouter) | The router library, its [documentation](ModRouter/docs) and the [changelog](ModRouter/CHANGELOG.md) |
| [`ModRouter.Server`](ModRouter.Server) | The package for the server project of a Blazor Web App: includes `ModRouter` and sets the HTTP status of a server-rendered page |
| [`ModRouter.Generators`](ModRouter.Generators) | Source generator for typed route names and URL helpers |
| [`ModRouter.Test`](ModRouter.Test), [`ModRouter.Test.Fixtures`](ModRouter.Test.Fixtures) | The test suite and the pages it routes |
| [`ModRouter.Sample`](ModRouter.Sample), [`ModRouter.Sample.Extra`](ModRouter.Sample.Extra) | A Blazor WebAssembly demo app with browser checks, and a class library whose pages are found by reflection |
| [`ModRouter.Crm`](ModRouter.Crm) | A sample CRM in a browser-style frame with a guide beside it that turns on one router feature at a time, [live online](https://modrouter.github.io/blazor-modrouter/crm/) |
| [`ModRouter.Benchmarks`](ModRouter.Benchmarks) | BenchmarkDotNet benchmarks for matching, generation, `Explain` and compilation |
| [`docs-site`](docs-site) | DocFX configuration for the documentation site: the guide in `ModRouter/docs` plus the generated API reference |

## Build and test

```bash
dotnet test ModRouter.Test
dotnet run --project ModRouter.Sample --urls http://localhost:5199
```

See [Development](ModRouter/docs/development.md) for the sample's browser checks and the benchmarks.

## License

[Apache-2.0](LICENSE). Copyright (c) Paul Van Gundy.

## Author

Paul Van Gundy
