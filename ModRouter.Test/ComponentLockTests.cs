using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using ModRouter.Components;
using ModRouter.Core.Definitions;
using ModRouter.Core.Guards;
using ModRouter.Core.Loading;
using ModRouter.Navigation;

namespace ModRouter.Test;

/// <summary>
/// A route component that implements <see cref="IRouteNavigationLock"/> is asked, on the instance on screen, before its level
/// is left and also when its route parameters change; its answer comes before the route-declared leave guards'.
/// </summary>
[TestFixture]
public sealed class ComponentLockTests
{
    /// <summary>What happened, in order; static because the router creates the components.</summary>
    public static List<string> Log { get; } = [];

    public static bool Dirty { get; set; }

    public sealed class FormPage : ComponentBase, IRouteNavigationLock
    {
        [Parameter]
        public int Id { get; set; }

        public ValueTask<RouteLeaveResult> CanLeaveAsync(RouteLeaveContext context)
        {
            Log.Add($"form:{(context.IsParameterChange ? "parameters" : "leave")}@{context.Depth}");

            return ValueTask.FromResult(Dirty ? RouteLeaveResult.Cancel() : RouteLeaveResult.Allow());
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, $"form {Id}");
    }

    public sealed class LockedParent : ComponentBase, IRouteNavigationLock
    {
        public ValueTask<RouteLeaveResult> CanLeaveAsync(RouteLeaveContext context)
        {
            Log.Add($"parent:{(context.IsParameterChange ? "parameters" : "leave")}");

            return ValueTask.FromResult(RouteLeaveResult.Allow());
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.AddContent(0, "parent ");
            builder.OpenComponent<ModRouterOutlet>(1);
            builder.CloseComponent();
        }
    }

    public sealed class LockedChild : ComponentBase, IRouteNavigationLock
    {
        public ValueTask<RouteLeaveResult> CanLeaveAsync(RouteLeaveContext context)
        {
            Log.Add("child");

            return ValueTask.FromResult(RouteLeaveResult.Allow());
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "child");
    }

    public sealed class RecordingLeaveGuard : IRouteLeaveGuard
    {
        public ValueTask<RouteLeaveResult> CanDeactivateAsync(RouteLeaveContext context)
        {
            Log.Add("declared guard");

            return ValueTask.FromResult(RouteLeaveResult.Allow());
        }
    }

