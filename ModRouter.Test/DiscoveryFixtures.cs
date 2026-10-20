// Page components for the discovery tests. Each sub-namespace is one "set" that AddPages is pointed at through its
// filter: Pages.App is the good set (every attribute); the others each hold one mistake.
using ModRouter.Core.Guards;
using ModRouter.Core.Loading;
using ModRouter.Discovery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace ModRouter.Test.Pages
{
    /// <summary>A page that renders its name in an element with that id, with an outlet for children.</summary>
    public abstract class FixturePage : ComponentBase
    {
        protected abstract string ElementId { get; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "section");
            builder.AddAttribute(1, "id", ElementId);
            builder.AddContent(2, Text());
            builder.OpenComponent<Components.ModRouterOutlet>(3);
            builder.CloseComponent();
            builder.CloseElement();
        }

        protected virtual string Text() => ElementId;
    }

    public sealed class AllowAllGuard : IRouteGuard
    {
        public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context) => ValueTask.FromResult(RouteGuardResult.Allow());
    }

    public sealed class RecordingGuard : IRouteGuard
    {
        public static List<string> Calls { get; } = [];

        public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context)
        {
            Calls.Add(context.Level.RouteName ?? context.Level.Template);

            return ValueTask.FromResult(RouteGuardResult.Allow());
        }
    }

    public sealed class EmptyLoader : IRouteLoader
    {
        public ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context) => ValueTask.FromResult(RouteLoadResult.Create("loaded"));
    }
}

namespace ModRouter.Test.Pages.App
{
    [Route("/")]
    [RouteName("home")]
    [RouteTitle("Home")]
    public sealed class HomePage : FixturePage
    {
        protected override string ElementId => "home";
    }

    /// <summary>A multi-@page component that lists the parameterless template first, like most real ones.</summary>
    [Route("/org-chart")]
    [Route("/org-chart/{employeeId:int}")]
    [RouteName("org-chart")]
    [RouteTitle("Org chart")]
    public sealed class OrgChartPage : FixturePage
    {
        [Parameter]
        public int? EmployeeId { get; set; }

        protected override string ElementId => "org-chart";

        protected override string Text() => EmployeeId is null ? "org-chart" : $"employee {EmployeeId}";
    }

    [Route("/customers")]
    [RouteName("customers")]
    [RouteTitle("Customers")]
    [RouteBreadcrumb("All customers")]
    [RouteGuard<AllowAllGuard>]
    [RouteGuard(typeof(RecordingGuard))]
    [RouteLoader<EmptyLoader>(RouteReloadPolicy.Never)]
    public sealed class CustomersPage : FixturePage
    {
        protected override string ElementId => "customers";
    }

    [Route("/customers")]
    [RouteParent(typeof(CustomersPage))]
    [RouteIndex]
    [RouteName("customer-list")]
    public sealed class CustomerListPage : FixturePage
    {
        protected override string ElementId => "customer-list";
    }

    [Route("/customers/{Id:int}")]
    [RouteParent(typeof(CustomersPage))]
    [RouteName("customer")]
    [RouteFocus("#customer")]
    [RouteNoAnnounce]
    public sealed class CustomerPage : FixturePage
    {
        [Parameter]
        public int Id { get; set; }

        protected override string ElementId => "customer";

        protected override string Text() => $"customer {Id}";
    }

    [Route("/customers/{Id:int}/orders/{OrderId:guid}")]
    [RouteParent(typeof(CustomerPage))]
    [RouteName("order")]
    [RouteTitle("Order")]
    [RouteBreadcrumb(Hidden = true)]
    [RouteLoader(typeof(EmptyLoader))]
    public sealed class OrderPage : FixturePage
    {
        [Parameter]
        public int Id { get; set; }

        [Parameter]
        public Guid OrderId { get; set; }

        protected override string ElementId => "order";

        protected override string Text() => $"order {OrderId} of {Id}";
    }

    [Route("/vendors")]
    [Route("/suppliers")]
    [RouteName("vendors")]
    [RouteCanonicalAlias]
    public sealed class VendorsPage : FixturePage
    {
        protected override string ElementId => "vendors";
    }

