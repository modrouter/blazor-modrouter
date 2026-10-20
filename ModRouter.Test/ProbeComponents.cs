using ModRouter.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace ModRouter.Test;

/// <summary>A static event log the probe components write to; tests reset it in SetUp.</summary>
internal static class Probes
{
    public static List<string> Events { get; } = [];

    public static void Reset() => Events.Clear();

    public static int Count(string name) => Events.Count(e => e == name);
}

public abstract class ProbeBase : ComponentBase, IDisposable
{
    protected ProbeBase() => Probes.Events.Add($"ctor:{GetType().Name}");

    protected override void OnParametersSet() => Probes.Events.Add($"params:{GetType().Name}");

    public void Dispose() => Probes.Events.Add($"dispose:{GetType().Name}");

    protected static void Outlet(RenderTreeBuilder builder, int sequence, RenderFragment? empty = null)
    {
        builder.OpenComponent<ModRouterOutlet>(sequence);

        if (empty is not null)
        {
            builder.AddComponentParameter(sequence + 1, nameof(ModRouterOutlet.EmptyContent), empty);
        }

        builder.CloseComponent();
    }
}

public sealed class HomeProbe : ProbeBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "h1");
        builder.AddAttribute(1, "id", "home");
        builder.AddContent(2, "Home");
        builder.CloseElement();
    }
}

public sealed class CustomersProbe : ProbeBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "section");
        builder.AddAttribute(1, "id", "customers");
        Outlet(builder, 2);
        builder.CloseElement();
    }
}

public sealed class CustomersWithEmptyProbe : ProbeBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "section");
        builder.AddAttribute(1, "id", "customers");
        Outlet(builder, 2, b => b.AddMarkupContent(0, "<em id=\"empty\">pick a customer</em>"));
        builder.CloseElement();
    }
}

public sealed class DoubleOutletProbe : ProbeBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        Outlet(builder, 0);
        Outlet(builder, 10);
    }
}

public sealed class ListProbe : ProbeBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "ul");
        builder.AddAttribute(1, "id", "list");
        builder.CloseElement();
    }
}

public sealed class DetailsProbe : ProbeBase
{
    [Parameter]
    public int Id { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", "details");
        builder.AddContent(2, $"Customer {Id}");
        Outlet(builder, 3);
        builder.CloseElement();
    }
}

/// <summary>A component bound to an optional route parameter (<c>/list/{page:int?}</c>).</summary>
public sealed class PageProbe : ProbeBase
{
    [Parameter]
    public int? Page { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddAttribute(1, "id", "page");
        builder.AddContent(2, Page is null ? "no page" : $"page {Page}");
        builder.CloseElement();
    }
}

/// <summary>A component whose optional route parameter has an initializer (<c>/dl/{deleteFile:bool?}</c>), like a real page that defaults a flag to true.</summary>
public sealed class DeleteFileProbe : ProbeBase
{
    [Parameter]
    public bool? DeleteFile { get; set; } = true;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddAttribute(1, "id", "delete");
        builder.AddContent(2, DeleteFile is null ? "null" : DeleteFile.Value ? "true" : "false");
        builder.CloseElement();
    }
}

/// <summary>
/// A parent that hosts an outlet and whose <c>Id</c> has an initializer; another route places it with <c>{id:int}</c>
/// (<c>/p</c> with a child, and <c>/p/{id:int}</c>), so <c>id</c> is a route parameter name of the component.
/// </summary>
public sealed class DefaultIdProbe : ProbeBase
{
    [Parameter]
    public int Id { get; set; } = -1;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", "pid");
        builder.AddContent(2, $"id {Id}");
        Outlet(builder, 3);
        builder.CloseElement();
    }
}

/// <summary>A component bound to a floating-point route parameter (<c>/p/{x:double}</c>).</summary>
public sealed class DoubleProbe : ProbeBase
{
    [Parameter]
    public double X { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddAttribute(1, "id", "x");
        builder.AddContent(2, X.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.CloseElement();
    }
}

public sealed class OrderProbe : ProbeBase
{
    [Parameter]
    public Guid OrderId { get; set; }

    [Parameter]
    public int Id { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", "order");
        builder.AddContent(2, $"Order {OrderId} of customer {Id}");
        builder.CloseElement();
    }
}

public sealed class ShowProbe : ProbeBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddAttribute(1, "id", "show");
        builder.AddContent(2, "show");
        builder.CloseElement();
    }
}

public sealed class GuidProbe : ProbeBase
{
    [Parameter]
    public Guid Id { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddAttribute(1, "id", "guid");
        builder.AddContent(2, Id.ToString());
        builder.CloseElement();
    }
}

public sealed class MainLayoutProbe : LayoutComponentBase, IDisposable
{
    public MainLayoutProbe() => Probes.Events.Add("ctor:MainLayoutProbe");

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", "layout");
        builder.AddContent(2, Body);
        builder.CloseElement();
    }

    public void Dispose() => Probes.Events.Add("dispose:MainLayoutProbe");
}

public sealed class AltLayoutProbe : LayoutComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", "alt");
        builder.AddContent(2, Body);
        builder.CloseElement();
    }
}

[Layout(typeof(AltLayoutProbe))]
public sealed class AltLayoutPageProbe : ProbeBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "h1");
        builder.AddAttribute(1, "id", "alt-page");
        builder.AddContent(2, "alt");
        builder.CloseElement();
    }
}

public sealed class QueryParameterPage : ComponentBase
{
    [Parameter]
    [SupplyParameterFromQuery]
    public string? Id { get; set; }

    [Parameter]
    public string? Plain { get; set; }

    [CascadingParameter]
    public string? Cascaded { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? Rest { get; set; }

    [Parameter]
    public int? OptionalNumber { get; set; }

    [Parameter]
    public DayOfWeek Day { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
    }
}
