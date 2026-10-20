using ModRouter.Core.Matching;

namespace ModRouter.Core.Compilation;

public sealed partial class CompiledRouteTable
{
    /// <summary>
    /// Explains how <paramref name="appRelativePath"/> is matched: every candidate route with a verdict per segment,
    /// and the selected route. Intended for debugging precedence; it is slower than <see cref="Match"/>.
    /// </summary>
    public RouteExplanation Explain(string appRelativePath) => ExplainEvaluator.Explain(Routes, appRelativePath);
}
