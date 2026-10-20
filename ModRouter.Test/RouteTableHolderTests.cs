using Bunit;
using ModRouter.Components;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Generation;
using ModRouter.Core.Guards;
using ModRouter.Navigation;
using Microsoft.Extensions.DependencyInjection;
using static ModRouter.Test.ComponentTestKit;

namespace ModRouter.Test;

/// <summary>
/// The route table behind a <see cref="RouteTableHolder"/>: the startup table until the router freezes it exactly once,
/// read per use by every consumer (the binder reads no table: it binds what the match supplies), with a hint on a failed
/// name lookup while the table is not final.
/// </summary>
[TestFixture]
public sealed class RouteTableHolderTests
{
    const string Hint = "The route table is not final yet: markup <Routes> entries are compiled after ModRouter's first render.";

    /// <summary>A guard that redirects by route name to a route that exists only in the swapped table.</summary>
    sealed class ToSwappedRouteGuard : IRouteGuard
    {
        public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context)
            => ValueTask.FromResult(RouteGuardResult.RedirectToRoute("customer-details", new { id = 77 }));
    }

    static CompiledRouteTable Startup() => RouteTestKit.Compile(map => map.Route("/", typeof(HomeProbe), name: "home", title: "Home"));

    static CompiledRouteTable Swapped() => RouteTestKit.Compile(map => map
        .Route("/", typeof(HomeProbe), name: "home", title: "Home")
        .Route("/customers", typeof(CustomersProbe), name: "customers", children: customers => customers
            .Index(typeof(ListProbe), name: "customer-list")
            .Route("{id:int}", typeof(DetailsProbe), name: "customer-details"))
        .Route("/details", typeof(DetailsProbe), name: "details-no-id")
        .Route("/secret", typeof(ShowProbe), name: "secret").Guard<ToSwappedRouteGuard>()
        .Route("/vendors", typeof(ShowProbe), name: "vendors", aliases: ["/suppliers"]).CanonicalAlias()
        .Redirect("/old/customers/{id:int}", "customer-details"));

    static (ServiceProvider Provider, RouteTableHolder Holder, FakeNavigationAdapter Adapter) Build(string uri)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddModRouter(map => map.Route("/", typeof(HomeProbe), name: "home", title: "Home"));
        var adapter = new FakeNavigationAdapter("http://localhost/", uri) { ExecuteNavigateTo = true };
        services.AddSingleton<INavigationPlatformAdapter>(adapter);
        var provider = services.BuildServiceProvider();

        return (provider, provider.GetRequiredService<RouteTableHolder>(), adapter);
    }

    // ---- the holder itself --------------------------------------------------------------------------------------

    [Test]
    public void Serves_the_startup_table_and_its_definitions_until_frozen()
    {
        var startup = Startup();
        var definitions = RouteMap.Create().Route("/", typeof(HomeProbe), name: "home").Build();
        var holder = new RouteTableHolder(startup, definitions);

        Assert.That(holder.IsFrozen, Is.False);
        Assert.That(holder.Table, Is.SameAs(startup));
        Assert.That(holder.StartupDefinitions, Is.SameAs(definitions));
        Assert.That(holder.TryGetRoute("home", out var home), Is.True);
        Assert.That(home!.Component, Is.EqualTo(typeof(HomeProbe)));
    }

    [Test]
    public void TryGetRoute_with_a_null_name_throws_ArgumentNullException_naming_routeName()
    {
        var holder = new RouteTableHolder(Startup());
        INamedRouteRegistry registry = holder;

        var onHolder = Assert.Throws<ArgumentNullException>(() => holder.TryGetRoute(null!, out _))!;
        var onInterface = Assert.Throws<ArgumentNullException>(() => registry.TryGetRoute(null!, out _))!;

        Assert.That(onHolder.ParamName, Is.EqualTo("routeName"));
        Assert.That(onInterface.ParamName, Is.EqualTo("routeName"));
    }

    [Test]
    public void Freeze_swaps_the_table_once()
    {
        var holder = new RouteTableHolder(Startup());
        var swapped = Swapped();

        holder.Freeze(swapped);

        Assert.That(holder.IsFrozen, Is.True);
        Assert.That(holder.Table, Is.SameAs(swapped));
        Assert.That(holder.TryGetRoute("customer-details", out _), Is.True);
    }

    [Test]
    public void A_second_freeze_throws_and_leaves_the_table_alone()
    {
        var holder = new RouteTableHolder(Startup());
        var first = Swapped();
        holder.Freeze(first);

        var error = Assert.Throws<InvalidOperationException>(() => holder.Freeze(Swapped()))!;

        Assert.That(error.Message, Does.StartWith("The route table is already frozen."));
        Assert.That(holder.Table, Is.SameAs(first));
    }

    [Test]
    public void A_repeat_freeze_with_the_same_signature_is_a_no_op_and_a_different_one_throws()
    {
        var holder = new RouteTableHolder(Startup());
        var first = Swapped();
        holder.Freeze(first, "markup: a, b");

        Assert.DoesNotThrow(() => holder.Freeze(Swapped(), "markup: a, b"), "a remounted router declaring the same routes");
        Assert.That(holder.Table, Is.SameAs(first), "the first table stays");
        Assert.Throws<InvalidOperationException>(() => holder.Freeze(Swapped(), "markup: a"));
        Assert.Throws<InvalidOperationException>(() => holder.Freeze(Swapped()));
    }

    [Test]
    public void Concurrent_freezes_from_many_circuits_settle_on_one_table_and_never_tear_it()
    {
        // The holder is a singleton: every circuit of a Blazor Server host freezes it from its own thread.
        for (var round = 0; round < 50; round++)
        {
            var holder = new RouteTableHolder(Startup());
            var tables = Enumerable.Range(0, 16).Select(_ => Swapped()).ToArray();
            var winners = new System.Collections.Concurrent.ConcurrentBag<CompiledRouteTable>();
            var rejected = 0;
            using var start = new ManualResetEventSlim();

            var threads = tables.Select((table, index) => Task.Run(() =>
            {
                start.Wait();

                try
                {
                    holder.Freeze(table, index % 2 == 0 ? "markup: even" : "markup: odd");
                    winners.Add(table);
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Increment(ref rejected);
                }
            })).ToArray();

            start.Set();
            Task.WaitAll(threads);

            // Same-signature repeats are no-ops (they add themselves to winners without replacing the table); different
            // signatures throw. Whichever signature won, the table is exactly one of the offered tables, frozen once.
            Assert.That(holder.IsFrozen, Is.True);
            Assert.That(tables, Does.Contain(holder.Table));
            Assert.That(winners.Count + rejected, Is.EqualTo(tables.Length));
            Assert.That(rejected, Is.EqualTo(8), "exactly the callers with the other signature are rejected");
        }
    }

    [Test]
    public void Before_the_freeze_a_failed_name_lookup_carries_the_not_final_hint()
    {
        var holder = new RouteTableHolder(Startup());

        var thrown = Assert.Throws<RouteUrlGenerationException>(() => holder.GenerateUrl("customer-details"))!;
        Assert.That(thrown.Code, Is.EqualTo(UrlGenerationErrorCode.UnknownRoute));
        Assert.That(thrown.Message, Is.EqualTo("No route named 'customer-details' exists. Registered route names: home. " + Hint));

        Assert.That(holder.TryGenerateUrl("customer-details", null, null, null, null, out _, out var error), Is.False);
        Assert.That(error!.Message, Does.EndWith(Hint));

        var missing = Assert.Throws<KeyNotFoundException>(() => holder.GetRequiredRoute("customer-details"))!;
        Assert.That(missing.Message, Does.StartWith("No route named 'customer-details' exists.").And.EndWith(Hint));

        Assert.That(holder.GenerateUrl("home"), Is.EqualTo("/"), "a lookup that succeeds is unaffected");
    }

    [Test]
    public void Other_generation_failures_and_lookups_after_the_freeze_carry_no_hint()
    {
        var holder = new RouteTableHolder(Startup());
        var swapped = Swapped();

        var before = Assert.Throws<RouteUrlGenerationException>(() => holder.GenerateUrl("home", new Dictionary<string, object?> { ["id"] = 1 }))!;
        Assert.That(before.Code, Is.EqualTo(UrlGenerationErrorCode.UnknownParameter));
        Assert.That(before.Message, Does.Not.Contain(Hint), "only an unknown route name can be a route that is not compiled yet");

        holder.Freeze(swapped);

        var after = Assert.Throws<RouteUrlGenerationException>(() => holder.GenerateUrl("nope"))!;
        Assert.That(after.Message, Is.EqualTo("No route named 'nope' exists. Registered route names: customer-details, customer-list, customers, details-no-id, home, secret, vendors."));
        Assert.That(Assert.Throws<KeyNotFoundException>(() => holder.GetRequiredRoute("nope"))!.Message, Does.Not.Contain(Hint));
        Assert.That(holder.GenerateUrl("customer-details", new Dictionary<string, object?> { ["id"] = 3 }), Is.EqualTo("/customers/3"));
    }

    // ---- registration -------------------------------------------------------------------------------------------

    [Test]
    public void The_table_is_not_a_DI_service_and_the_named_registry_is_the_holder()
    {
        var (provider, holder, _) = Build("http://localhost/");

        Assert.That(provider.GetService<CompiledRouteTable>(), Is.Null);
        Assert.That(provider.GetRequiredService<INamedRouteRegistry>(), Is.SameAs(holder));
        Assert.That(holder.IsFrozen, Is.False, "AddModRouter compiles the startup table; only ModRouter freezes it");
        Assert.That(holder.StartupDefinitions.Select(d => d.Name), Is.EqualTo(new[] { "home" }));
    }

    [Test]
    public void AddModRouter_without_routes_registers_an_empty_startup_table()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddModRouter();

        using var provider = services.BuildServiceProvider();
        var holder = provider.GetRequiredService<RouteTableHolder>();
        Assert.That(holder.StartupDefinitions, Is.Empty);
        Assert.That(holder.Table.Routes, Is.Empty);
    }

    // ---- every consumer reads the holder's table per use ---------------------------------------------------------

    [Test]
    public async Task Every_stage_the_service_and_the_coordinator_read_the_swapped_table()
    {
        var (provider, holder, adapter) = Build("http://localhost/customers/42");
        using var scope = provider.CreateScope();
        var navigation = scope.ServiceProvider.GetRequiredService<INavigationService>();
        var coordinator = scope.ServiceProvider.GetRequiredService<NavigationCoordinator>();
        var store = scope.ServiceProvider.GetRequiredService<RouteStateStore>();
        using var lease = coordinator.Attach();
        var swapped = Swapped();

        // Before the swap everything sees the startup table (home only).
        Assert.Throws<RouteUrlGenerationException>(() => navigation.GetRouteUrl("customer-details", new Dictionary<string, object?> { ["id"] = 1 }));

        holder.Freeze(swapped);

        // NavigationService (resolved before the swap).
        Assert.That(navigation.GetRouteUrl("customer-details", new Dictionary<string, object?> { ["id"] = 1 }), Is.EqualTo("/customers/1"));

        // RecognizeStage and BindStage through the coordinator built before the swap.
        await coordinator.NavigateInitialAsync();
        Assert.That(store.Current!.Chain!.Leaf.RouteName, Is.EqualTo("customer-details"));

        // RedirectStage: a redirect rule that exists only in the swapped table.
        await adapter.NavigateAsync("old/customers/3", intercepted: true);
        await adapter.SettledAsync();
        Assert.That(store.Current!.Path, Is.EqualTo("/customers/3"));
        Assert.That(store.Current.Trigger, Is.EqualTo(NavigationTrigger.Redirect));

        // RedirectStage: a canonical alias of the swapped table.
        await adapter.NavigateAsync("suppliers", intercepted: true);
        await adapter.SettledAsync();
        Assert.That(store.Current!.Path, Is.EqualTo("/vendors"));

        // GuardStage: a guard redirect by a route name of the swapped table.
        await adapter.NavigateAsync("secret", intercepted: true);
        await adapter.SettledAsync();
        Assert.That(store.Current!.Path, Is.EqualTo("/customers/77"));

        // NavigationService.NavigateToRoute by a name of the swapped table.
        navigation.NavigateToRoute("customer-details", new { id = 9 });
        await adapter.SettledAsync();
        Assert.That(store.Current!.Path, Is.EqualTo("/customers/9"));
    }

    // ---- ModRouter freezes the holder ------------------------------------------------------------------------------

    [Test]
    public async Task ModRouter_without_markup_routes_freezes_the_startup_table_on_mount_and_a_remount_is_a_no_op()
    {
        await using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddModRouter(map => map.Route("/", typeof(HomeProbe), name: "home"));
        ctx.Services.AddSingleton<INavigationPlatformAdapter>(new FakeNavigationAdapter("http://localhost/", "http://localhost/"));
        var holder = ctx.Services.GetRequiredService<RouteTableHolder>();
        var startup = holder.Table;

        var cut = ctx.Render<Components.ModRouter>();
        await EventuallyAsync(() => All(cut, "#home").Count, Is.EqualTo(1));

        Assert.That(holder.IsFrozen, Is.True);
        Assert.That(holder.Table, Is.SameAs(startup));

        await ctx.DisposeComponentsAsync();
        var again = ctx.Render<Components.ModRouter>();
        await EventuallyAsync(() => All(again, "#home").Count, Is.EqualTo(1));

        Assert.That(holder.Table, Is.SameAs(startup));
        Assert.That(ctx.Renderer.UnhandledException.IsCompleted, Is.False, () => "a render exception was swallowed: " + ctx.Renderer.UnhandledException.Result);
    }
}
