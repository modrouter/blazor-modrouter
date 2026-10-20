using Bunit;
using ModRouter.Components;
using ModRouter.Core.Definitions;
using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace ModRouter.Test;

[TestFixture]
public sealed class RouterComponentTests
{
    BunitContext _ctx = default!;
    FakeNavigationAdapter _adapter = default!;
    bool _expectsRenderException;

    sealed class TwoRoutersProbe : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<Components.ModRouter>(0);
            builder.CloseComponent();
            builder.OpenComponent<Components.ModRouter>(1);
            builder.CloseComponent();
        }
    }

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
        // bUnit does not rethrow an exception from a render triggered inside InvokeAsync (a navigation-driven
        // render), so a swallowed one would otherwise go unnoticed. Tests that provoke one deliberately opt out.
        if (!_expectsRenderException)
        {
            Assert.That(_ctx.Renderer.UnhandledException.IsCompleted, Is.False, () => "a render exception was swallowed: " + _ctx.Renderer.UnhandledException.Result);
        }

        await _ctx.DisposeAsync();
    }

    static void CustomerRoutes(RouteMap map) => map
        .Route("/", typeof(HomeProbe), name: "home")
        .Route("/customers", typeof(CustomersProbe), name: "customers", children: customers => customers
            .Index(typeof(ListProbe), name: "customer-list")
            .Route("{id:int}", typeof(DetailsProbe), name: "customer-details", children: customer => customer
                .Route("orders/{orderId:guid}", typeof(OrderProbe), name: "customer-order-details")));

    /// <summary>The scoped store the mounted router commits to; reading it proves a navigation really committed.</summary>
    RouteStateStore Store => _ctx.Services.GetRequiredService<RouteStateStore>();

    IRenderedComponent<Components.ModRouter> Mount(
        Action<RouteMap> routes,
        string uri = "http://localhost/",
        Type? layout = null,
        RenderFragment? notFound = null,
        string baseUri = "http://localhost/")
    {
        _ctx.Services.AddModRouter(routes);
        _adapter = new FakeNavigationAdapter(baseUri, uri);
        _ctx.Services.AddSingleton<INavigationPlatformAdapter>(_adapter);

        return _ctx.Render<Components.ModRouter>(p =>
        {
            if (layout is not null)
            {
                p.Add(r => r.DefaultLayout, layout);
            }

            if (notFound is not null)
            {
                p.Add(r => r.NotFound, notFound);
            }
        });
    }

    // ---- rendering ----------------------------------------------------------------------------------------------

    [Test]
    public void Renders_the_matched_chain_inside_the_default_layout()
    {
        var cut = Mount(CustomerRoutes, "http://localhost/customers/42", layout: typeof(MainLayoutProbe));

        Assert.That(cut.Find("#layout #customers #details").TextContent, Does.Contain("Customer 42"));
    }

    [Test]
    public void Without_a_layout_the_chain_renders_bare()
    {
        var cut = Mount(CustomerRoutes, "http://localhost/customers/42");

        Assert.That(cut.FindAll("#layout"), Is.Empty);
        Assert.That(cut.Find("#customers #details"), Is.Not.Null);
    }

    [Test]
    public void The_index_route_renders_in_the_parents_outlet()
    {
        var cut = Mount(CustomerRoutes, "http://localhost/customers");

        Assert.That(cut.Find("#customers #list"), Is.Not.Null);
        Assert.That(cut.FindAll("#details"), Is.Empty);
    }

    [Test]
    public void The_grandchild_receives_its_own_and_its_ancestors_parameters()
    {
        var cut = Mount(CustomerRoutes, "http://localhost/customers/42/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77");

        Assert.That(cut.Find("#customers #details #order").TextContent, Is.EqualTo("Order 4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77 of customer 42"));
    }

    [Test]
    public void A_parent_without_an_index_renders_the_outlets_empty_content()
    {
        var cut = Mount(map => map
            .Route("/customers", typeof(CustomersWithEmptyProbe), children: c => c.Route("{id:int}", typeof(DetailsProbe))), "http://localhost/customers");

        Assert.That(cut.Find("#customers #empty").TextContent, Is.EqualTo("pick a customer"));
    }

    [Test]
    public void A_route_component_can_declare_its_layout_with_the_Layout_attribute()
    {
        var cut = Mount(map => map.Route("/alt", typeof(AltLayoutPageProbe)), "http://localhost/alt", layout: typeof(MainLayoutProbe));

        Assert.That(cut.Find("#alt #alt-page"), Is.Not.Null);
        Assert.That(cut.FindAll("#layout"), Is.Empty);
    }

    [Test]
    public void Not_found_renders_the_not_found_content_inside_the_default_layout()
    {
        var cut = Mount(
            CustomerRoutes,
            "http://localhost/nope",
            layout: typeof(MainLayoutProbe),
            notFound: b => b.AddMarkupContent(0, "<p id=\"nf\">nothing here</p>"));

        Assert.That(cut.Find("#layout #nf").TextContent, Is.EqualTo("nothing here"));
    }

    [Test]
    public void A_value_that_cannot_bind_renders_the_not_found_content()
    {
        var cut = Mount(
            map => map.Route("/g/{id}", typeof(GuidProbe)),
            "http://localhost/g/not-a-guid",
            notFound: b => b.AddMarkupContent(0, "<p id=\"nf\">bad</p>"));

        Assert.That(cut.Find("#nf"), Is.Not.Null);
        Assert.That(cut.FindAll("#guid"), Is.Empty);
    }

    // ---- navigation and component lifetime ----------------------------------------------------------------------

    [Test]
    public async Task Navigating_updates_the_rendered_content()
    {
        var cut = Mount(CustomerRoutes, "http://localhost/", layout: typeof(MainLayoutProbe));
        Assert.That(cut.Find("#home"), Is.Not.Null);

        await _adapter.NavigateAsync("customers/7", intercepted: true);

        Assert.That(cut.Find("#layout #customers #details").TextContent, Does.Contain("Customer 7"));
        Assert.That(cut.FindAll("#home"), Is.Empty);
    }

    [Test]
    public async Task A_parent_is_retained_when_only_its_child_changes()
    {
        var cut = Mount(CustomerRoutes, "http://localhost/customers");

        await _adapter.NavigateAsync("customers/42");

        Assert.That(Probes.Count("ctor:CustomersProbe"), Is.EqualTo(1), "the parent must not be recreated");
        Assert.That(Probes.Count("params:CustomersProbe"), Is.EqualTo(1), "the parent must not even receive new parameters");
        Assert.That(Probes.Count("dispose:CustomersProbe"), Is.EqualTo(0));
        Assert.That(Probes.Count("dispose:ListProbe"), Is.EqualTo(1));
        Assert.That(Probes.Count("ctor:DetailsProbe"), Is.EqualTo(1));
        Assert.That(cut.Find("#customers #details"), Is.Not.Null);
    }

    [Test]
    public async Task The_same_component_with_a_new_parameter_is_updated_in_place()
    {
        var cut = Mount(CustomerRoutes, "http://localhost/customers/42");

        await _adapter.NavigateAsync("customers/43");

        Assert.That(Probes.Count("ctor:DetailsProbe"), Is.EqualTo(1));
        Assert.That(Probes.Count("params:DetailsProbe"), Is.EqualTo(2));
        Assert.That(Probes.Count("params:CustomersProbe"), Is.EqualTo(1));
        Assert.That(cut.Find("#details").TextContent, Does.Contain("Customer 43"));
    }

    [Test]
    public async Task Adding_a_grandchild_keeps_the_parent_and_child_instances()
    {
        var cut = Mount(CustomerRoutes, "http://localhost/customers/42");

        await _adapter.NavigateAsync("customers/42/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77");

        Assert.That(Probes.Count("ctor:CustomersProbe"), Is.EqualTo(1));
        Assert.That(Probes.Count("ctor:DetailsProbe"), Is.EqualTo(1));
        Assert.That(Probes.Count("params:DetailsProbe"), Is.EqualTo(1));
        Assert.That(Probes.Count("ctor:OrderProbe"), Is.EqualTo(1));
        Assert.That(cut.Find("#order"), Is.Not.Null);
    }

    [Test]
    public async Task A_query_only_navigation_does_not_touch_components_that_do_not_use_the_query()
    {
        var cut = Mount(CustomerRoutes, "http://localhost/customers/42?tab=a");

        await _adapter.NavigateAsync("customers/42?tab=b");

        Assert.That((Store.Current!.Path, Store.Current.Query), Is.EqualTo(("/customers/42", "?tab=b")), "the query-only navigation must have committed");
        Assert.That(Probes.Count("ctor:DetailsProbe"), Is.EqualTo(1));
        Assert.That(Probes.Count("params:DetailsProbe"), Is.EqualTo(1));
        Assert.That(Probes.Count("params:CustomersProbe"), Is.EqualTo(1));
        Assert.That(cut.Find("#details").TextContent, Does.Contain("Customer 42"));
    }

    [Test]
    public async Task An_omitted_optional_parameter_is_reset_on_the_retained_component()
    {
        var cut = Mount(map => map.Route("/list/{page:int?}", typeof(PageProbe)), "http://localhost/list/3");
        Assert.That(cut.Find("#page").TextContent, Is.EqualTo("page 3"));

        await _adapter.NavigateAsync("list");

        Assert.That(Store.Current!.Path, Is.EqualTo("/list"));
        Assert.That(Probes.Count("ctor:PageProbe"), Is.EqualTo(1), "the component is reused");
        Assert.That(cut.Find("#page").TextContent, Is.EqualTo("no page"), "a reused component must not keep the old page");
    }

    [Test]
    public async Task A_route_that_does_not_supply_a_parameter_resets_it_on_the_reused_component()
    {
        var cut = Mount(map => map.Route("/a/{id:int}", typeof(DetailsProbe)).Route("/b", typeof(DetailsProbe)), "http://localhost/a/5");
        Assert.That(cut.Find("#details").TextContent, Does.Contain("Customer 5"));

        await _adapter.NavigateAsync("b");

        Assert.That(Store.Current!.Path, Is.EqualTo("/b"));
        Assert.That(Probes.Count("ctor:DetailsProbe"), Is.EqualTo(1), "the component is reused by default");
        Assert.That(cut.Find("#details").TextContent, Does.Contain("Customer 0"), "a reused component must not keep the old id");
    }

    [Test]
    public async Task A_fresh_instance_keeps_its_property_initializer_for_an_omitted_parameter()
    {
        var cut = Mount(map => map.Route("/dl/{deleteFile:bool?}", typeof(DeleteFileProbe)).Route("/other", typeof(ShowProbe)), "http://localhost/dl");
        Assert.That(cut.Find("#delete").TextContent, Is.EqualTo("true"), "the first instance keeps DeleteFile = true");

        await _adapter.NavigateAsync("other");
        Assert.That(cut.FindAll("#delete"), Is.Empty);
        await _adapter.NavigateAsync("dl");

        Assert.That(Store.Current!.Path, Is.EqualTo("/dl"));
        Assert.That(Probes.Count("ctor:DeleteFileProbe"), Is.EqualTo(2), "a different component rendered in between, so this is a fresh instance");
        Assert.That(cut.Find("#delete").TextContent, Is.EqualTo("true"), "a fresh instance is never reset: its initializer stands");
    }

    [Test]
    public async Task A_reused_instance_resets_an_omitted_parameter_to_the_type_default_not_the_initializer()
    {
        var cut = Mount(map => map.Route("/dl/{deleteFile:bool?}", typeof(DeleteFileProbe)), "http://localhost/dl/false");
        Assert.That(cut.Find("#delete").TextContent, Is.EqualTo("false"));

        await _adapter.NavigateAsync("dl");

        Assert.That(Store.Current!.Path, Is.EqualTo("/dl"));
        Assert.That(Probes.Count("ctor:DeleteFileProbe"), Is.EqualTo(1), "the component is reused");
        Assert.That(cut.Find("#delete").TextContent, Is.EqualTo("null"), "a reused instance must not keep the old value; the type default, not the initializer, is what it gets");
    }

    [Test]
    public async Task A_query_only_navigation_leaves_a_parameter_the_url_never_supplied_at_its_initializer()
    {
        var cut = Mount(map => map.Route("/dl/{deleteFile:bool?}", typeof(DeleteFileProbe)), "http://localhost/dl");
        Assert.That(cut.Find("#delete").TextContent, Is.EqualTo("true"));

        await _adapter.NavigateAsync("dl?tab=2");

        Assert.That((Store.Current!.Path, Store.Current.Query), Is.EqualTo(("/dl", "?tab=2")), "the query-only navigation must have committed");
        Assert.That(Probes.Count("ctor:DeleteFileProbe"), Is.EqualTo(1), "the component is reused");
        Assert.That(cut.Find("#delete").TextContent, Is.EqualTo("true"), "the previous URL never supplied DeleteFile, so there is nothing stale to reset: the initializer stands");
    }

    [Test]
    public async Task Opening_a_child_route_leaves_the_parents_unsupplied_parameter_at_its_initializer()
    {
        // The same component is placed by /p (with a child) and by /p/{id:int}, so "id" is one of its route parameter names.
        var cut = Mount(map => map
            .Route("/p", typeof(DefaultIdProbe), children: p => p.Route("child", typeof(ShowProbe)))
            .Route("/p/{id:int}", typeof(DefaultIdProbe)), "http://localhost/p");
        Assert.That(cut.Find("#pid").TextContent, Is.EqualTo("id -1"));

        await _adapter.NavigateAsync("p/child");

        Assert.That(Store.Current!.Path, Is.EqualTo("/p/child"));
        Assert.That(cut.Find("#pid #show"), Is.Not.Null, "the child opened in the parent's outlet");
        Assert.That(Probes.Count("ctor:DefaultIdProbe"), Is.EqualTo(1), "the parent is reused");
        Assert.That(cut.Find("#pid").TextContent, Does.StartWith("id -1"), "neither URL supplied Id, so the parent keeps its initializer");
    }

    [Test]
    public async Task A_recreated_instance_keeps_its_initializer_under_a_recreate_policy()
    {
        var cut = Mount(map => map.Route("/dl/{deleteFile:bool?}", typeof(DeleteFileProbe), reuse: RouteComponentReuse.RecreateWhenParametersChange), "http://localhost/dl/false");
        Assert.That(cut.Find("#delete").TextContent, Is.EqualTo("false"));

        await _adapter.NavigateAsync("dl");

        Assert.That(Probes.Count("ctor:DeleteFileProbe"), Is.EqualTo(2), "the parameters changed, so the policy recreates the component");
        Assert.That(cut.Find("#delete").TextContent, Is.EqualTo("true"));
    }

    [Test]
    public async Task A_fractional_double_parameter_renders_on_a_double_property()
    {
        var cut = Mount(map => map.Route("/p/{x:double}", typeof(DoubleProbe)), "http://localhost/p/2.5");
        Assert.That(cut.Find("#x").TextContent, Is.EqualTo("2.5"));

        await _adapter.NavigateAsync("p/0.25");

        Assert.That(Store.Current!.Path, Is.EqualTo("/p/0.25"));
        Assert.That(cut.Find("#x").TextContent, Is.EqualTo("0.25"));
    }

    [Test]
    public void A_double_parameter_bound_to_an_int_property_fails_at_startup()
    {
        var exception = Assert.Throws<RouteBindingValidationException>(() => _ctx.Services.AddModRouter(map => map.Route("/p/{x:double}", typeof(IntXProbe))));

        Assert.That(exception!.Errors, Has.Exactly(1).Contain("'x' is Double").And.Contain("IntXProbe.X is Int32"));
    }

    sealed class IntXProbe : ComponentBase
    {
        [Parameter]
        public int X { get; set; }
    }

    [Test]
    public void An_optional_parameter_bound_to_a_non_nullable_property_fails_at_startup()
    {
        var exception = Assert.Throws<RouteBindingValidationException>(() => _ctx.Services.AddModRouter(map => map.Route("/d/{id:int?}", typeof(DetailsProbe))));

        Assert.That(exception!.Errors, Has.Exactly(1).Contain("'id' is optional").And.Contain("must be nullable"));
    }

    [Test]
    public async Task Moving_between_found_and_not_found_keeps_the_layout_instance()
    {
        var cut = Mount(
            CustomerRoutes,
            "http://localhost/customers",
            layout: typeof(MainLayoutProbe),
            notFound: b => b.AddMarkupContent(0, "<p id=\"nf\">x</p>"));

        await _adapter.NavigateAsync("nope");
        Assert.That(cut.Find("#layout #nf"), Is.Not.Null);
        await _adapter.NavigateAsync("customers/1");

        Assert.That(Probes.Count("ctor:MainLayoutProbe"), Is.EqualTo(1));
        Assert.That(Probes.Count("dispose:MainLayoutProbe"), Is.EqualTo(0));
        Assert.That(cut.Find("#layout #details"), Is.Not.Null);
    }

    // ---- reuse policies -----------------------------------------------------------------------------------------

    [Test]
    public async Task Components_of_two_routes_sharing_a_type_are_reused_by_default()
    {
        Mount(map => map.Route("/a", typeof(ShowProbe), name: "a").Route("/b", typeof(ShowProbe), name: "b"), "http://localhost/a");
        Assert.That(Store.Current!.Chain!.Leaf.RouteName, Is.EqualTo("a"));

        await _adapter.NavigateAsync("b");

        Assert.That((Store.Current!.Path, Store.Current.Chain!.Leaf.RouteName), Is.EqualTo(("/b", "b")), "the navigation to /b must have committed");
        Assert.That(Probes.Count("ctor:ShowProbe"), Is.EqualTo(1));
    }

    [Test]
    public async Task RecreateWhenRouteChanges_creates_a_new_instance_for_a_different_route_but_not_for_a_parameter_change()
    {
        Mount(
            map => map
                .Route("/a", typeof(ShowProbe), reuse: RouteComponentReuse.RecreateWhenRouteChanges)
                .Route("/b", typeof(ShowProbe), reuse: RouteComponentReuse.RecreateWhenRouteChanges)
                .Route("/d/{id:int}", typeof(DetailsProbe), reuse: RouteComponentReuse.RecreateWhenRouteChanges),
            "http://localhost/a");

        await _adapter.NavigateAsync("b");
        Assert.That(Probes.Count("ctor:ShowProbe"), Is.EqualTo(2));

        await _adapter.NavigateAsync("d/1");
        await _adapter.NavigateAsync("d/2");
        Assert.That(Probes.Count("ctor:DetailsProbe"), Is.EqualTo(1));
    }

    [Test]
    public async Task RecreateWhenParametersChange_creates_a_new_instance_when_the_parameters_change_only()
    {
        Mount(
            map => map.Route("/d/{id:int}", typeof(DetailsProbe), reuse: RouteComponentReuse.RecreateWhenParametersChange),
            "http://localhost/d/1");

        await _adapter.NavigateAsync("d/1?x=1");
        Assert.That((Store.Current!.Path, Store.Current.Query), Is.EqualTo(("/d/1", "?x=1")), "the query-only navigation must have committed");
        Assert.That(Probes.Count("ctor:DetailsProbe"), Is.EqualTo(1));

        await _adapter.NavigateAsync("d/2");

        Assert.That((Store.Current!.Path, Store.Current.Query), Is.EqualTo(("/d/2", "")));
        Assert.That(Probes.Count("ctor:DetailsProbe"), Is.EqualTo(2));
        Assert.That(Probes.Count("dispose:DetailsProbe"), Is.EqualTo(1));
    }

    [Test]
    public async Task NeverReuse_creates_a_new_instance_on_every_committed_navigation_including_query_only()
    {
        Mount(
            map => map.Route("/d/{id:int}", typeof(DetailsProbe), reuse: RouteComponentReuse.NeverReuse),
            "http://localhost/d/1");

        await _adapter.NavigateAsync("d/1?x=1");

        Assert.That(Probes.Count("ctor:DetailsProbe"), Is.EqualTo(2));
    }

    // ---- mistakes -----------------------------------------------------------------------------------------------

    [Test]
    public void Two_outlets_at_the_same_depth_are_reported()
    {
        _expectsRenderException = true;
        var exception = Assert.Catch(() => Mount(
            map => map.Route("/x", typeof(DoubleOutletProbe), children: c => c.Route("y", typeof(ShowProbe))),
            "http://localhost/x/y"));

        Assert.That(exception!.ToString(), Does.Contain("More than one ModRouterOutlet"));
    }

    [Test]
    public void Only_one_router_can_be_mounted()
    {
        _expectsRenderException = true;
        _ctx.Services.AddModRouter(map => map.Route("/", typeof(HomeProbe)));
        _ctx.Services.AddSingleton<INavigationPlatformAdapter>(new FakeNavigationAdapter());

        var exception = Assert.Catch(() => _ctx.Render<TwoRoutersProbe>());

        Assert.That(exception!.ToString(), Does.Contain("already mounted"));
    }

    [Test]
    public async Task Disposing_the_router_detaches_it_from_navigation()
    {
        Mount(CustomerRoutes, "http://localhost/customers");
        Assert.That(_adapter.HandlerCount, Is.EqualTo(1));

        // DisposeComponentsAsync resets Renderer.UnhandledException, so TearDown's check cannot see a swallowed render
        // exception from this test; check explicitly before disposing.
        Assert.That(_ctx.Renderer.UnhandledException.IsCompleted, Is.False, () => "a render exception was swallowed: " + _ctx.Renderer.UnhandledException.Result);
        await _ctx.DisposeComponentsAsync();

        Assert.That(_adapter.HandlerCount, Is.EqualTo(0));
    }

    // ---- regressions (outlet claims and router lifetime) -------------------------------------------------------

    /// <summary>A depth-0 component whose outlet is rendered conditionally, so the outlet can be removed and re-added in one state.</summary>
    sealed class ToggleOutletProbe : ProbeBase
    {
        bool _visible = true;

        public static ToggleOutletProbe? Instance { get; private set; }

        public ToggleOutletProbe() => Instance = this;

        public Task SetVisibleAsync(bool visible) => InvokeAsync(() =>
        {
            _visible = visible;
            StateHasChanged();
        });

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "section");
            builder.AddAttribute(1, "id", "toggle");

            if (_visible)
            {
                Outlet(builder, 2);
            }

            builder.CloseElement();
        }
    }

    /// <summary>Records every distinct <see cref="RouteOutletContext"/> instance it is cascaded.</summary>
    sealed class ContextProbe : ProbeBase
    {
        public static HashSet<RouteOutletContext> Seen { get; } = new(ReferenceEqualityComparer.Instance);

        [CascadingParameter]
        public RouteOutletContext? Context { get; set; }

        protected override void OnParametersSet()
        {
            base.OnParametersSet();

            if (Context is not null)
            {
                Seen.Add(Context);
            }
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "p");
            builder.AddAttribute(1, "id", "ctx");
            builder.CloseElement();
        }
    }

    /// <summary>A stage that completes only when the test says so, to make the initial navigation genuinely asynchronous.</summary>
    sealed class GateStage : INavigationStage
    {
        public TaskCompletionSource Gate { get; } = new();

        public async ValueTask<StageResult> RunAsync(RouteNavigationContext context)
        {
            await Gate.Task;

            return StageResult.Continue;
        }
    }

    [Test]
    public async Task Switching_between_two_depth0_components_that_both_have_outlets_is_not_a_double_outlet()
    {
        var cut = Mount(
            map => map
                .Route("/customers", typeof(CustomersProbe), children: c => c.Index(typeof(ListProbe)))
                .Route("/vendors", typeof(CustomersWithEmptyProbe), children: c => c.Route("{id:int}", typeof(DetailsProbe))),
            "http://localhost/customers");

        await _adapter.NavigateAsync("vendors/5");

        Assert.That(_ctx.Renderer.UnhandledException.IsCompleted, Is.False, () => "the renderer caught an exception: " + _ctx.Renderer.UnhandledException.Result);
        Assert.That(Probes.Count("dispose:CustomersProbe"), Is.EqualTo(1));
        Assert.That(Probes.Count("ctor:CustomersWithEmptyProbe"), Is.EqualTo(1));
        Assert.That(cut.Find("#customers #details").TextContent, Does.Contain("Customer 5"));
    }

    [Test]
    public async Task Moving_to_not_found_from_a_component_with_an_outlet_leaves_no_unhandled_exception()
    {
        var cut = Mount(CustomerRoutes, "http://localhost/customers", notFound: b => b.AddMarkupContent(0, "<p id=\"nf\">x</p>"));

        await _adapter.NavigateAsync("nope");

        Assert.That(_ctx.Renderer.UnhandledException.IsCompleted, Is.False, () => "the renderer caught an exception: " + _ctx.Renderer.UnhandledException.Result);
        Assert.That(cut.Find("#nf"), Is.Not.Null);
        Assert.That(Probes.Count("dispose:CustomersProbe"), Is.EqualTo(1));
    }

    [Test]
    public async Task An_outlet_removed_and_re_added_in_the_same_state_claims_its_depth_again()
    {
        var cut = Mount(map => map.Route("/t", typeof(ToggleOutletProbe), children: c => c.Index(typeof(ListProbe))), "http://localhost/t");
        Assert.That(cut.Find("#toggle #list"), Is.Not.Null);

        await ToggleOutletProbe.Instance!.SetVisibleAsync(false);
        Assert.That(cut.FindAll("#list"), Is.Empty);
        await ToggleOutletProbe.Instance!.SetVisibleAsync(true);

        Assert.That(_ctx.Renderer.UnhandledException.IsCompleted, Is.False, () => "the renderer caught an exception: " + _ctx.Renderer.UnhandledException.Result);
        Assert.That(cut.Find("#toggle #list"), Is.Not.Null);
        Assert.That(Probes.Count("ctor:ListProbe"), Is.EqualTo(2));
    }

    [Test]
    public async Task An_asynchronous_initial_navigation_produces_one_context_per_state()
    {
        ContextProbe.Seen.Clear();
        var stage = new GateStage();
        _ctx.Services.AddSingleton<INavigationStage>(stage);
        var cut = Mount(map => map.Route("/", typeof(ContextProbe)), "http://localhost/");
        Assert.That(cut.FindAll("#ctx"), Is.Empty, "nothing renders before the first commit");

        await _ctx.Renderer.Dispatcher.InvokeAsync(stage.Gate.SetResult);

        cut.WaitForAssertion(() => Assert.That(cut.FindAll("#ctx"), Has.Count.EqualTo(1)));
        Assert.That(ContextProbe.Seen, Has.Count.EqualTo(1), "the same committed state must not be applied as two different contexts");
    }
}