    /// <summary>A relative child template, written through the attribute (Razor's @page insists on a leading '/').</summary>
    [Route("{Id:int}")]
    [RouteParent(typeof(VendorsPage))]
    [RouteName("vendor")]
    public sealed class VendorPage : FixturePage
    {
        [Parameter]
        public int Id { get; set; }

        protected override string ElementId => "vendor";
    }

    [Route("/abstract")]
    public abstract class AbstractPage : FixturePage
    {
    }

    [Route("/generic")]
    public sealed class GenericPage<T> : FixturePage
    {
        protected override string ElementId => "generic";
    }

#if NET9_0_OR_GREATER
    // The attribute exists from .NET 9; page discovery honors it there and has nothing to honor on .NET 8.
    [Route("/excluded")]
    [ExcludeFromInteractiveRouting]
    public sealed class ExcludedPage : FixturePage
    {
        protected override string ElementId => "excluded";
    }
#endif

    public sealed class UnroutedComponent : FixturePage
    {
        protected override string ElementId => "unrouted";
    }

    [Route("/plain")]
    public sealed class RoutedButNotAComponent
    {
    }

    [Route("/filtered")]
    public sealed class FilteredPage : FixturePage
    {
        protected override string ElementId => "filtered";
    }
}

namespace ModRouter.Test.Pages.Elsewhere
{
    [Route("/elsewhere")]
    public sealed class NotInTheSetPage : FixturePage
    {
        protected override string ElementId => "elsewhere";
    }
}

namespace ModRouter.Test.Pages.UnknownParent
{
    [Route("/orphan")]
    [RouteParent(typeof(Elsewhere.NotInTheSetPage))]
    public sealed class OrphanPage : FixturePage
    {
        protected override string ElementId => "orphan";
    }
}

namespace ModRouter.Test.Pages.Cycle
{
    [Route("/a")]
    [RouteParent(typeof(CycleBPage))]
    public sealed class CycleAPage : FixturePage
    {
        protected override string ElementId => "a";
    }

    [Route("/b")]
    [RouteParent(typeof(CycleAPage))]
    public sealed class CycleBPage : FixturePage
    {
        protected override string ElementId => "b";
    }

    [Route("/c")]
    [RouteParent(typeof(CycleAPage))]
    public sealed class HangsOffTheCyclePage : FixturePage
    {
        protected override string ElementId => "c";
    }
}

namespace ModRouter.Test.Pages.IndexWithoutParent
{
    [Route("/lonely")]
    [RouteIndex]
    public sealed class LonelyIndexPage : FixturePage
    {
        protected override string ElementId => "lonely";
    }
}

namespace ModRouter.Test.Pages.IndexWithTemplate
{
    [Route("/p")]
    public sealed class ParentPage : FixturePage
    {
        protected override string ElementId => "p";
    }

    [Route("/p/extra")]
    [RouteParent(typeof(ParentPage))]
    [RouteIndex]
    public sealed class WrongIndexPage : FixturePage
    {
        protected override string ElementId => "wrong-index";
    }
}

namespace ModRouter.Test.Pages.BadGuard
{
    [Route("/bad-guard")]
    [RouteGuard(typeof(string))]
    public sealed class BadGuardPage : FixturePage
    {
        protected override string ElementId => "bad-guard";
    }
}

namespace ModRouter.Test.Pages.BadLoader
{
    [Route("/bad-loader")]
    [RouteLoader(typeof(FixturePage))]
    public sealed class BadLoaderPage : FixturePage
    {
        protected override string ElementId => "bad-loader";
    }
}

namespace ModRouter.Test.Pages.BadBreadcrumb
{
    [Route("/no-label")]
    [RouteBreadcrumb]
    public sealed class NoLabelPage : FixturePage
    {
        protected override string ElementId => "no-label";
    }

    [Route("/label-and-hidden")]
    [RouteBreadcrumb("Label", Hidden = true)]
    public sealed class LabelAndHiddenPage : FixturePage
    {
        protected override string ElementId => "label-and-hidden";
    }
}

namespace ModRouter.Test.Pages.BadNesting
{
    [Route("/p")]
    public sealed class ParentPage : FixturePage
    {
        protected override string ElementId => "p";
    }

