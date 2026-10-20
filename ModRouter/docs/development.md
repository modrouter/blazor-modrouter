# Development

Running the tests, the sample, its browser checks and the benchmarks. Back to the [README](../README.md).

## Tests

```bash
dotnet test ModRouter.Test
```

The differential test (`DifferentialTests`) compares the trie against an independent brute-force evaluator on random
route tables; the round-trip test (`RoundTripTests`) checks `GenerateUrl` against `Match`, and
`UrlGenerationFallbackTests` that the template fallback never changes a canonical generation. `PageDiscoveryTests`
use a fixture namespace of page components (`DiscoveryFixtures.cs`, one sub-namespace per mistake), `MarkupRoutesTests`
mount `ModRouter` with `<Routes>` in bUnit, and `ArchitectureTests` keep `Core/` Blazor-free, `Discovery/` the only code
that enumerates assembly types, the authorization packages inside `Authorization/` and `Compatibility/`, and the public
surface listed. `AuthorizationDifferentialTests` compares the guard with the built-in `AuthorizeRouteView` cell by cell and
`BuiltInConstraintParityTests` the constraints with the built-in router's; `CompatibilityRouterTests` run a typical-site-shaped
`App` over the compatibility layer (its pages live in `ModRouter.Test.Fixtures`, an assembly whose every
page is valid), and `CompatibilityMatrixTests` reads the [compatibility matrix](compatibility.md#compatibility-matrix) and checks that every *supported* row names a test that
exists. `RouterLogTests` check each log event's id, name, level and placeholders and compare the [Log events](observability.md#log-events) table with
the code, `TelemetryTests` the activities and records of every kind of navigation, `RouterDiagnosticsTests` the history,
`OutletWarningTests` the outlet warning's probe table in both render modes, `ExplainAdditionsTests` the structured `Explain`
candidates and `TryGetRouteUrl`. The sample's `tools/verify-modes.mjs` checks the pages and markup modes, `tools/verify-compat.mjs`
and the compat mode, in a real browser.

The whole suite also runs in two telemetry modes, which must pass like the plain run (a few tests about one mode only are
reported as inconclusive in the other):

```bash
MODROUTER_TEST_TELEMETRY=listener dotnet test ModRouter.Test   # an ActivityListener samples every router activity
MODROUTER_TEST_TELEMETRY=history dotnet test ModRouter.Test    # every coordinator keeps a history of 50
```

In the listener mode the run fails (`TearDown failed for test fixture ... TelemetryModeSetup`, exit code 1) when an activity the
suite started was never stopped.

## The sample, the browser checks and the benchmarks

`ModRouter.Sample` is a standalone Blazor WebAssembly app that exercises every feature in a real browser (it is not deployed). Run it, and see its pages, modes and checks, in its [README](../../ModRouter.Sample/README.md):

```bash
dotnet run --project ModRouter.Sample --urls http://localhost:5199
```

bUnit cannot reproduce Blazor's navigation hooks, so four Node scripts in the sample's `tools/` folder (Node 22+ and Chrome) check the running sample. Each takes the URL of the running sample:

```bash
node ModRouter.Sample/tools/verify.mjs http://localhost:5199/                # navigation, guards, loaders, links, title, focus and scroll
node ModRouter.Sample/tools/verify-modes.mjs http://localhost:5199/ all     # the pages and markup modes (or: markup | pages)
node ModRouter.Sample/tools/verify-compat.mjs http://localhost:5199/        # the compatibility layer
```

The benchmarks (`ModRouter.Benchmarks`, BenchmarkDotNet) measure matching, generation, `Explain` and compilation through the public API over synthetic tables of 10 to 10,000 routes. Run one class at a time, in Release:

```bash
dotnet run -c Release --project ModRouter.Benchmarks -- --filter "*MatchBenchmarks*" --job short
```

The other classes are `GenerationBenchmarks`, `ExplainBenchmarks` and `CompileBenchmarks`. The figures and how to read them are on the [performance](performance.md) page; `MatchAllocationTests` in the test project enforces a per-match allocation budget on every `dotnet test` run.
