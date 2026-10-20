using BenchmarkDotNet.Attributes;
using ModRouter.Core.Compilation;
using ModRouter.Core.Generation;

namespace ModRouter.Benchmarks;

[MemoryDiagnoser]
public class GenerationBenchmarks
{
    readonly IReadOnlyDictionary<string, object?> _values = RouteValues.From(new { id = 42 });

    CompiledRouteTable _table = null!;
    string _name = string.Empty;

    [Params(10, 100, 1000, 10000)]
    public int Routes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _table = RouteTableFactory.Build(Routes);
        _name = $"s{RouteTableFactory.MidSection(Routes)}.detail";
    }

    [Benchmark]
    public string GenerateUrl() => _table.GenerateUrl(_name, _values);

    [Benchmark]
    public bool TryGenerateUrl() => _table.TryGenerateUrl(_name, _values, null, null, null, out _, out _);
}
