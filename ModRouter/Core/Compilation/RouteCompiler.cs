using ModRouter.Core.Constraints;
using ModRouter.Core.Definitions;

namespace ModRouter.Core.Compilation;

/// <summary>Compiles route definitions from any source into one immutable <see cref="CompiledRouteTable"/>.</summary>
public static class RouteCompiler
{
    /// <exception cref="RouteCompilationException">One or more definitions are invalid; lists every problem.</exception>
    public static CompiledRouteTable Compile(IEnumerable<RouteDefinition> definitions, IConstraintRegistry? constraints = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        return new RouteTableBuilder(constraints ?? new ConstraintRegistry()).Build(definitions.ToList());
    }
}
