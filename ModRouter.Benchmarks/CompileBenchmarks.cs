using BenchmarkDotNet.Attributes;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;

namespace ModRouter.Benchmarks;

/// <summary>Compiling the definitions into a table: the one-time startup cost (the definitions are built outside the measurement).</summary>
[MemoryDiagnoser]
public class CompileBenchmarks
{
    IReadOnlyList<RouteDefinition> _definitions = [];

    [Params(10, 100, 1000, 10000)]
    public int Routes { get; set; }

    [GlobalSetup]
    public void Setup() => _definitions = RouteTableFactory.Definitions(Routes);

    [Benchmark]
    public CompiledRouteTable Compile() => RouteCompiler.Compile(_definitions);
}
