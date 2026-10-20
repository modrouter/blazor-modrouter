// The pages of the compatibility Router tests: a small application shaped like a typical site (a home, a login page,
// a customers tree, authorized and anonymous pages, a page with its own layout). The layouts and pages count their
// instances so a test can prove what survived a navigation.
using ModRouter.Components;
using ModRouter.Discovery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace ModRouter.Test.Fixtures;

/// <summary>Resets the instance counters of the layouts and pages below.</summary>
public static class CompatCounters
{
    public static void Reset()
    {
        CompatLayout.Instances = 0;
        CompatAltLayout.Instances = 0;
        CompatCustomersPage.Instances = 0;
        CompatCustomerPage.Instances = 0;
        CompatCustomerPage.ParameterSets = 0;
    }
}

/// <summary>The default layout; counts its instances.</summary>
public sealed class CompatLayout : LayoutComponentBase
{
    public static int Instances { get; set; }

    public CompatLayout() => Instances++;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", "layout");
        builder.AddContent(2, Body);
        builder.CloseElement();
    }
}

/// <summary>A second layout, selected by a page's [Layout].</summary>
public sealed class CompatAltLayout : LayoutComponentBase
{
    public static int Instances { get; set; }

    public CompatAltLayout() => Instances++;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", "alt-layout");
        builder.AddContent(2, Body);
        builder.CloseElement();
    }
}

[Route("/")]
[RouteName("home")]
public sealed class CompatHomePage : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<h1 id=\"home\">Home</h1>");
}

[Route("/login")]
[RouteName("login")]
public sealed class CompatLoginPage : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<p id=\"login\">Sign in</p>");
}

[Route("/customers")]
[RouteName("customers")]
public sealed class CompatCustomersPage : ComponentBase
{
    public static int Instances { get; set; }

    public CompatCustomersPage() => Instances++;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "section");
        builder.AddAttribute(1, "id", "customers");
        builder.OpenComponent<ModRouterOutlet>(2);
        builder.CloseComponent();
        builder.CloseElement();
    }
}

[Route("/customers/{Id:int}")]
[RouteParent(typeof(CompatCustomersPage))]
[RouteName("customer")]
public sealed class CompatCustomerPage : ComponentBase
{
    public static int Instances { get; set; }

    public static int ParameterSets { get; set; }

    public CompatCustomerPage() => Instances++;

    [Parameter]
    public int Id { get; set; }

    protected override void OnParametersSet() => ParameterSets++;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", "customer");
        builder.AddContent(2, $"Customer {Id}");
        builder.CloseElement();
    }
}

/// <summary>Administrators only: the real [Authorize] guard denies everyone else, so the application's NotAuthorized fragment renders.</summary>
[Route("/admin")]
[RouteName("admin")]
[Authorize(Roles = "admin")]
public sealed class CompatAdminPage : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<p id=\"admin\">Admin area</p>");
}

/// <summary>[Authorize] + [AllowAnonymous]: what a real site's Index page carries through _Imports.razor.</summary>
[Route("/open")]
[RouteName("open")]
[Authorize]
[AllowAnonymous]
public sealed class CompatOpenPage : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<p id=\"open\">Open to everyone</p>");
}

/// <summary>A page that selects its own layout with [Layout] (@layout in Razor).</summary>
[Route("/alt")]
[RouteName("alt")]
[Layout(typeof(CompatAltLayout))]
public sealed class CompatAltLayoutPage : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<p id=\"alt\">Alt layout page</p>");
}

/// <summary>A page an application names as the Router's NotFoundPage (the built-in requires a [Route] on it).</summary>
[Route("/not-found-page")]
[RouteName("not-found-page")]
public sealed class CompatNotFoundPage : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<p id=\"not-found-page\">Nothing here.</p>");
}

/// <summary>A top-level template without a leading slash, as <c>@attribute [Route("document-expiry-tool")]</c> in a real site.</summary>
[Route("tool")]
[RouteName("tool")]
public sealed class CompatBareTemplatePage : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<p id=\"tool\">Tool</p>");
}
