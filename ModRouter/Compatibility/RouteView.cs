using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using ModRouter.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace ModRouter.Compatibility;

/// <summary>
/// A drop-in replacement for <c>Microsoft.AspNetCore.Components.RouteView</c>: renders the matched chain (through
/// <see cref="ModRouterOutlet"/>) inside the layout the depth-0 page declares with <c>[Layout]</c> (<c>@layout</c>), else
/// <see cref="DefaultLayout"/>. <see cref="RouteData"/> is carried for source compatibility and layout selection; the
/// page type is rendered directly only when the router has no chain for it (the <c>NotFoundPage</c> case). It must be
/// rendered inside the compatibility <see cref="Router"/>'s <c>Found</c> fragment.
/// </summary>
public class RouteView : ComponentBase
{
    static readonly ConcurrentDictionary<Type, Type?> LayoutTypes = new();

    [CascadingParameter]
    RouteOutletContext? Context { get; set; }

    /// <summary>The depth-0 component and its route values, as the router produced them.</summary>
    [Parameter]
    [EditorRequired]
    public RouteData? RouteData { get; set; }

    /// <summary>The layout when the page declares none with <c>@layout</c> / <c>[Layout]</c>.</summary>
    [Parameter]
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type? DefaultLayout { get; set; }

    /// <summary>The outlet context the compatibility Router cascades; available after <see cref="OnParametersSet"/>.</summary>
    protected RouteOutletContext OutletContext => Context!;

    /// <summary>
    /// Checks that <see cref="RouteData"/> is set and that the view sits inside the compatibility
    /// <see cref="Router"/>'s <c>Found</c> fragment.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///   <see cref="RouteData"/> is null, or no route outlet context was cascaded (the view is not inside the
    ///   compatibility router).
    /// </exception>
    protected override void OnParametersSet()
    {
        if (RouteData is null)
        {
            throw new InvalidOperationException($"The {GetType().Name} component requires a non-null value for the parameter {nameof(RouteData)}.");
        }

        if (Context is null)
        {
            throw new InvalidOperationException($"{GetType().Name} (ModRouter.Compatibility) must be rendered inside the <Found> fragment of the compatibility Router, ModRouter.Compatibility.Router (no route outlet context was found). In App.razor use the fully-qualified tags <ModRouter.Compatibility.Router> and <ModRouter.Compatibility.{GetType().Name}>: a plain <Router> binds to the built-in router.");
        }
    }

    /// <summary>
    /// Renders <see cref="RenderBody"/> inside the page's layout: its <c>[Layout]</c>, else
    /// <see cref="DefaultLayout"/>, else none.
    /// </summary>
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<LayoutView>(0);
        builder.AddComponentParameter(1, nameof(LayoutView.Layout), Layout);
        builder.AddComponentParameter(2, nameof(LayoutView.ChildContent), (RenderFragment)RenderBody);
        builder.CloseComponent();
    }

    /// <summary>
    /// What goes inside the layout: the chain's depth-0 outlet; or, when the committed state has no chain (the Router's
    /// <c>NotFoundPage</c> rendered through <c>Found</c>), the page itself with its route values, as the built-in does.
    /// </summary>
    protected virtual void RenderBody(RenderTreeBuilder builder)
    {
        if (OutletContext.State.Chain is null)
        {
            builder.OpenComponent(0, RouteData!.PageType);

            foreach (var (name, value) in RouteData.RouteValues)
            {
                builder.AddComponentParameter(1, name, value);
            }

            builder.CloseComponent();

            return;
        }

        builder.OpenComponent<ModRouterOutlet>(2);
        builder.CloseComponent();
    }

    /// <summary>The page's <c>[Layout]</c>, else <see cref="DefaultLayout"/>.</summary>
    protected Type? Layout
        => LayoutTypes.GetOrAdd(RouteData!.PageType, static type => type.GetCustomAttribute<LayoutAttribute>(inherit: true)?.LayoutType) ?? DefaultLayout;
}
