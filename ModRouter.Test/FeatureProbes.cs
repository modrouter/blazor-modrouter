using ModRouter.Components;
using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace ModRouter.Test;

/// <summary>A parent whose outlet has its own error content, to prove it beats the router's.</summary>
public sealed class CustomersWithOwnErrorsProbe : ProbeBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "section");
        builder.AddAttribute(1, "id", "customers");
        builder.OpenComponent<ModRouterOutlet>(2);
        builder.AddComponentParameter(3, nameof(ModRouterOutlet.NotAuthorized), (RenderFragment<RouteError>)(error => b => b.AddMarkupContent(0, $"<p id=\"own-na\">own: {error.Message}</p>")));
        builder.AddComponentParameter(4, nameof(ModRouterOutlet.Error), (RenderFragment<RouteError>)(error => b => b.AddMarkupContent(0, $"<p id=\"own-error\">own error: {error.Kind}</p>")));
        builder.CloseComponent();
        builder.CloseElement();
    }
}

/// <summary>Shows the data the router cascaded: its own level's, and the nearest ancestor's.</summary>
public sealed class DataProbe : ProbeBase
{
    [CascadingParameter]
    ModRouteData Data { get; set; } = default!;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", "data");
        builder.AddContent(2, Data.Get<string>());
        builder.CloseElement();
        builder.OpenElement(3, "div");
        builder.AddAttribute(4, "id", "ancestor");
        builder.AddContent(5, Data.GetFromAncestor<string>());
        builder.CloseElement();
        builder.OpenComponent<ModRouterOutlet>(6);
        builder.CloseComponent();
    }
}

/// <summary>Shows a deferred value and a stream registered by the route's loader.</summary>
public sealed class SlotsProbe : ProbeBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<ModDeferred<int>>(0);
        builder.AddComponentParameter(1, nameof(ModDeferred<int>.Name), "slow");
        builder.AddComponentParameter(2, nameof(ModDeferred<int>.Fallback), (RenderFragment)(b => b.AddMarkupContent(0, "<i id=\"slow-fallback\">waiting</i>")));
        builder.AddComponentParameter(3, nameof(ModDeferred<int>.ChildContent), (RenderFragment<int>)(value => b => b.AddMarkupContent(0, $"<b id=\"slow-value\">{value}</b>")));
        builder.CloseComponent();

        builder.OpenComponent<ModStream<int>>(10);
        builder.AddComponentParameter(11, nameof(ModStream<int>.Name), "feed");
        builder.AddComponentParameter(12, nameof(ModStream<int>.Fallback), (RenderFragment)(b => b.AddMarkupContent(0, "<i id=\"feed-fallback\">none yet</i>")));
        builder.AddComponentParameter(13, nameof(ModStream<int>.ItemTemplate), (RenderFragment<int>)(item => b => b.AddMarkupContent(0, $"<span class=\"feed-item\">{item}</span>")));
        builder.CloseComponent();
    }
}
