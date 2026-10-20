# Performance

What the router costs, measured through its public API. Back to the [README](../README.md).

## What is measured

The benchmark project (`ModRouter.Benchmarks`, BenchmarkDotNet) measures four
operations over synthetic route tables of 10, 100, 1,000 and 10,000 routes (a root, a catch-all, an optional route, and one
section per six routes with an index, a literal, an unconstrained parameter and a constrained parameter with two nested
children):

- **Match**: `CompiledRouteTable.Match` for seven URLs, in the middle section of the table (`/s<n>`, `n` being half the number
  of sections): the root, a static page (`/s<n>/new`), a parameter page (`/s<n>/42`), a deep page with a constrained `Guid`
  (`/s<n>/42/items/<guid>`), a miss (`/s<n>/nope/x/y`), a percent-encoded path (`/s<n>/by/hello%20w%C3%B6rld`) and a catch-all
  (`/docs/a/b/c/d`).
- **Generation**: `GenerateUrl` and `TryGenerateUrl` for a named route with one parameter.
- **Explain**: the diagnostic walk behind `Explain`, for a hit and a miss.
- **Compile**: `RouteCompiler.Compile` of the whole table.

How to run them is on the [development](development.md) page. The full tables are in
`ModRouter.Benchmarks/BASELINE.md` (Before and After).

## What it costs

Measured on an Intel Core i7-14700, Windows 11, .NET 10.0.12, Release, `--job short` (3 warmup and 3 measured iterations, so the
error columns are wide).

**Matching a URL**: a handful of hundred nanoseconds and under 2 KB, whatever the size of the table. The numbers below are the mean
of the four table sizes; the allocated bytes are identical at every size.

| URL | Before (B) | After (B) | Saved | Before (ns) | After (ns) |
|---|---:|---:|---:|---:|---:|
| Root | 272 | 240 | 12% | 31 | 28 |
| Static | 560 | 472 | 16% | 103 | 91 |
| Parameter | 1,144 | 776 | 32% | 168 | 130 |
| Deep | 2,064 | 1,696 | 18% | 368 | 339 |
| Miss | 312 | 224 | 28% | 70 | 78 |
| Encoded | 1,648 | 816 | 50% | 300 | 195 |
| Catch-all | 1,296 | 792 | 39% | 203 | 144 |

"Before" is the router before the match path was optimized; "After" is the router as shipped. The change was a count-then-fill
path splitter, a span-based percent-decoder (a stack or pooled buffer instead of a `List<byte>`, an intermediate array and a
substring per escape), one parameter dictionary for a level with no inherited parameters (it used to build two) and
`string.Join` instead of LINQ for a catch-all. The encoded path gains most. The miss time is within the noise of a short job: the
splitter now counts before it fills.

**Generating a URL**: about 210 to 260 ns and 1.13 KB per call at every table size. Not changed by that optimization.

**Compiling the table** allocates about 6 KB per route, and its time per route grows with the table (about 1 microsecond at 10
and 100 routes, 2 at 1,000, 5 at 10,000), paid once at startup (roughly 2 ms for 1,000 routes and 50 ms for 10,000). The optimization added about 5% to 7% to the allocations of a compile, because the trie now
computes a canonical key for each constraint list (48.7 KB to 51.0 KB at 10 routes, 54.6 MB to 58.4 MB at 10,000); the time is
within the noise of a short job.

**`Explain` is linear in the number of routes** by design: it tests every route against the path and keeps the verdict of each
(Before figures: 0.7 microseconds at 10 routes, 46 microseconds at 1,000, about 0.5 milliseconds and 2.3 MB at 10,000; that
optimization changed only how it ranks matching candidates and did not measure it again). It is the engine of the
debug panel's explorer and of diagnostics, not a hot path, and nothing in a navigation calls it.

## Match time does not depend on the number of routes

Literal segments are looked up in a dictionary and the walk is bounded by the depth of the path, so a table of 10,000 routes
matches as fast, and allocates as much, as a table of 10. The same holds for generation (one named lookup and a string build).
This is why the span-based trie walk the first design sketched (a "zero-allocation walk") was evaluated and **rejected**: the
baseline shows that scale is not the problem, so rewriting the walk would add risk for nothing the numbers ask for. The match path
was instead trimmed where the allocations are: the splitter and decoder, and the chain builder.

## The allocation budget

`MatchAllocationTests` guards the match path against regressions on every `dotnet test` run. It measures the bytes one `Match`
allocates on the current thread after warmup, in Debug and Release alike, and fails when a path exceeds its budget:

| URL | Measured | Budget |
|---|---:|---:|
| Static | 472 B | 480 B |
| Parameter | 776 B | 850 B |
| Miss | 224 B | 245 B |
| Encoded | 816 B | 900 B |

Each budget is the measured figure plus up to 10%, and never above the target set when the baseline was taken (static 480,
parameter 900, miss 260, encoded 1,000); the static path therefore has only 8 bytes of headroom, so one more field on its path
fails the test. The test also asserts that each path still matches (or, for the miss, misses), so a regression that ends a walk
early cannot pass on fewer bytes. It is a guard against regressions, not a performance contract: a change that needs more
should say why and raise the budget in the same commit.

## Limits of these numbers

- **They are CoreCLR numbers, not WebAssembly.** The allocated bytes are exact and carry over to the browser; the timings do not.
  The Blazor WebAssembly runtime is interpreted or AOT-compiled and runs on one thread, so read the timings as ratios between rows
  and between Before and After, not as milliseconds a user will see.
- **One machine and short jobs.** The error column of a `--job short` run is wide; compare the Allocated column exactly and the Mean
  column only when the difference is large.
- **Synthetic tables.** The tables have a regular shape (sections of six routes). The figures show that table size is not the
  cost, not what any one URL costs in a particular app.
- **Navigation costs more than matching.** A navigation also binds parameters, runs guards and loaders, commits state, renders and
  runs the title, focus and scroll effects. These benchmarks measure the router's engine, not a whole navigation.
