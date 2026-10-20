using Bunit;
using ModRouter.Components;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using static ModRouter.Test.ComponentTestKit;

namespace ModRouter.Test;

/// <summary>A page with state of its own (a counter) that a route keeps alive, and the hooks that report when it is hidden and shown.</summary>
public sealed class KeepProbe : ProbeBase, IKeepAliveRoute
{
    int _count;

    [Parameter]
    public int Id { get; set; }

    public ValueTask OnDeactivatedAsync()
    {
        Probes.Events.Add($"deactivated:{Id}");

        return ValueTask.CompletedTask;
    }

    public ValueTask OnActivatedAsync()
    {
        Probes.Events.Add($"activated:{Id}");

        return ValueTask.CompletedTask;
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "keep");
        builder.AddAttribute(2, "data-id", Id);
        builder.OpenElement(3, "p");
        builder.AddAttribute(4, "class", "count");
        builder.AddContent(5, $"{Id}:{_count}");
        builder.CloseElement();
        builder.OpenElement(6, "button");
        builder.AddAttribute(7, "class", "inc");
        builder.AddAttribute(8, "onclick", EventCallback.Factory.Create(this, () => _count++));
        builder.AddContent(9, "+");
        builder.CloseElement();
        builder.CloseElement();
    }
}

/// <summary>A page that throws from its keep-alive hook.</summary>
public sealed class ThrowingKeepProbe : ProbeBase, IKeepAliveRoute
{
    public ValueTask OnDeactivatedAsync() => throw new InvalidOperationException("hook failed");

    public ValueTask OnActivatedAsync() => ValueTask.CompletedTask;

    protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<p id=\"throwing\">t</p>");
}

/// <summary><c>RouteMap.KeepAlive</c>: a page that stays rendered, hidden, with its state while another page is current.</summary>
[TestFixture]
public sealed class KeepAliveTests
{
    BunitContext _ctx = default!;
    FakeNavigationAdapter _adapter = default!;
    bool _expectsRenderException;

    [SetUp]
    public void SetUp()
    {
        Probes.Reset();
        _expectsRenderException = false;
        _ctx = new BunitContext();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (!_expectsRenderException)
        {
            Assert.That(_ctx.Renderer.UnhandledException.IsCompleted, Is.False, () => "a render exception was swallowed: " + _ctx.Renderer.UnhandledException.Result);
        }

        await _ctx.DisposeAsync();
    }

    IRenderedComponent<Components.ModRouter> Mount(Action<RouteMap> routes, string uri)
    {
        _ctx.Services.AddModRouter(routes);
        _adapter = new FakeNavigationAdapter("http://localhost/", uri) { ExecuteNavigateTo = true };
        _ctx.Services.AddSingleton<INavigationPlatformAdapter>(_adapter);

        return _ctx.Render<Components.ModRouter>(p => p.Add(r => r.DefaultLayout, typeof(MainLayoutProbe)));
    }

    async Task GoAsync(string uri)
    {
        await _adapter.NavigateAsync(uri, intercepted: true);
        await _adapter.SettledAsync();
    }

    static void Routes(RouteMap map) => map
        .Route("/", typeof(HomeProbe), name: "home")
        .Route("/counter/{id:int}", typeof(KeepProbe), name: "counter").KeepAlive(2)
        .Route("/other", typeof(ShowProbe), name: "other");

    static IReadOnlyList<AngleSharp.Dom.IElement> Entries(IRenderedComponent<Components.ModRouter> cut) => All(cut, "[data-mod-keep-alive]");

    static string Counts(IRenderedComponent<Components.ModRouter> cut, string state)
        => string.Join(",", All(cut, $"[data-mod-keep-alive={state}] .count").Select(e => e.TextContent));

    async Task ClickAsync(IRenderedComponent<Components.ModRouter> cut, string id)
    {
        await cut.Find($"[data-mod-keep-alive=active] .keep[data-id='{id}'] .inc").ClickAsync(new());
    }

