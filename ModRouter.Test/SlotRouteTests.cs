using Bunit;
using ModRouter.Components;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Guards;
using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using static ModRouter.Test.ComponentTestKit;

namespace ModRouter.Test;

/// <summary>A layout with a named outlet beside the page: <c>side</c> is filled by the route's slot, the body by its page.</summary>
public sealed class SlotLayoutProbe : LayoutComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", "slot-layout");
        builder.OpenElement(2, "aside");
        builder.AddAttribute(3, "id", "side");
        builder.OpenComponent<ModRouterOutlet>(4);
        builder.AddComponentParameter(5, nameof(ModRouterOutlet.Name), "Side");
        builder.AddComponentParameter(6, nameof(ModRouterOutlet.EmptyContent), (RenderFragment)(b => b.AddMarkupContent(0, "<i id=\"side-empty\">none</i>")));
        builder.CloseComponent();
        builder.CloseElement();
        builder.OpenElement(7, "main");
        builder.AddAttribute(8, "id", "main");
        builder.AddContent(9, Body);
        builder.CloseElement();
        builder.CloseElement();
    }
}

public sealed class SideA : ProbeBase
{
    [Parameter]
    public int Id { get; set; }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddAttribute(1, "id", "side-a");
        builder.AddContent(2, $"A{Id}");
        builder.CloseElement();
    }
}

public sealed class SideB : ProbeBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddAttribute(1, "id", "side-b");
        builder.AddContent(2, "B");
        builder.CloseElement();
    }
}

public sealed class DenySlotGuard : IRouteGuard
{
    public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context) => ValueTask.FromResult(RouteGuardResult.NotAuthorized("no"));
}

/// <summary>Named outlets (<c>ModRouterOutlet Name</c>) filled by route slots (<c>RouteMap.Slot</c>).</summary>
[TestFixture]
public sealed class SlotRouteTests
{
    BunitContext _ctx = default!;
    FakeNavigationAdapter _adapter = default!;

    [SetUp]
    public void SetUp()
    {
        Probes.Reset();
        _ctx = new BunitContext();
    }

    [TearDown]
    public async Task TearDown()
    {
        Assert.That(_ctx.Renderer.UnhandledException.IsCompleted, Is.False, () => "a render exception was swallowed: " + _ctx.Renderer.UnhandledException.Result);

        await _ctx.DisposeAsync();
    }

    IRenderedComponent<Components.ModRouter> Mount(Action<RouteMap> routes, string uri)
    {
        _ctx.Services.AddModRouter(routes);
        _adapter = new FakeNavigationAdapter("http://localhost/", uri) { ExecuteNavigateTo = true };
        _ctx.Services.AddSingleton<INavigationPlatformAdapter>(_adapter);

        return _ctx.Render<Components.ModRouter>(p => p.Add(r => r.DefaultLayout, typeof(SlotLayoutProbe)));
    }

    async Task GoAsync(string uri)
    {
        await _adapter.NavigateAsync(uri, intercepted: true);
        await _adapter.SettledAsync();
    }

    [Test]
    public async Task A_route_fills_the_named_outlet_and_a_route_without_a_slot_shows_the_empty_content()
    {
        var cut = Mount(map => map
            .Route("/", typeof(HomeProbe), name: "home")
            .Route("/customers/{id:int}", typeof(DetailsProbe), name: "customer").Slot("side", typeof(SideA))
            .Route("/other", typeof(ShowProbe), name: "other").Slot("side", typeof(SideB)), "http://localhost/customers/7");

        await EventuallyAsync(() => All(cut, "#side #side-a").Select(e => e.TextContent).SingleOrDefault(), Is.EqualTo("A7"));
        Assert.That(All(cut, "#main #side-a"), Is.Empty, "the slot renders in the named outlet, not in the page's");

        await GoAsync("other");
        await EventuallyAsync(() => All(cut, "#side #side-b").Count, Is.EqualTo(1));
        Assert.That(All(cut, "#side-a"), Is.Empty);

        await GoAsync("/");
        await EventuallyAsync(() => All(cut, "#side #side-empty").Count, Is.EqualTo(1));
        Assert.That(All(cut, "#side-b"), Is.Empty);
    }

