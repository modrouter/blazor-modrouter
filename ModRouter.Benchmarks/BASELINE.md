# ModRouter benchmarks

What the router costs: matching a URL, generating a URL, explaining a match and compiling a table, measured through the public
API over synthetic tables of 10 to 10,000 routes (see `RouteTableFactory.cs`: a root, a catch-all, an optional, and one section
per six routes with an index, a literal, an unconstrained parameter and a named constrained parameter with two nested children).
These are CoreCLR numbers, not WebAssembly: allocation figures are exact and carry over, timings are indicative and are best read
as ratios between rows and between Before and After.

## Environment

- CPU: Intel Core i7-14700 (20 cores, 28 logical processors), 2.10 GHz
- OS: Windows 11 Home 10.0.26300
- .NET SDK 10.0.401, runtime .NET 10.0.12 (X64 RyuJIT x86-64-v3)
- Release build, BenchmarkDotNet 0.15.8, `--job short` (3 warmup, 3 iterations, 1 launch), so the Error column is wide
- Command, one class at a time: `dotnet run -c Release --no-build --project ModRouter.Benchmarks -- --filter "*MatchBenchmarks*" --job short`
  (and `GenerationBenchmarks`, `ExplainBenchmarks`, `CompileBenchmarks`)

## Before

The state of the router before any slice 4d matcher change.

### Match

| Method    | Routes | Mean      | Error      | StdDev    | Gen0   | Gen1   | Allocated |
|---------- |------- |----------:|-----------:|----------:|-------:|-------:|----------:|
| Root      | 10     |  29.83 ns |   0.899 ns |  0.049 ns | 0.0157 |      - |     272 B |
| Static    | 10     |  93.82 ns |  11.114 ns |  0.609 ns | 0.0324 |      - |     560 B |
| Parameter | 10     | 151.82 ns |  66.747 ns |  3.659 ns | 0.0663 |      - |    1144 B |
| Deep      | 10     | 328.60 ns | 193.050 ns | 10.582 ns | 0.1192 | 0.0005 |    2064 B |
| Miss      | 10     |  61.25 ns |   9.731 ns |  0.533 ns | 0.0180 |      - |     312 B |
| Encoded   | 10     | 282.76 ns |  85.935 ns |  4.710 ns | 0.0954 |      - |    1648 B |
| CatchAll  | 10     | 217.16 ns |  99.752 ns |  5.468 ns | 0.0751 |      - |    1296 B |
| Root      | 100    |  33.33 ns |  20.441 ns |  1.120 ns | 0.0157 |      - |     272 B |
| Static    | 100    | 117.21 ns |  14.706 ns |  0.806 ns | 0.0324 |      - |     560 B |
| Parameter | 100    | 182.74 ns |  94.016 ns |  5.153 ns | 0.0663 |      - |    1144 B |
| Deep      | 100    | 390.74 ns | 134.308 ns |  7.362 ns | 0.1192 | 0.0005 |    2064 B |
| Miss      | 100    |  73.60 ns |  25.519 ns |  1.399 ns | 0.0180 |      - |     312 B |
| Encoded   | 100    | 280.53 ns | 213.871 ns | 11.723 ns | 0.0954 |      - |    1648 B |
| CatchAll  | 100    | 198.87 ns |  22.130 ns |  1.213 ns | 0.0751 |      - |    1296 B |
| Root      | 1000   |  32.01 ns |  77.318 ns |  4.238 ns | 0.0157 |      - |     272 B |
| Static    | 1000   | 101.35 ns |  79.210 ns |  4.342 ns | 0.0324 |      - |     560 B |
| Parameter | 1000   | 177.02 ns |  17.450 ns |  0.956 ns | 0.0663 |      - |    1144 B |
| Deep      | 1000   | 375.18 ns |  62.243 ns |  3.412 ns | 0.1192 | 0.0005 |    2064 B |
| Miss      | 1000   |  73.63 ns |  76.174 ns |  4.175 ns | 0.0180 |      - |     312 B |
| Encoded   | 1000   | 309.08 ns | 323.139 ns | 17.712 ns | 0.0954 |      - |    1648 B |
| CatchAll  | 1000   | 195.77 ns |  21.224 ns |  1.163 ns | 0.0751 |      - |    1296 B |
| Root      | 10000  |  29.79 ns |   4.008 ns |  0.220 ns | 0.0157 |      - |     272 B |
| Static    | 10000  |  99.08 ns |  16.526 ns |  0.906 ns | 0.0324 |      - |     560 B |
| Parameter | 10000  | 161.47 ns | 101.485 ns |  5.563 ns | 0.0663 |      - |    1144 B |
| Deep      | 10000  | 377.12 ns |  99.796 ns |  5.470 ns | 0.1192 | 0.0005 |    2064 B |
| Miss      | 10000  |  72.20 ns |  32.998 ns |  1.809 ns | 0.0180 |      - |     312 B |
| Encoded   | 10000  | 327.63 ns |  79.494 ns |  4.357 ns | 0.0954 |      - |    1648 B |
| CatchAll  | 10000  | 201.67 ns |  65.759 ns |  3.604 ns | 0.0751 |      - |    1296 B |

