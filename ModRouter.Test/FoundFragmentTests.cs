using Bunit;
using ModRouter.Components;
using ModRouter.Core.Definitions;
using ModRouter.Core.Loading;
using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using static ModRouter.Test.ComponentTestKit;

namespace ModRouter.Test;

/// <summary>
/// <c>ModRouter.Found</c>, the hook the compatibility Router builds on: a matched chain renders through the application's
/// fragment with a <c>RouteData</c> for the depth-0 component, cached per committed state so a fixed-shape fragment keeps
/// its layout and page instances; loading, not-found and router-level errors render bare.
/// </summary>
[TestFixture]
public sealed class FoundFragmentTests
{
    sealed class GatedLoader : IRouteLoader
    {
        public static TaskCompletionSource Gate { get; set; } = new();

        public async ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context)
        {
            await Gate.Task.WaitAsync(context.CancellationToken);

            return RouteLoadResult.Create("loaded");
        }
    }

    sealed class ThrowingStage : INavigationStage
    {
        public ValueTask<StageResult> RunAsync(RouteNavigationContext context) => throw new InvalidOperationException("stage broke");
    }

    BunitContext _ctx = default!;
    FakeNavigationAdapter _adapter = default!;
    readonly List<RouteData> _found = [];

    [SetUp]
    public void SetUp()
    {
        Probes.Reset();
        _found.Clear();
        GatedLoader.Gate = new TaskCompletionSource();
        _ctx = new BunitContext();
        _ctx.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [TearDown]
    public async Task TearDown()
    {
        Assert.That(_ctx.Renderer.UnhandledException.IsCompleted, Is.False, () => "a render exception was swallowed: " + _ctx.Renderer.UnhandledException.Result);
        GatedLoader.Gate.TrySetResult();
        await _ctx.DisposeAsync();
    }

    RouteStateStore Store => _ctx.Services.GetRequiredService<RouteStateStore>();

    static void Routes(RouteMap map) => map
        .Route("/", typeof(HomeProbe), name: "home")
        .Route("/customers", typeof(CustomersProbe), name: "customers", children: customers => customers
            .Route("{id:int}", typeof(DetailsProbe), name: "customer-details"))
        .Route("/slow", typeof(HomeProbe), name: "slow").Loader<GatedLoader>();

    /// <summary>The compatibility shape: the fragment owns the layout and places the outlet inside it.</summary>
    RenderFragment<RouteData> Found => routeData => builder =>
    {
        _found.Add(routeData);
        builder.OpenComponent<LayoutView>(0);
        builder.AddComponentParameter(1, nameof(LayoutView.Layout), typeof(MainLayoutProbe));
        builder.AddComponentParameter(2, nameof(LayoutView.ChildContent), (RenderFragment)(body =>
        {
            body.OpenComponent<ModRouterOutlet>(0);
            body.CloseComponent();
        }));
        builder.CloseComponent();
    };

    IRenderedComponent<Components.ModRouter> Mount(string uri, IEnumerable<INavigationStage>? stages = null, bool errorFragment = true)
    {
        _ctx.Services.AddModRouter(Routes);

        foreach (var stage in stages ?? [])
        {
            _ctx.Services.AddSingleton(stage);
        }

        _adapter = new FakeNavigationAdapter("http://localhost/", uri) { ExecuteNavigateTo = true };
        _ctx.Services.AddSingleton<INavigationPlatformAdapter>(_adapter);

        return _ctx.Render<Components.ModRouter>(p =>
        {
            p.Add(r => r.DefaultLayout, typeof(AltLayoutProbe));
            p.Add(r => r.Found, Found);
            p.Add(r => r.Loading, Markup("<p id=\"loading\">Loading...</p>"));
            p.Add(r => r.NotFound, Markup("<p id=\"not-found\">Not found.</p>"));

            if (errorFragment)
            {
                p.Add(r => r.Error, error => b => b.AddMarkupContent(0, $"<p id=\"route-error\">{error.Kind}</p>"));
            }
        });
    }

    async Task GoAsync(string uri)
    {
        Assert.That(await _adapter.NavigateAsync(uri, intercepted: true), Is.True);
        await _adapter.SettledAsync();
    }

    [Test]
    public void A_matched_chain_renders_through_the_fragment_with_a_RouteData_for_the_depth_0_component()
    {
        var cut = Mount("http://localhost/customers/42");

        Assert.That(cut.Find("#layout #customers #details").TextContent, Does.StartWith("Customer 42"));
        Assert.That(_found.Distinct(), Has.Exactly(1).Items, "one RouteData per committed state, however often the router renders");

        var routeData = _found[0];
        Assert.That(routeData.PageType, Is.EqualTo(typeof(CustomersProbe)), "the depth-0 component, not the leaf");
        Assert.That(routeData.RouteValues, Is.Empty, "the depth-0 level declares no parameters; the child's {id} is not the page's");
        Assert.That(routeData.Template, Is.EqualTo("/customers"));
        Assert.That(cut.FindAll("#alt"), Is.Empty, "DefaultLayout is not used: the fragment owns the layout");
    }

    [Test]
    public async Task The_fragment_re_renders_per_navigation_while_the_layout_and_the_parent_instances_survive()
    {
        var cut = Mount("http://localhost/customers");
        Assert.That(cut.Find("#layout #customers"), Is.Not.Null);

        await GoAsync("customers/42");
        await GoAsync("customers/43");

        Assert.That(cut.Find("#layout #customers #details").TextContent, Does.StartWith("Customer 43"));
        Assert.That(Probes.Count("ctor:MainLayoutProbe"), Is.EqualTo(1), "the layout survives every navigation");
        Assert.That(Probes.Count("ctor:CustomersProbe"), Is.EqualTo(1), "the depth-0 parent is retained");
        Assert.That(Probes.Count("ctor:DetailsProbe"), Is.EqualTo(1), "the child is updated in place");
        Assert.That(Probes.Count("params:DetailsProbe"), Is.EqualTo(2));
        Assert.That(_found.Distinct(), Has.Exactly(3).Items, "a new RouteData per committed state, as the built-in Router gives its Found fragment");
        Assert.That(_found.Distinct().Select(r => r.PageType), Is.All.EqualTo(typeof(CustomersProbe)));
    }

    [Test]
    public async Task Not_found_renders_bare_and_moving_to_it_lets_the_fragments_layout_go()
    {
        var cut = Mount("http://localhost/");
        Assert.That(cut.Find("#layout #home"), Is.Not.Null);

        await GoAsync("nope");

        Assert.That(cut.Find("#not-found"), Is.Not.Null);
        Assert.That(cut.FindAll("#layout"), Is.Empty, "the fragment is not rendered, so neither is its layout: the application's NotFound brings its own, like the built-in");
        Assert.That(cut.FindAll("#alt"), Is.Empty, "DefaultLayout does not wrap it either");
        Assert.That(Probes.Count("dispose:MainLayoutProbe"), Is.EqualTo(1));
        Assert.That(Store.Current!.Failure, Is.EqualTo(RouteFailure.NotFound));
    }

    [Test]
    public async Task Loading_renders_bare_until_the_first_navigation_commits()
    {
        var cut = Mount("http://localhost/slow");

        Assert.That(cut.Find("#loading"), Is.Not.Null);
        Assert.That(cut.FindAll("#layout"), Is.Empty);
        Assert.That(cut.FindAll("#alt"), Is.Empty);
        Assert.That(_found, Is.Empty);

        GatedLoader.Gate.SetResult();
        await EventuallyAsync(() => All(cut, "#layout #home").Count, Is.EqualTo(1));
        Assert.That(All(cut, "#loading"), Is.Empty);
    }

    [Test]
    public void A_router_level_error_renders_bare_through_the_Error_fragment()
    {
        var cut = Mount("http://localhost/", [new ThrowingStage()]);

        Assert.That(cut.Find("#route-error").TextContent, Is.EqualTo(nameof(RouteErrorKind.NavigationFailed)));
        Assert.That(cut.FindAll("#layout"), Is.Empty);
        Assert.That(_found, Is.Empty);
    }

    [Test]
    public void A_router_level_error_in_the_Found_shape_is_inside_the_error_host()
    {
        var cut = Mount("http://localhost/", [new ThrowingStage()]);

        AssertInErrorHost(cut.Find("#route-error"));
    }

    [Test]
    public void The_default_alert_of_a_router_level_error_in_the_Found_shape_is_inside_the_error_host()
    {
        var cut = Mount("http://localhost/", [new ThrowingStage()], errorFragment: false);

        var alert = cut.Find("[role=alert]");
        Assert.That(alert.GetAttribute("data-mod-route-error"), Is.EqualTo(nameof(RouteErrorKind.NavigationFailed)));
        AssertInErrorHost(alert);
    }
}
