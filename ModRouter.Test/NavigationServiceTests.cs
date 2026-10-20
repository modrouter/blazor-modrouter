using ModRouter.Components;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Matching;
using ModRouter.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModRouter.Test;

[TestFixture]
public sealed class NavigationServiceTests
{
    static (NavigationService Service, FakeNavigationAdapter Adapter, RouteStateStore Store, NavigationCoordinator Coordinator) Create(
        string baseUri = "http://localhost/",
        string? uri = null)
    {
        var table = RouteTestKit.CustomersTable();
        var adapter = new FakeNavigationAdapter(baseUri, uri);
        var store = new RouteStateStore();
        var holder = RouteTestKit.Frozen(table);
        var coordinator = new NavigationCoordinator(adapter, store, holder, new RouteComponentBinder(), new ServiceCollection().BuildServiceProvider(), new ModRouterOptions(), [], NullLoggerFactory.Instance);

        return (new NavigationService(adapter, holder, store), adapter, store, coordinator);
    }

    [Test]
    public void NavigateTo_passes_through_to_the_platform()
    {
        var (service, adapter, _, _) = Create();

        service.NavigateTo("customers/42", replaceHistory: true, forceLoad: true);

        Assert.That(adapter.NavigateCalls.Single(), Is.EqualTo(("customers/42", true, true, (string?)null)));
    }

    [Test]
    public void NavigateToRoute_generates_a_base_relative_url_with_query_and_fragment()
    {
        var (service, adapter, _, _) = Create();

        service.NavigateToRoute(
            "customer-details",
            new Dictionary<string, object?> { ["id"] = 42 },
            new Dictionary<string, object?> { ["tab"] = "orders" },
            "top");
        service.NavigateToRoute("home");

        Assert.That(adapter.NavigateCalls.Select(c => c.Uri), Is.EqualTo(new[] { "customers/42?tab=orders#top", "" }));
    }

    [Test]
    public void NavigateToRoute_accepts_an_anonymous_object()
    {
        var (service, adapter, _, _) = Create();

        service.NavigateToRoute("customer-details", new { id = 7 }, replaceHistory: true);

        Assert.That(adapter.NavigateCalls.Single(), Is.EqualTo(("customers/7", true, false, (string?)null)));
    }

    [Test]
    public async Task Ambient_values_come_from_the_current_leaf_only_when_asked_for()
    {
        var (service, adapter, _, coordinator) = Create(uri: "http://localhost/customers/42");
        await coordinator.NavigateInitialAsync();

        service.NavigateToRoute("customer-order-details", new { orderId = Guid.Parse("4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77") }, useAmbientValues: true);

        Assert.That(adapter.NavigateCalls.Single().Uri, Is.EqualTo("customers/42/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77"));
        Assert.Throws<ModRouter.Core.Generation.RouteUrlGenerationException>(
            () => service.NavigateToRoute("customer-order-details", new { orderId = Guid.NewGuid() }));
    }

    [TestCase("http://localhost/", "/customers/42")]
    [TestCase("http://localhost/myapp/", "/myapp/customers/42")]
    public void GetRouteUrl_is_prefixed_with_the_base_path(string baseUri, string expected)
    {
        var (service, _, _, _) = Create(baseUri);

        Assert.That(service.GetRouteUrl("customer-details", new Dictionary<string, object?> { ["id"] = 42 }), Is.EqualTo(expected));
    }

    [Test]
    public async Task Location_and_state_changes_are_forwarded()
    {
        var (service, adapter, _, coordinator) = Create(uri: "http://localhost/customers");
        var locations = new List<string>();
        var states = new List<RouteState>();
        service.OnLocationChanged += (_, location) => locations.Add(location);
        service.RouteStateChanged += states.Add;
        using var lease = coordinator.Attach();
        await coordinator.NavigateInitialAsync();

        await adapter.NavigateAsync("customers/9");

        Assert.That(service.GetLocation(), Is.EqualTo("http://localhost/customers/9"));
        Assert.That(locations, Is.EqualTo(new[] { "http://localhost/customers/9" }));
        Assert.That(states.Select(s => s.Path), Is.EqualTo(new[] { "/customers", "/customers/9" }));
        Assert.That(service.Current!.Path, Is.EqualTo("/customers/9"));
    }

