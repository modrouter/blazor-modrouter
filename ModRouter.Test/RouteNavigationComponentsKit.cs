using Bunit;
using ModRouter.Core.Definitions;
using ModRouter.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace ModRouter.Test;

/// <summary>
/// A bUnit context with the real router services over the fake platform and a mounted coordinator (no <c>ModRouter</c>
/// component), for components that only read the committed state: <c>RouteLink</c> and <c>RouteBreadcrumb</c>.
/// </summary>
internal sealed class RouterHarness : IAsyncDisposable
{
    readonly IDisposable _lease;

    RouterHarness(BunitContext context, FakeNavigationAdapter adapter, IDisposable lease)
    {
        Context = context;
        Adapter = adapter;
        _lease = lease;
    }

    public BunitContext Context { get; }

    public FakeNavigationAdapter Adapter { get; }

    public RouteStateStore Store => Context.Services.GetRequiredService<RouteStateStore>();

    /// <summary>Home, a customers section (list, details with a title factory, orders), about, and a hidden-breadcrumb route.</summary>
    public static void Routes(RouteMap map) => map
        .Route("/", typeof(HomeProbe), name: "home", title: "Home")
        .Route("/about", typeof(ShowProbe), name: "about", title: "About")
        .Route("/customers", typeof(CustomersProbe), name: "customers", title: "Customers", children: customers => customers
            .Index(typeof(ListProbe), name: "customer-list", title: "All customers")
            .Route("{id:int}", typeof(DetailsProbe), p => $"Customer {p["id"]}", name: "customer", children: customer => customer
                .Route("orders/{orderId:guid}", typeof(OrderProbe), p => $"Order {p["orderId"]}", name: "order")));

    public static async Task<RouterHarness> StartAsync(
        BunitContext context,
        Action<RouteMap>? routes = null,
        string uri = "http://localhost/",
        string baseUri = "http://localhost/")
    {
        context.Services.AddModRouter(routes ?? Routes);
        var adapter = new FakeNavigationAdapter(baseUri, uri) { ExecuteNavigateTo = true };
        context.Services.AddSingleton<INavigationPlatformAdapter>(adapter);

        var coordinator = context.Services.GetRequiredService<NavigationCoordinator>();
        var lease = coordinator.Attach();
        await coordinator.NavigateInitialAsync();

        return new RouterHarness(context, adapter, lease);
    }

    public async Task GoAsync(string uri)
    {
        await Adapter.NavigateAsync(uri, intercepted: true);
        await Adapter.SettledAsync();
    }

    public ValueTask DisposeAsync()
    {
        _lease.Dispose();

        return ValueTask.CompletedTask;
    }
}
