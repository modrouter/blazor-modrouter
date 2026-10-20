using BenchmarkDotNet.Running;

// dotnet run -c Release -- --filter "*MatchBenchmarks*" --job short
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
