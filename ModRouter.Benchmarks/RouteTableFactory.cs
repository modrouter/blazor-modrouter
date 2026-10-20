using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;

namespace ModRouter.Benchmarks;

/// <summary>
/// A synthetic table of about <c>routes</c> flattened routes: a root, a catch-all, an optional, and one section per six
/// routes (an index, a literal, an unconstrained parameter, a named constrained parameter with two nested children).
/// </summary>
internal static class RouteTableFactory
{
    public static IReadOnlyList<RouteDefinition> Definitions(int routes)
    {
        var map = RouteMap.Create();
        map.Route("/", typeof(object));
        map.Route("/docs/{**path}", typeof(object));
        map.Route("/search/{term?}", typeof(object));

        for (var section = 0; section < Sections(routes); section++)
        {
            var number = section;

            map.Route($"/s{number}", typeof(object), children: sectionMap =>
            {
                sectionMap.Index(typeof(object));
                sectionMap.Route("new", typeof(object));
                sectionMap.Route("by/{slug}", typeof(object));
                sectionMap.Route("{id:int}", typeof(object), name: $"s{number}.detail", children: detail =>
                {
                    detail.Route("edit", typeof(object));
                    detail.Route("items/{itemId:guid}", typeof(object));
                });
            });
        }

        return map.Build();
    }

    public static CompiledRouteTable Build(int routes) => RouteCompiler.Compile(Definitions(routes));

    /// <summary>The section the request paths use: in the middle of the table.</summary>
    public static int MidSection(int routes) => Sections(routes) / 2;

    static int Sections(int routes) => Math.Max(1, routes / 6);
}
