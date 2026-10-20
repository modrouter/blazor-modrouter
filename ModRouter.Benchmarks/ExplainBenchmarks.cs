using BenchmarkDotNet.Attributes;
using ModRouter.Core.Compilation;
using ModRouter.Core.Matching;

namespace ModRouter.Benchmarks;

/// <summary><c>Explain</c> tests every route against the path (it is the debug panel's engine, not a hot path), so it is linear in the table.</summary>
[MemoryDiagnoser]
public class ExplainBenchmarks
{
    CompiledRouteTable _table = null!;
    string _path = string.Empty;
    string _miss = string.Empty;

    [Params(10, 100, 1000, 10000)]
    public int Routes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _table = RouteTableFactory.Build(Routes);

        var section = RouteTableFactory.MidSection(Routes);
        _path = $"/s{section}/42";
        _miss = $"/s{section}/nope/x/y";
    }

    [Benchmark]
    public RouteExplanation Hit() => _table.Explain(_path);

    [Benchmark]
    public RouteExplanation Miss() => _table.Explain(_miss);
}
