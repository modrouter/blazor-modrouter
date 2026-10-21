<p align="center"><img src="docs-site/images/logo-128.png" alt="ModRouter logo" width="96" height="96"></p>

# ModRouter

An alternative to the built-in Blazor `Router` with nested routes, named routes, reverse URL generation and a deterministic
precedence-based matcher. The route table is compiled once, precedence never depends on declaration order, and a table with
ambiguous or invalid routes fails at startup with a code and a message. A compatibility layer (a drop-in `Router`,
`RouteView` and `AuthorizeRouteView`) lets an app adopt it without rewriting its `App.razor` or its `@page` components.

Targets .NET 8, 9 and 10. Read the [documentation site](https://modrouter.github.io/blazor-modrouter/) (guide and API reference), or start with the
[ModRouter README](ModRouter/README.md) for installation, a quick start and the documentation index.

## Repository layout

| Project | What it is |
|---|---|
| [`ModRouter`](ModRouter) | The router library, its [documentation](ModRouter/docs) and the [changelog](ModRouter/CHANGELOG.md) |
| [`ModRouter.AspNetCore`](ModRouter.AspNetCore) | ASP.NET Core adapter: the HTTP status of a server-rendered page |
| [`ModRouter.Generators`](ModRouter.Generators) | Source generator for typed route names and URL helpers |
| [`ModRouter.Test`](ModRouter.Test), [`ModRouter.Test.Fixtures`](ModRouter.Test.Fixtures) | The test suite and the pages it routes |
| [`ModRouter.Sample`](ModRouter.Sample), [`ModRouter.Sample.Extra`](ModRouter.Sample.Extra) | A Blazor WebAssembly demo app with browser checks, and a class library whose pages are found by reflection |
| [`ModRouter.Lab`](ModRouter.Lab) | An interactive Blazor WebAssembly app with nine guided lessons, [live online](https://modrouter.github.io/blazor-modrouter/lab/); the lessons are written up in the [guide](ModRouter/docs/lessons.md) |
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