### Generation

| Method         | Routes | Mean     | Error     | StdDev   | Gen0   | Allocated |
|--------------- |------- |---------:|----------:|---------:|-------:|----------:|
| GenerateUrl    | 10     | 208.9 ns |  21.03 ns |  1.15 ns | 0.0672 |   1.13 KB |
| TryGenerateUrl | 10     | 213.8 ns |  57.59 ns |  3.16 ns | 0.0672 |   1.13 KB |
| GenerateUrl    | 100    | 217.6 ns |  61.72 ns |  3.38 ns | 0.0672 |   1.13 KB |
| TryGenerateUrl | 100    | 229.8 ns |  52.18 ns |  2.86 ns | 0.0672 |   1.13 KB |
| GenerateUrl    | 1000   | 235.7 ns |  41.61 ns |  2.28 ns | 0.0672 |   1.13 KB |
| TryGenerateUrl | 1000   | 213.8 ns |  25.85 ns |  1.42 ns | 0.0672 |   1.13 KB |
| GenerateUrl    | 10000  | 235.0 ns | 241.58 ns | 13.24 ns | 0.0672 |   1.13 KB |
| TryGenerateUrl | 10000  | 238.5 ns |  75.16 ns |  4.12 ns | 0.0672 |   1.13 KB |

### Explain

| Method | Routes | Mean         | Error         | StdDev       | Gen0     | Gen1   | Allocated  |
|------- |------- |-------------:|--------------:|-------------:|---------:|-------:|-----------:|
| Hit    | 10     |     748.9 ns |      61.50 ns |      3.37 ns |   0.2584 | 0.0019 |    4.36 KB |
| Miss   | 10     |     673.6 ns |      70.03 ns |      3.84 ns |   0.2365 | 0.0019 |    3.99 KB |
| Hit    | 100    |   4,414.1 ns |     648.87 ns |     35.57 ns |   1.4648 | 0.0076 |   24.75 KB |
| Miss   | 100    |   4,603.0 ns |   1,554.59 ns |     85.21 ns |   1.4420 | 0.0076 |   24.38 KB |
| Hit    | 1000   |  45,897.1 ns |  24,393.51 ns |  1,337.09 ns |  13.5498 | 0.1221 |  228.66 KB |
| Miss   | 1000   |  43,794.3 ns |  23,662.37 ns |  1,297.01 ns |  13.5498 | 0.1221 |   228.3 KB |
| Hit    | 10000  | 514,721.0 ns |  49,972.14 ns |  2,739.14 ns | 138.6719 | 0.9766 | 2341.09 KB |
| Miss   | 10000  | 517,314.3 ns | 709,313.37 ns | 38,879.86 ns | 138.6719 | 0.9766 | 2340.73 KB |

### Compile

| Method  | Routes | Mean         | Error          | StdDev       | Gen0      | Gen1      | Gen2     | Allocated   |
|-------- |------- |-------------:|---------------:|-------------:|----------:|----------:|---------:|------------:|
| Compile | 10     |     10.75 us |       4.056 us |     0.222 us |    2.8992 |    0.1068 |        - |    48.72 KB |
| Compile | 100    |    124.12 us |      17.682 us |     0.969 us |   32.7148 |    9.0332 |        - |    537.8 KB |
| Compile | 1000   |  2,070.96 us |   1,964.306 us |   107.670 us |  343.7500 |  265.6250 |        - |  5439.38 KB |
| Compile | 10000  | 57,097.34 us | 127,123.480 us | 6,968.067 us | 4600.0000 | 2500.0000 | 800.0000 | 54600.53 KB |