    [Route("/q/{id:int}")]
    [RouteParent(typeof(ParentPage))]
    public sealed class StrangerPage : FixturePage
    {
        protected override string ElementId => "stranger";
    }
}

namespace ModRouter.Test.Pages.Conflicts
{
    [Route("/dup")]
    [RouteName("home")]
    public sealed class DuplicateNamePage : FixturePage
    {
        protected override string ElementId => "dup";
    }

    [Route("/")]
    public sealed class SameTemplatePage : FixturePage
    {
        protected override string ElementId => "same";
    }
}

namespace ModRouter.Test.Pages.FluentParent
{
    /// <summary>Nested under a route the fluent map declared with the same component type.</summary>
    [Route("/customers/{Id:int}/notes")]
    [RouteParent(typeof(CustomersProbe))]
    [RouteName("notes")]
    public sealed class NotesPage : FixturePage
    {
        [Parameter]
        public int Id { get; set; }

        protected override string ElementId => "notes";
    }
}

namespace ModRouter.Test.Pages.BareTemplate
{
    /// <summary>
    /// A top-level page whose templates have no leading '/', as <c>@attribute [Route("document-expiry-tool")]</c> in a real
    /// site (only the attribute form allows it; Razor's <c>@page</c> insists on the slash). The built-in router accepts it.
    /// </summary>
    [Route("tool")]
    [Route("tools/{Id:int}")]
    [RouteName("tool")]
    public sealed class ToolPage : FixturePage
    {
        [Parameter]
        public int? Id { get; set; }

        protected override string ElementId => "tool";
    }

    /// <summary>A child under a bare-template parent whose own template is relative already: the child rule is unchanged.</summary>
    [Route("{Section:alpha}")]
    [RouteParent(typeof(ToolPage))]
    [RouteName("tool-section")]
    public sealed class ToolSectionPage : FixturePage
    {
        [Parameter]
        public string? Section { get; set; }

        protected override string ElementId => "tool-section";
    }
}

namespace ModRouter.Test.Pages.Authorized
{
    /// <summary>[Authorize(Roles)] next to a [RouteGuard]: the authorization guard must come first.</summary>
    [Route("/secure")]
    [RouteName("secure")]
    [Authorize(Roles = "admin")]
    [RouteGuard(typeof(RecordingGuard))]
    public sealed class SecurePage : FixturePage
    {
        protected override string ElementId => "secure";
    }

    /// <summary>A nested page, contributed at its own depth.</summary>
    [Route("/secure/{Id:int}")]
    [RouteParent(typeof(SecurePage))]
    [RouteName("secure-child")]
    [Authorize]
    public sealed class SecureChildPage : FixturePage
    {
        [Parameter]
        public int Id { get; set; }

        protected override string ElementId => "secure-child";
    }

    /// <summary>[Authorize] + [AllowAnonymous], what every real site's Index page carries through _Imports.razor.</summary>
    [Route("/open")]
    [RouteName("open")]
    [Authorize]
    [AllowAnonymous]
    public sealed class OpenPage : FixturePage
    {
        protected override string ElementId => "open";
    }
}

namespace ModRouter.Test.Pages.Slotted
{
    [Route("/slotted")]
    [RouteName("slotted")]
    [RouteSlot("side", typeof(SidePanel))]
    [RouteSlot("tools", typeof(SidePanel))]
    public sealed class SlottedPage : FixturePage
    {
        protected override string ElementId => "slotted";
    }

    public sealed class SidePanel : FixturePage
    {
        protected override string ElementId => "side-panel";
    }
}

namespace ModRouter.Test.Pages.BadSlot
{
    [Route("/bad-slot")]
    [RouteSlot("side", typeof(string))]
    public sealed class BadSlotPage : FixturePage
    {
        protected override string ElementId => "bad-slot";
    }
}

namespace ModRouter.Test.Pages.Kept
{
    [Route("/kept")]
    [RouteName("kept")]
    [RouteKeepAlive(4)]
    public sealed class KeptPage : FixturePage
    {
        protected override string ElementId => "kept";
    }
}

namespace ModRouter.Test.Pages.BadKeep
{
    [Route("/bad-keep")]
    [RouteKeepAlive(0)]
    public sealed class BadKeepPage : FixturePage
    {
        protected override string ElementId => "bad-keep";
    }
}