    [Test]
    public async Task A_kept_page_keeps_its_state_and_instance_while_another_page_is_current()
    {
        var cut = Mount(Routes, "http://localhost/counter/1");
        await EventuallyAsync(() => Counts(cut, "active"), Is.EqualTo("1:0"));

        await ClickAsync(cut, "1");
        await ClickAsync(cut, "1");
        await EventuallyAsync(() => Counts(cut, "active"), Is.EqualTo("1:2"));

        await GoAsync("other");
        await EventuallyAsync(() => All(cut, "#show").Count, Is.EqualTo(1));

        Assert.That(Counts(cut, "inactive"), Is.EqualTo("1:2"), "the page is still there, hidden, with its state");
        Assert.That(One(cut, "[data-mod-keep-alive=inactive]").HasAttribute("inert"), Is.True);
        Assert.That(One(cut, "[data-mod-keep-alive=inactive]").GetAttribute("style"), Is.EqualTo("display:none"));

        await GoAsync("counter/1");

        await EventuallyAsync(() => Counts(cut, "active"), Is.EqualTo("1:2"));
        Assert.That(Probes.Count("ctor:KeepProbe"), Is.EqualTo(1), "one instance for the whole visit");
        Assert.That(Probes.Count("dispose:KeepProbe"), Is.Zero);
        Assert.That(All(cut, "#show"), Is.Empty);
        Assert.That(One(cut, "[data-mod-keep-alive=active]").GetAttribute("style"), Is.EqualTo("display:contents"));
    }

    [Test]
    public async Task Each_set_of_route_values_is_its_own_page_and_the_oldest_hidden_ones_are_dropped_beyond_Max()
    {
        var cut = Mount(Routes, "http://localhost/counter/1");
        await EventuallyAsync(() => Counts(cut, "active"), Is.EqualTo("1:0"));
        await ClickAsync(cut, "1");

        await GoAsync("counter/2");
        await EventuallyAsync(() => Counts(cut, "active"), Is.EqualTo("2:0"));
        Assert.That(Counts(cut, "inactive"), Is.EqualTo("1:1"), "the first page waits, hidden, with its count");

        await GoAsync("counter/3");
        await EventuallyAsync(() => Counts(cut, "active"), Is.EqualTo("3:0"));
        Assert.That(Probes.Count("ctor:KeepProbe"), Is.EqualTo(3));

        await GoAsync("counter/4");
        await EventuallyAsync(() => Counts(cut, "active"), Is.EqualTo("4:0"));

        // Max is 2: pages 3 and 2 are the two most recently shown hidden ones; page 1 is gone.
        Assert.That(Counts(cut, "inactive").Split(',').Order(), Is.EqualTo(new[] { "2:0", "3:0" }));
        Assert.That(Probes.Count("dispose:KeepProbe"), Is.EqualTo(1), "the dropped page's component was disposed");

        await GoAsync("counter/1");
        await EventuallyAsync(() => Counts(cut, "active"), Is.EqualTo("1:0"));
        Assert.That(Probes.Count("ctor:KeepProbe"), Is.EqualTo(5));
    }

    [Test]
    public async Task A_page_that_implements_the_hooks_is_told_when_it_is_hidden_and_shown_again_but_not_on_its_first_show()
    {
        var cut = Mount(Routes, "http://localhost/counter/1");
        await EventuallyAsync(() => Counts(cut, "active"), Is.EqualTo("1:0"));
        Assert.That(Probes.Events.Where(e => e.StartsWith("activated") || e.StartsWith("deactivated")), Is.Empty, "first show: no call");

        await GoAsync("other");
        await EventuallyAsync(() => Probes.Events.Contains("deactivated:1"), Is.True);

        await GoAsync("counter/1");
        await EventuallyAsync(() => Probes.Events.Contains("activated:1"), Is.True);

        Assert.That(Probes.Events.Where(e => e.StartsWith("activated") || e.StartsWith("deactivated")), Is.EqualTo(new[] { "deactivated:1", "activated:1" }));

        await GoAsync("counter/2");
        await EventuallyAsync(() => Probes.Events.Count(e => e == "deactivated:1"), Is.EqualTo(2));
        Assert.That(Probes.Events, Does.Not.Contain("activated:2"), "a page's first show is not a re-activation");
    }

    [Test]
    public async Task A_hook_that_throws_surfaces_as_an_unhandled_exception_of_the_outlet()
    {
        _expectsRenderException = true;
        var cut = Mount(map => map
            .Route("/", typeof(HomeProbe), name: "home")
            .Route("/t", typeof(ThrowingKeepProbe), name: "t").KeepAlive()
            .Route("/other", typeof(ShowProbe), name: "other"), "http://localhost/t");
        await EventuallyAsync(() => All(cut, "#throwing").Count, Is.EqualTo(1));

        await GoAsync("other");

        await EventuallyAsync(() => _ctx.Renderer.UnhandledException.IsCompleted, Is.True);
        Assert.That(_ctx.Renderer.UnhandledException.Result.Message, Is.EqualTo("hook failed"));
    }

