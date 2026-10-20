using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace ModRouter.Components;

/// <summary>
/// Renders the application's <c>NotFound</c> fragment in the compatibility shape and keeps it from running again, for the
/// same committed state, while a navigation is in progress. The sites' <c>NotFound</c> fragments navigate while they render
/// (<c>RedirectToDashboard</c>); anything that re-renders the router during that navigation (the application's root,
/// re-rendered by an <c>EventCallback</c> when <c>OnNavigateAsync</c> starts, or the screen-reader announcement) would run the
/// fragment, and so the navigation, again: each location change cancels the hook that is running, so no hook finishes and
/// the router loops. While <see cref="Navigating"/> is true the output already on screen stays; the fragment runs again as
/// soon as the router re-renders with no navigation in progress, or for a new committed state.
/// </summary>
internal sealed class ModNotFoundHost : ComponentBase
{
    bool _rendered;
    RouteState? _renderedState;

    /// <summary>The application's <c>NotFound</c> content.</summary>
    [Parameter]
    public RenderFragment? Content { get; set; }

    /// <summary>The committed state that shows the content.</summary>
    [Parameter]
    public RouteState? State { get; set; }

    /// <summary>True while a navigation is preparing or waiting for its <c>OnNavigateAsync</c>.</summary>
    [Parameter]
    public bool Navigating { get; set; }

    // The first render of a component never asks ShouldRender, so what was rendered is recorded where it is rendered.
    protected override bool ShouldRender() => !(_rendered && ReferenceEquals(State, _renderedState) && Navigating);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        _rendered = true;
        _renderedState = State;
        builder.AddContent(0, Content);
    }
}
