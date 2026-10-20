using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Discovery;

namespace ModRouter;

/// <summary>
/// Page discovery for the fluent <see cref="RouteMap"/>. In the root namespace, so the same <c>using</c> that gives
/// <c>AddModRouter</c> gives <c>AddPages</c>; the <c>Route*</c> attributes and <see cref="PageAssemblyCollection"/> are in
/// <c>ModRouter.Discovery</c>.
/// </summary>
public static class RouteMapPagesExtensions
{
    internal const string TrimmingMessage = "Pages are found by reflection over the assembly's exported types. The default Blazor WebAssembly publish keeps them; an assembly that opts into trimming (IsTrimmable) must be rooted in the app project with <TrimmerRootAssembly Include=\"...\" />.";

    /// <summary>
    /// Adds every routable component of <paramref name="assembly"/> as a route: a non-abstract, non-generic
    /// <c>IComponent</c> with at least one <c>[Route]</c> (<c>@page</c>) and without <c>[ExcludeFromInteractiveRouting]</c>,
    /// for which <paramref name="filter"/> (when given) returns true. The first <c>[Route]</c> is the route's template, the
    /// others its aliases; the <c>Route*</c> attributes of the Discovery namespace add the name, title, breadcrumb, guards,
    /// loader, focus, announcement, index and nesting. Pages are appended after the routes declared so far; a page nested
    /// with <c>[RouteParent]</c> under a component the map already declares is attached to that route.
    /// </summary>
    /// <exception cref="RouteCompilationException">A page's attributes are invalid (RT0019 to RT0022, RT0024).</exception>
    [RequiresUnreferencedCode(TrimmingMessage)]
    public static RouteMap AddPages(this RouteMap map, Assembly assembly, Func<Type, bool>? filter = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var (parent, definition) in PageDiscovery.Discover(assembly, filter, map.FullTemplatesOf))
        {
            if (parent is null)
            {
                map.Add(definition);
            }
            else
            {
                map.NestUnder(parent, definition);
            }
        }

        return map;
    }
}
