using ModRouter.Components;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Guards;
using ModRouter.Core.Loading;
using ModRouter.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModRouter.Test;

/// <summary>Scripted behavior for the test guards; every call is logged as <c>guard@route</c>.</summary>
internal sealed class GuardScript
{
    readonly Dictionary<string, Func<RouteGuardContext, ValueTask<RouteGuardResult>>> _behaviors = new(StringComparer.Ordinal);

    public List<string> Calls { get; } = [];

    public List<RouteGuardContext> Contexts { get; } = [];

    public void Set(string guard, Func<RouteGuardContext, ValueTask<RouteGuardResult>> behavior) => _behaviors[guard] = behavior;

    public void Set(string guard, RouteGuardResult result) => Set(guard, _ => ValueTask.FromResult(result));

    public ValueTask<RouteGuardResult> RunAsync(string guard, RouteGuardContext context)
    {
        Calls.Add($"{guard}@{context.Level.RouteName ?? context.Level.Template}");
        Contexts.Add(context);

        return _behaviors.TryGetValue(guard, out var behavior) ? behavior(context) : ValueTask.FromResult(RouteGuardResult.Allow());
    }
}

internal sealed class AuthGuard(GuardScript script) : IRouteGuard
{
    public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context) => script.RunAsync("auth", context);
}

internal sealed class AuditGuard(GuardScript script) : IRouteGuard
{
    public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context) => script.RunAsync("audit", context);
}

internal sealed class AdminGuard(GuardScript script) : IRouteGuard
{
    public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context) => script.RunAsync("admin", context);
}

/// <summary>Scripted behavior for the test loaders; every call is logged as <c>loader@route</c> and its context kept.</summary>
internal sealed class LoaderScript
{
    readonly Dictionary<string, Func<RouteLoadContext, ValueTask<RouteLoadResult>>> _behaviors = new(StringComparer.Ordinal);

    public List<string> Calls { get; } = [];

    public List<RouteLoadContext> Contexts { get; } = [];

    public void Set(string loader, Func<RouteLoadContext, ValueTask<RouteLoadResult>> behavior) => _behaviors[loader] = behavior;

    public void Returns(string loader, object? data) => Set(loader, _ => ValueTask.FromResult(RouteLoadResult.Create(data)));

    public ValueTask<RouteLoadResult> RunAsync(string loader, RouteLoadContext context)
    {
        Calls.Add($"{loader}@{context.Level.RouteName ?? context.Level.Template}");
        Contexts.Add(context);

        return _behaviors.TryGetValue(loader, out var behavior) ? behavior(context) : ValueTask.FromResult(RouteLoadResult.Create(loader));
    }
}

internal sealed class CustomersLoader(LoaderScript script) : IRouteLoader
{
    public ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context) => script.RunAsync("customers", context);
}

internal sealed class CustomerLoader(LoaderScript script) : IRouteLoader
{
    public ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context) => script.RunAsync("customer", context);
}

internal sealed class OrdersLoader(LoaderScript script) : IRouteLoader
{
    public ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context) => script.RunAsync("orders", context);
}

/// <summary>A coordinator wired like the real router (DI-resolved guards and loaders) over the fake platform.</summary>
internal sealed class Pipeline : IDisposable
{
    readonly ServiceProvider _provider;

    Pipeline(ServiceProvider provider, FakeNavigationAdapter adapter, RouteStateStore store, NavigationCoordinator coordinator, GuardScript guards, LoaderScript loaders)
    {
        _provider = provider;
        Adapter = adapter;
        Store = store;
        Coordinator = coordinator;
        Guards = guards;
        Loaders = loaders;
        store.Changed += Commits.Add;
    }

    public FakeNavigationAdapter Adapter { get; }

    public RouteStateStore Store { get; }

    public NavigationCoordinator Coordinator { get; }

    public GuardScript Guards { get; }

    public LoaderScript Loaders { get; }

    /// <summary>The navigation history the coordinator feeds (off unless <c>Diagnostics.HistorySize</c> is configured).</summary>
    public required Diagnostics.RouterDiagnostics Diagnostics { get; init; }

    public List<RouteState> Commits { get; } = [];

    public IDisposable? Lease { get; private set; }

    public RouteState Current => Store.Current!;

    /// <summary>
    /// The shared routes: home, login, a redirecting hop route, and customers (index list, details with a canonical
    /// alias, orders). Guards and loaders are attached by <paramref name="configure"/> on top of them when given.
    /// </summary>
    public static CompiledRouteTable Table(Action<RouteMap>? configure = null)
        => RouteTestKit.Compile(map =>
        {
            map.Route("/", typeof(HomePage), name: "home");
            map.Route("/login", typeof(OtherPage), name: "login");
            map.Route("/hop/{n:int}", typeof(OtherPage), name: "hop");

            if (configure is null)
            {
                map.Route("/customers", typeof(CustomersPage), name: "customers", children: c => c
                    .Index(typeof(CustomerList), name: "customer-list")
                    .Route("{id:int}", typeof(CustomerDetails), name: "customer", children: d => d
                        .Route("orders", typeof(OrderDetails), name: "orders")));
            }
            else
            {
                configure(map);
            }
        });

    public static Pipeline Create(
        CompiledRouteTable? table = null,
        string baseUri = "http://localhost/",
        string? uri = null,
        bool attach = true,
        bool executeNavigateTo = true,
        Action<ModRouterOptions>? configure = null,
        IEnumerable<INavigationStage>? stages = null,
        Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory = null)
    {
        var guards = new GuardScript();
        var loaders = new LoaderScript();
        var options = new ModRouterOptions();
        configure?.Invoke(options);

        var services = new ServiceCollection()
            .AddSingleton(guards)
            .AddSingleton(loaders)
            .BuildServiceProvider();

        table ??= Table();
        var adapter = new FakeNavigationAdapter(baseUri, uri) { ExecuteNavigateTo = executeNavigateTo };
        var store = new RouteStateStore();
        var diagnostics = new Diagnostics.RouterDiagnostics(options);
        var coordinator = new NavigationCoordinator(adapter, store, RouteTestKit.Frozen(table), new RouteComponentBinder(), services, options, stages ?? [], loggerFactory ?? NullLoggerFactory.Instance, diagnostics);
        var pipeline = new Pipeline(services, adapter, store, coordinator, guards, loaders) { Diagnostics = diagnostics };

        if (attach)
        {
            pipeline.Lease = coordinator.Attach();
        }

        return pipeline;
    }

    /// <summary>Navigates like a link click and waits for everything that navigation started.</summary>
    public async Task<bool> GoAsync(string uri)
    {
        var allowed = await Adapter.NavigateAsync(uri, intercepted: true);
        await Adapter.SettledAsync();

        return allowed;
    }

    public void Dispose()
    {
        Lease?.Dispose();
        _provider.Dispose();
    }
}
