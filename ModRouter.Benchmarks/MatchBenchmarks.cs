using BenchmarkDotNet.Attributes;
using ModRouter.Core.Compilation;
using ModRouter.Core.Matching;

namespace ModRouter.Benchmarks;

[MemoryDiagnoser]
public class MatchBenchmarks
{
    const string CatchAllPath = "/docs/a/b/c/d";

    CompiledRouteTable _table = null!;
    string _static = string.Empty;
    string _parameter = string.Empty;
    string _deep = string.Empty;
    string _miss = string.Empty;
    string _encoded = string.Empty;

    [Params(10, 100, 1000, 10000)]
    public int Routes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _table = RouteTableFactory.Build(Routes);

        var section = RouteTableFactory.MidSection(Routes);
        _static = $"/s{section}/new";
        _parameter = $"/s{section}/42";
        _deep = $"/s{section}/42/items/0f8fad5b-d9cb-469f-a165-70867728950e";
        _miss = $"/s{section}/nope/x/y";
        _encoded = $"/s{section}/by/hello%20w%C3%B6rld";
    }

    [Benchmark]
    public RouteMatchResult Root() => _table.Match("/");

    [Benchmark]
    public RouteMatchResult Static() => _table.Match(_static);

    [Benchmark]
    public RouteMatchResult Parameter() => _table.Match(_parameter);

    [Benchmark]
    public RouteMatchResult Deep() => _table.Match(_deep);

    [Benchmark]
    public RouteMatchResult Miss() => _table.Match(_miss);

    [Benchmark]
    public RouteMatchResult Encoded() => _table.Match(_encoded);

    [Benchmark]
    public RouteMatchResult CatchAll() => _table.Match(CatchAllPath);
}