    [Test]
    public async Task A_slot_receives_the_route_parameters_of_its_level_and_follows_them()
    {
        var cut = Mount(map => map
            .Route("/customers/{id:int}", typeof(DetailsProbe), name: "customer").Slot("side", typeof(SideA)), "http://localhost/customers/1");

        await EventuallyAsync(() => All(cut, "#side-a").Select(e => e.TextContent).SingleOrDefault(), Is.EqualTo("A1"));

        await GoAsync("customers/2");

        await EventuallyAsync(() => All(cut, "#side-a").Select(e => e.TextContent).SingleOrDefault(), Is.EqualTo("A2"));
        Assert.That(Probes.Count("ctor:SideA"), Is.EqualTo(1), "the same slot component keeps its instance across a parameter change");
    }

    [Test]
    public async Task The_deepest_level_that_declares_the_slot_wins_and_the_name_is_case_insensitive()
    {
        var cut = Mount(map => map
            .Route("/customers", typeof(CustomersProbe), name: "customers", children: customers => customers
                .Index(typeof(ListProbe), name: "list")
                .Route("{id:int}", typeof(DetailsProbe), name: "customer").Slot("SIDE", typeof(SideA)))
            .Slot("side", typeof(SideB)), "http://localhost/customers");

        await EventuallyAsync(() => All(cut, "#side #side-b").Count, Is.EqualTo(1));

        await GoAsync("customers/3");

        await EventuallyAsync(() => All(cut, "#side #side-a").Count, Is.EqualTo(1));
        Assert.That(All(cut, "#side-b"), Is.Empty);
    }

    [Test]
    public async Task A_state_with_an_error_shows_no_slot()
    {
        var cut = Mount(map => map
            .Route("/", typeof(HomeProbe), name: "home").Slot("side", typeof(SideA)), "http://localhost/");

        await EventuallyAsync(() => All(cut, "#side #side-a").Count, Is.EqualTo(1));

        await GoAsync("nowhere");

        await EventuallyAsync(() => All(cut, "#side-a").Count, Is.EqualTo(0));
    }

    [Test]
    public async Task A_level_the_guard_denied_shows_no_slot_of_the_page_it_did_not_admit()
    {
        var cut = Mount(map => map
            .Route("/", typeof(HomeProbe), name: "home").Slot("side", typeof(SideB))
            .Route("/secret", typeof(ShowProbe), name: "secret").Guard<DenySlotGuard>().Slot("side", typeof(SideA)), "http://localhost/");

        await EventuallyAsync(() => All(cut, "#side #side-b").Count, Is.EqualTo(1));

        await GoAsync("secret");

        await EventuallyAsync(() => All(cut, "#side-b").Count, Is.EqualTo(0));
        Assert.That(All(cut, "#side-a"), Is.Empty);
        Assert.That(All(cut, "#side-empty"), Has.Count.EqualTo(1));
    }

    [Test]
    public void A_slot_declared_twice_or_with_a_non_component_is_a_startup_error()
    {
        var duplicate = Assert.Throws<RouteCompilationException>(() => RouteTestKit.Compile(map => map
            .Route("/", typeof(HomeProbe), name: "home").Slot("side", typeof(SideA)).Slot("Side", typeof(SideB))))!;
        var wrong = Assert.Throws<RouteCompilationException>(() => RouteTestKit.Compile(map => map
            .Route("/", typeof(HomeProbe), name: "home").Slot("side", typeof(string))))!;

        Assert.That(duplicate.Errors.Select(e => e.Code), Does.Contain("RT0028"));
        Assert.That(wrong.Errors.Select(e => e.Code), Does.Contain("RT0028"));
    }

    [Test]
    public void The_slot_call_validates_its_arguments()
    {
        var map = RouteMap.Create().Route("/", typeof(HomeProbe));

        Assert.Throws<ArgumentException>(() => map.Slot(" ", typeof(SideA)));
        Assert.Throws<ArgumentNullException>(() => map.Slot("side", null!));
        Assert.Throws<InvalidOperationException>(() => RouteMap.Create().Slot("side", typeof(SideA)));
    }

    [Test]
    public async Task A_named_outlet_does_not_take_a_depth_of_the_chain()
    {
        var cut = Mount(map => map
            .Route("/customers", typeof(CustomersProbe), name: "customers", children: customers => customers
                .Route("{id:int}", typeof(DetailsProbe), name: "customer")).Slot("side", typeof(SideB)), "http://localhost/customers/5");

        await EventuallyAsync(() => All(cut, "#side #side-b").Count, Is.EqualTo(1));
        Assert.That(All(cut, "#main #customers"), Has.Count.EqualTo(1), "the page's own outlet still renders both levels of the chain");
    }
}