    [Test]
    public async Task A_throwing_RouteStateChanged_subscriber_does_not_stop_the_ones_after_it()
    {
        var (service, adapter, _, coordinator) = Create(uri: "http://localhost/customers");
        var states = new List<RouteState>();
        service.RouteStateChanged += _ => throw new InvalidOperationException("subscriber failed");
        service.RouteStateChanged += states.Add;
        using var lease = coordinator.Attach();
        await coordinator.NavigateInitialAsync();

        await adapter.NavigateAsync("customers/9");

        Assert.That(states.Select(s => s.Path), Is.EqualTo(new[] { "/customers", "/customers/9" }));
    }

    [Test]
    public async Task An_unsubscribed_RouteStateChanged_handler_is_no_longer_called()
    {
        var (service, adapter, _, coordinator) = Create(uri: "http://localhost/customers");
        var states = new List<RouteState>();
        Action<RouteState> handler = states.Add;
        service.RouteStateChanged += handler;
        using var lease = coordinator.Attach();
        await coordinator.NavigateInitialAsync();
        service.RouteStateChanged -= handler;

        await adapter.NavigateAsync("customers/9");

        Assert.That(states.Select(s => s.Path), Is.EqualTo(new[] { "/customers" }));
    }
}

[TestFixture]
public sealed class AddModRouterTests
{
    [Test]
    public void Registers_the_table_holder_and_the_scoped_services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModRouter(map => map.Route("/", typeof(HomeProbe), name: "home"));
        services.AddSingleton<INavigationPlatformAdapter>(new FakeNavigationAdapter());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var holder = scope.ServiceProvider.GetRequiredService<RouteTableHolder>();
        Assert.That(holder.Table.TryGetRoute("home", out _), Is.True);
        Assert.That(scope.ServiceProvider.GetService<CompiledRouteTable>(), Is.Null, "the table is read through the holder, never resolved directly");
        Assert.That(scope.ServiceProvider.GetRequiredService<ModRouter.Core.Generation.INamedRouteRegistry>(), Is.SameAs(holder));
        Assert.That(scope.ServiceProvider.GetRequiredService<INavigationService>(), Is.Not.Null);
        Assert.That(scope.ServiceProvider.GetRequiredService<NavigationCoordinator>(), Is.Not.Null);
        var store = scope.ServiceProvider.GetRequiredService<RouteStateStore>();
        Assert.That(scope.ServiceProvider.GetRequiredService<RouteStateStore>(), Is.SameAs(store));
    }

    [Test]
    public void Compile_errors_surface_at_registration()
    {
        var services = new ServiceCollection();

        Assert.Throws<RouteCompilationException>(() => services.AddModRouter(map => map.Route("/a/{", typeof(HomeProbe))));
    }

    [Test]
    public void Binding_mismatches_surface_at_registration_and_can_be_switched_off()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<RouteBindingValidationException>(
            () => services.AddModRouter(map => map.Route("/g/{id:int}", typeof(GuidProbe))));
        Assert.That(exception!.Message, Does.Contain("can never be converted"));

        Assert.DoesNotThrow(() => services.AddModRouter(map => map.Route("/g/{id:int}", typeof(GuidProbe)), o => o.ValidateComponentBindings = false));
    }

    [Test]
    public void A_custom_constraint_registry_is_used()
    {
        var registry = new ModRouter.Core.Constraints.ConstraintRegistry();
        registry.Register("any", _ => new ModRouter.Core.Constraints.ConstraintRegistry().TryCreate("alpha", out var c) ? c! : throw new InvalidOperationException());

        Assert.DoesNotThrow(() => new ServiceCollection().AddModRouter(map => map.Route("/x/{v:any}", typeof(HomeProbe)), o => o.ConstraintRegistry = registry));
    }
}