## Reading the numbers

- **Match time and allocations do not depend on the number of routes.** Literal lookup is a dictionary and the walk is bounded by
  the path depth, so the 10-route and 10,000-route rows agree (within the noise of a short job) and the allocated bytes are
  identical.
- **Generation is flat too**: one named lookup and a string build, about 1.1 KB per call at every size.
- **Compilation allocates linearly in the number of routes** (about 5.5 KB per route), but its time per route grows with the
  table: about 1 microsecond per route at 10 and 100 routes, 2 at 1,000 and 5 to 6 at 10,000 (roughly 57 ms and 54 MB for
  10,000 routes), paid once at startup.
- **`Explain` is linear by design**: it tests every route against the path (it is the debug panel's engine, not a hot path).
- Short jobs have wide error columns; compare the Allocated column exactly and the Mean column only when the difference is large.

## After

After the slice 4d match-path change (count-then-fill split, span decoder with a stack or pooled buffer, one dictionary for a
level without inherited parameters, `string.Join` instead of LINQ for a catch-all), same machine and command.

### Match

| Method    | Routes | Mean      | Error      | StdDev    | Gen0   | Allocated |
|---------- |------- |----------:|-----------:|----------:|-------:|----------:|
| Root      | 10     |  27.31 ns |   0.989 ns |  0.054 ns | 0.0139 |     240 B |
| Static    | 10     |  89.43 ns |  20.400 ns |  1.118 ns | 0.0273 |     472 B |
| Parameter | 10     | 140.80 ns | 110.233 ns |  6.042 ns | 0.0449 |     776 B |
| Deep      | 10     | 351.00 ns | 228.021 ns | 12.499 ns | 0.0982 |    1696 B |
| Miss      | 10     |  79.00 ns |  41.752 ns |  2.289 ns | 0.0129 |     224 B |
| Encoded   | 10     | 192.45 ns | 214.494 ns | 11.757 ns | 0.0472 |     816 B |
| CatchAll  | 10     | 154.30 ns | 122.130 ns |  6.694 ns | 0.0458 |     792 B |
| Root      | 100    |  27.63 ns |   7.093 ns |  0.389 ns | 0.0139 |     240 B |
| Static    | 100    |  89.11 ns |  18.344 ns |  1.006 ns | 0.0273 |     472 B |
| Parameter | 100    | 136.46 ns |  55.537 ns |  3.044 ns | 0.0448 |     776 B |
| Deep      | 100    | 362.29 ns |  66.141 ns |  3.625 ns | 0.0982 |    1696 B |
| Miss      | 100    |  85.77 ns |  47.899 ns |  2.625 ns | 0.0130 |     224 B |
| Encoded   | 100    | 204.73 ns |   8.780 ns |  0.481 ns | 0.0472 |     816 B |
| CatchAll  | 100    | 133.87 ns |  18.524 ns |  1.015 ns | 0.0458 |     792 B |
| Root      | 1000   |  30.28 ns |   8.011 ns |  0.439 ns | 0.0139 |     240 B |
| Static    | 1000   |  93.63 ns |  10.802 ns |  0.592 ns | 0.0273 |     472 B |
| Parameter | 1000   | 122.79 ns |  98.735 ns |  5.412 ns | 0.0448 |     776 B |
| Deep      | 1000   | 346.12 ns | 144.799 ns |  7.937 ns | 0.0982 |    1696 B |
| Miss      | 1000   |  73.91 ns |  57.391 ns |  3.146 ns | 0.0130 |     224 B |
| Encoded   | 1000   | 203.06 ns |  47.272 ns |  2.591 ns | 0.0472 |     816 B |
| CatchAll  | 1000   | 151.90 ns | 208.536 ns | 11.431 ns | 0.0458 |     792 B |
| Root      | 10000  |  26.11 ns |   8.374 ns |  0.459 ns | 0.0139 |     240 B |
| Static    | 10000  |  92.32 ns |  28.023 ns |  1.536 ns | 0.0273 |     472 B |
| Parameter | 10000  | 121.70 ns |  79.471 ns |  4.356 ns | 0.0448 |     776 B |
| Deep      | 10000  | 297.24 ns | 121.494 ns |  6.659 ns | 0.0982 |    1696 B |
| Miss      | 10000  |  71.33 ns |  13.965 ns |  0.765 ns | 0.0129 |     224 B |
| Encoded   | 10000  | 179.89 ns |  85.282 ns |  4.675 ns | 0.0472 |     816 B |
| CatchAll  | 10000  | 136.56 ns |  74.653 ns |  4.092 ns | 0.0458 |     792 B |

### Generation

Not on the changed path; unchanged within noise.

| Method         | Routes | Mean     | Error     | StdDev   | Gen0   | Allocated |
|--------------- |------- |---------:|----------:|---------:|-------:|----------:|
| GenerateUrl    | 10     | 241.3 ns | 140.30 ns |  7.69 ns | 0.0672 |   1.13 KB |
| TryGenerateUrl | 10     | 252.2 ns | 108.57 ns |  5.95 ns | 0.0672 |   1.13 KB |
| GenerateUrl    | 100    | 255.4 ns | 211.91 ns | 11.62 ns | 0.0672 |   1.13 KB |
| TryGenerateUrl | 100    | 255.2 ns |  85.85 ns |  4.71 ns | 0.0672 |   1.13 KB |
| GenerateUrl    | 1000   | 261.7 ns |  78.75 ns |  4.32 ns | 0.0672 |   1.13 KB |
| TryGenerateUrl | 1000   | 220.9 ns |  49.23 ns |  2.70 ns | 0.0672 |   1.13 KB |
| GenerateUrl    | 10000  | 215.4 ns |  93.22 ns |  5.11 ns | 0.0672 |   1.13 KB |
| TryGenerateUrl | 10000  | 216.4 ns |  74.23 ns |  4.07 ns | 0.0672 |   1.13 KB |

### Compile

Not changed by the match-path work. The canonical constraint keys (computed while the trie is built) add about 5% to 7% to the
allocations of a compile (48.72 to 50.97 KB at 10 routes, 54,600 to 58,352 KB at 10,000); the time is within the noise of a short
job.

| Method  | Routes | Mean         | Error         | StdDev     | Gen0      | Gen1      | Gen2     | Allocated   |
|-------- |------- |-------------:|--------------:|-----------:|----------:|----------:|---------:|------------:|
| Compile | 10     |     11.05 us |      6.290 us |   0.345 us |    3.0212 |    0.1221 |        - |    50.97 KB |
| Compile | 100    |    128.83 us |     11.889 us |   0.652 us |   33.9355 |   10.7422 |        - |    573.8 KB |
| Compile | 1000   |  1,727.03 us |    342.687 us |  18.784 us |  343.7500 |  306.6406 |        - |  5812.88 KB |
| Compile | 10000  | 50,194.43 us | 15,918.011 us | 872.520 us | 4100.0000 | 2300.0000 | 700.0000 | 58352.19 KB |

### Change

Allocated bytes per match are identical at every table size; the times are the mean of the four table sizes (short jobs, so
read only the large differences).

| Scenario  | Before B | After B | Saved | Before ns | After ns |
|-----------|---------:|--------:|------:|----------:|---------:|
| Root      |      272 |     240 |   12% |      31.2 |     27.8 |
| Static    |      560 |     472 |   16% |     102.9 |     91.1 |
| Parameter |    1,144 |     776 |   32% |     168.3 |    130.4 |
| Deep      |    2,064 |   1,696 |   18% |     367.9 |    339.2 |
| Miss      |      312 |     224 |   28% |      70.2 |     77.5 |
| Encoded   |    1,648 |     816 |   50% |     300.0 |    195.0 |
| CatchAll  |    1,296 |     792 |   39% |     203.4 |    144.2 |

- The encoded path gains most: no substring per escape, no `List<byte>`, no intermediate array, and about a third less time.
- A single-level parameter route no longer copies its one dictionary into a second one (216 B each).
- The miss time is within the error column of a short job (it gained a counting pass over the path and lost the list).
- The budget test (`MatchAllocationTests`) measures the same figures on its own table in Debug and Release: 472, 776, 224 and
  816 B for static, parameter, miss and encoded.