    [Test]
    public async Task A_route_that_is_not_kept_leaves_no_wrapper_and_a_router_with_nothing_kept_renders_no_wrappers()
    {
        var cut = Mount(Routes, "http://localhost/other");

        await EventuallyAsync(() => All(cut, "#show").Count, Is.EqualTo(1));

        Assert.That(Entries(cut), Is.Empty);

        await GoAsync("/");
        await EventuallyAsync(() => All(cut, "#home").Count, Is.EqualTo(1));
        Assert.That(Entries(cut), Is.Empty);
    }

    [Test]
    public async Task The_nested_outlet_of_a_kept_page_keeps_its_child_while_hidden_and_follows_the_url_when_the_page_returns()
    {
        var cut = Mount(map => map
            .Route("/", typeof(HomeProbe), name: "home")
            .Route("/shop", typeof(CustomersProbe), name: "shop", children: shop => shop
                .Index(typeof(ListProbe), name: "shop-list")
                .Route("{id:int}", typeof(KeepProbe), name: "item")).KeepAlive()
            .Route("/other", typeof(ShowProbe), name: "other"), "http://localhost/shop/5");
        await EventuallyAsync(() => Counts(cut, "active"), Is.EqualTo("5:0"));
        await ClickAsync(cut, "5");

        await GoAsync("other");
        await EventuallyAsync(() => All(cut, "#show").Count, Is.EqualTo(1));
        Assert.That(Counts(cut, "inactive"), Is.EqualTo("5:1"), "the child of the hidden page is still rendered");

        await GoAsync("shop/7");
        // The child (not kept itself) follows the default reuse policy: the instance stays and takes the new Id.
        await EventuallyAsync(() => Counts(cut, "active"), Is.EqualTo("7:1"));
        Assert.That(Probes.Count("ctor:CustomersProbe"), Is.EqualTo(1), "the parent page was kept; only its child changed");
    }

    [Test]
    public async Task A_denied_or_failed_level_hides_every_kept_page()
    {
        var cut = Mount(map => map
            .Route("/", typeof(HomeProbe), name: "home")
            .Route("/counter/{id:int}", typeof(KeepProbe), name: "counter").KeepAlive()
            .Route("/secret", typeof(ShowProbe), name: "secret").Guard<DenyKeepGuard>(), "http://localhost/counter/1");
        await EventuallyAsync(() => Counts(cut, "active"), Is.EqualTo("1:0"));

        await GoAsync("secret");

        await EventuallyAsync(() => Counts(cut, "active"), Is.EqualTo(string.Empty));
        Assert.That(Counts(cut, "inactive"), Is.EqualTo("1:0"));
    }

    // ---- declaration ------------------------------------------------------------------------------------------------

    [Test]
    public void KeepAlive_needs_a_positive_Max_and_a_route_before_it()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RouteMap.Create().Route("/x", typeof(ShowProbe)).KeepAlive(0));
        Assert.Throws<InvalidOperationException>(() => RouteMap.Create().KeepAlive());
    }

    [Test]
    public void KeepAlive_twice_or_on_a_group_or_a_redirect_is_a_startup_error()
    {
        var twice = Assert.Throws<RouteCompilationException>(() => RouteTestKit.Compile(map => map.Route("/x", typeof(ShowProbe), name: "x").KeepAlive().KeepAlive()))!;
        var group = Assert.Throws<RouteCompilationException>(() => RouteTestKit.Compile(map => map
            .Group(typeof(GroupLayoutProbe), g => g.Route("a", typeof(ShowProbe), name: "a")).KeepAlive()))!;
        var redirect = Assert.Throws<RouteCompilationException>(() => RouteTestKit.Compile(map => map
            .Route("/x", typeof(ShowProbe), name: "x")
            .Redirect("/old", "x").KeepAlive()))!;

        Assert.That(twice.Errors.Select(e => e.Code), Does.Contain("RT0029"));
        Assert.That(group.Errors.Select(e => e.Code), Does.Contain("RT0029"));
        Assert.That(redirect.Errors.Select(e => e.Code), Does.Contain("RT0029"));
    }
}

public sealed class DenyKeepGuard : ModRouter.Core.Guards.IRouteGuard
{
    public ValueTask<ModRouter.Core.Guards.RouteGuardResult> CanActivateAsync(ModRouter.Core.Guards.RouteGuardContext context)
        => ValueTask.FromResult(ModRouter.Core.Guards.RouteGuardResult.NotAuthorized("no"));
}
