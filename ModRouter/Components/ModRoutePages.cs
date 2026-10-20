using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace ModRouter.Components;

/// <summary>
/// The markup form of <c>RouteMap.AddPages</c>: inside <c>&lt;ModRouter&gt;&lt;Routes&gt;</c>, discovers the <c>@page</c> components
/// of <see cref="Assembly"/> (those <see cref="Filter"/> returns true for, when given) as top-level routes, after the
/// <see cref="ModRouteEntry"/> entries. Renders nothing. Pages are found by reflection: an assembly that opts into trimming
/// must be rooted in the app project with <c>&lt;TrimmerRootAssembly Include="..." /&gt;</c>. Unlike <c>AddPages</c> and
/// <c>ModRouterOptions.Pages.Add</c>, which are <c>[RequiresUnreferencedCode]</c>, a markup use gets no trim-analyzer
/// warning: the requirement is on a component parameter, which the analyzer cannot see, so it is documented here instead.
/// </summary>
public sealed class ModRoutePages : ComponentBase
{
    [CascadingParameter]
    RouteEntryCollector? Collector { get; set; }

    /// <summary>The assembly whose pages are discovered.</summary>
    [Parameter]
    [EditorRequired]
    public Assembly? Assembly { get; set; }

    /// <summary>Keeps only the component types it returns true for.</summary>
    [Parameter]
    public Func<Type, bool>? Filter { get; set; }

    /// <summary>
    /// Registers the assembly and filter with the router's collector; the pages are discovered when the router compiles
    /// its table.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///   The component is not inside <c>&lt;ModRouter&gt;&lt;Routes&gt;</c>.
    /// </exception>
    protected override void OnInitialized()
    {
        if (Collector is null)
        {
            throw new InvalidOperationException("ModRoutePages must be rendered inside <ModRouter><Routes>...</Routes></ModRouter>.");
        }

        Collector.RegisterPages(Assembly, Filter);
    }
}