    public sealed class RedirectingPage : ComponentBase, IRouteNavigationLock
    {
        public ValueTask<RouteLeaveResult> CanLeaveAsync(RouteLeaveContext context) => ValueTask.FromResult(RouteLeaveResult.Redirect("about"));

        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "redirecting");
    }

    public sealed class ThrowingLoader : IRouteLoader
    {
        public ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context) => throw new InvalidOperationException("boom");
    }

    public sealed class FailedPage : ComponentBase, IRouteNavigationLock
    {
        public ValueTask<RouteLeaveResult> CanLeaveAsync(RouteLeaveContext context)
        {
            Log.Add("failed page asked");

            return ValueTask.FromResult(RouteLeaveResult.Cancel());
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "never shown");
    }

    static void Routes(RouteMap map) => map
        .Route("/", typeof(ShowProbe), name: "home")
        .Route("/about", typeof(ShowProbe), name: "about")
        .Route("/form/{id:int}", typeof(FormPage), name: "form").LeaveGuard<RecordingLeaveGuard>()
        .Route("/section", typeof(LockedParent), name: "section", children: section => section
            .Route("page", typeof(LockedChild), name: "page"))
        .Route("/redirecting", typeof(RedirectingPage), name: "redirecting")
        .Route("/failed", typeof(FailedPage), name: "failed").Loader<ThrowingLoader>();

    BunitContext _ctx = default!;
    IRenderedComponent<Components.ModRouter> _router = default!;

    INavigationService Navigation => _ctx.Services.GetRequiredService<INavigationService>();

    [SetUp]
    public void SetUp()
    {
        Log.Clear();
        Dirty = false;
        _ctx = new BunitContext();
        _ctx.Services.AddModRouter(Routes);
        _ctx.Services.AddSingleton<INavigationPlatformAdapter>(new FakeNavigationAdapter("http://localhost/", "http://localhost/") { ExecuteNavigateTo = true });
        _router = _ctx.Render<Components.ModRouter>();
    }

    [TearDown]
    public async Task TearDown()
    {
        _router.Dispose();
        await _ctx.DisposeAsync();
    }

    [Test]
    public async Task A_page_that_is_dirty_keeps_itself_on_screen()
    {
        await Navigation.NavigateToAsync("form/1");
        Dirty = true;

        var result = await Navigation.NavigateToAsync("about");

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Cancelled));
        Assert.That(Navigation.Current!.Path, Is.EqualTo("/form/1"));
        Assert.That(_router.Markup, Does.Contain("form 1"));
    }

    [Test]
    public async Task A_clean_page_may_be_left()
    {
        await Navigation.NavigateToAsync("form/1");

        var result = await Navigation.NavigateToAsync("about");

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Succeeded));
        Assert.That(Log, Is.EqualTo(new[] { "form:leave@0", "declared guard" }), "the component first, then the route's declared guard");
    }

    [Test]
    public async Task A_parameter_change_on_the_same_route_asks_the_component_only()
    {
        await Navigation.NavigateToAsync("form/1");
        Dirty = true;
        Log.Clear();

        var blocked = await Navigation.NavigateToAsync("form/2");

        Assert.That(blocked.Status, Is.EqualTo(NavigationStatus.Cancelled));
        Assert.That(Log, Is.EqualTo(new[] { "form:parameters@0" }), "the declared leave guard is not asked about a parameter change");
        Assert.That(Navigation.Current!.Path, Is.EqualTo("/form/1"));

        Dirty = false;
        var allowed = await Navigation.NavigateToAsync("form/2");

        Assert.That(allowed.Status, Is.EqualTo(NavigationStatus.Succeeded));
        Assert.That(_router.Markup, Does.Contain("form 2"));
    }

    [Test]
    public async Task A_query_only_navigation_asks_nobody()
    {
        await Navigation.NavigateToAsync("form/1");
        Dirty = true;
        Log.Clear();

        var result = await Navigation.NavigateToAsync("form/1?tab=notes");

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Succeeded));
        Assert.That(Log, Is.Empty);
    }

    [Test]
    public async Task Levels_are_asked_deepest_first_and_a_child_opening_asks_nobody()
    {
        await Navigation.NavigateToAsync("section");
        Log.Clear();

        await Navigation.NavigateToAsync("section/page");
        Assert.That(Log, Is.Empty, "the parent stays while its child opens");

        await Navigation.NavigateToAsync("about");

        Assert.That(Log, Is.EqualTo(new[] { "child", "parent:leave" }));
    }

    [Test]
    public async Task The_component_can_redirect()
    {
        await Navigation.NavigateToAsync("redirecting");

        var result = await Navigation.NavigateToAsync("form/1");

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Redirected));
        Assert.That(result.RedirectedTo, Does.EndWith("/about"));
    }

    [Test]
    public async Task A_page_that_showed_an_error_is_not_asked()
    {
        var failed = await Navigation.NavigateToAsync("failed");
        Assert.That(failed.Status, Is.EqualTo(NavigationStatus.Failed));

        var result = await Navigation.NavigateToAsync("about");

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Succeeded));
        Assert.That(Log, Does.Not.Contain("failed page asked"));
    }

    [Test]
    public async Task Once_the_page_is_gone_its_instance_is_no_longer_asked()
    {
        await Navigation.NavigateToAsync("form/1");
        await Navigation.NavigateToAsync("about");
        Log.Clear();
        Dirty = true;

        var result = await Navigation.NavigateToAsync("section");

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Succeeded));
        Assert.That(Log, Is.Empty);
    }
}
