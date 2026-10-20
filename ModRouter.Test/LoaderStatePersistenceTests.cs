using System.Globalization;
using Bunit;
using ModRouter.Components;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Loading;
using ModRouter.Discovery;
using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Infrastructure;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModRouter.Test;

/// <summary>
/// Loader data that opted in with <c>PersistLoaderData</c> travels from the prerender to the interactive pass through the
/// persistent component state: the prerender container persists it, the interactive container restores it, and the
/// loader does not run again.
/// </summary>
[TestFixture]
public sealed class LoaderStatePersistenceTests
{
    public sealed record Person(int Id, string Name);

    public sealed class PersonLoader : IRouteLoader
    {
        public static int Calls;

        public static bool Defer;

        public ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context)
        {
            var id = Convert.ToInt32(context.Parameters.Values["id"], CultureInfo.InvariantCulture);
            var call = Interlocked.Increment(ref Calls);
            var result = RouteLoadResult.Create(new Person(id, $"Person {id} by call {call}"));

            return ValueTask.FromResult(Defer ? result.Defer("later", Task.FromResult(1)) : result);
        }
    }

    public class PersonPage : ComponentBase
    {
        [CascadingParameter]
        ModRouteData Data { get; set; } = default!;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "p");
            builder.AddAttribute(1, "id", "name");
            builder.AddContent(2, Data.GetRequired<Person>().Name);
            builder.CloseElement();
        }
    }

    sealed class MemoryStore : IPersistentComponentStateStore
    {
        public IDictionary<string, byte[]> Saved { get; private set; } = new Dictionary<string, byte[]>();

        public Task<IDictionary<string, byte[]>> GetPersistedStateAsync() => Task.FromResult(Saved);

        public Task PersistStateAsync(IReadOnlyDictionary<string, byte[]> state)
        {
            Saved = new Dictionary<string, byte[]>(state);

            return Task.CompletedTask;
        }
    }

    /// <summary>One browser-or-server side: its own container, persistent state and router.</summary>
    sealed class Side(BunitContext context, ComponentStatePersistenceManager manager, IRenderedComponent<Components.ModRouter> router)
    {
        public BunitContext Context { get; } = context;

        public ComponentStatePersistenceManager Manager { get; } = manager;

        public string? Name => router.FindAll("#name").FirstOrDefault()?.TextContent;
    }

    readonly List<BunitContext> _contexts = [];

    [SetUp]
    public void SetUp()
    {
        PersonLoader.Calls = 0;
        PersonLoader.Defer = false;
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (var context in _contexts)
        {
            await context.DisposeAsync();
        }

        _contexts.Clear();
    }

    static void PersistedRoutes(RouteMap map) => map
        .Route("/person/{id:int}", typeof(PersonPage), name: "person").Loader<PersonLoader>().PersistLoaderData<Person>();

    static void PlainRoutes(RouteMap map) => map
        .Route("/person/{id:int}", typeof(PersonPage), name: "person").Loader<PersonLoader>();

    async Task<Side> MountAsync(Action<RouteMap> routes, string uri, MemoryStore? restoreFrom = null)
    {
        var context = new BunitContext();
        _contexts.Add(context);

        var manager = new ComponentStatePersistenceManager(NullLogger<ComponentStatePersistenceManager>.Instance);

        if (restoreFrom is not null)
        {
            await manager.RestoreStateAsync(restoreFrom);
        }

        context.Services.AddSingleton(manager.State);
        context.Services.AddModRouter(routes);
        context.Services.AddSingleton<INavigationPlatformAdapter>(new FakeNavigationAdapter("http://localhost/", uri));

        return new Side(context, manager, context.Render<Components.ModRouter>());
    }

    static async Task<MemoryStore> HandOverAsync(Side server)
    {
        var store = new MemoryStore();

        await server.Manager.PersistStateAsync(store, server.Context.Renderer);

        return store;
    }

    [Test]
    public async Task The_interactive_pass_restores_the_persisted_data_and_the_loader_does_not_run_again()
    {
        var server = await MountAsync(PersistedRoutes, "http://localhost/person/7");
        Assert.That(PersonLoader.Calls, Is.EqualTo(1));
        Assert.That(server.Name, Is.EqualTo("Person 7 by call 1"));

        var store = await HandOverAsync(server);
        var client = await MountAsync(PersistedRoutes, "http://localhost/person/7", store);

        Assert.That(PersonLoader.Calls, Is.EqualTo(1), "the loader ran on the server only");
        Assert.That(client.Name, Is.EqualTo("Person 7 by call 1"));
    }

    [Test]
    public async Task Without_PersistLoaderData_the_loader_runs_again_on_the_interactive_pass()
    {
        var server = await MountAsync(PlainRoutes, "http://localhost/person/7");
        var store = await HandOverAsync(server);

        var client = await MountAsync(PlainRoutes, "http://localhost/person/7", store);

        Assert.That(store.Saved, Is.Empty, "nothing opted in, so nothing is persisted");
        Assert.That(PersonLoader.Calls, Is.EqualTo(2));
        Assert.That(client.Name, Is.EqualTo("Person 7 by call 2"));
    }

    [Test]
    public async Task A_level_that_also_defers_a_value_is_not_persisted_and_loads_again()
    {
        PersonLoader.Defer = true;
        var server = await MountAsync(PersistedRoutes, "http://localhost/person/7");
        var store = await HandOverAsync(server);

        var client = await MountAsync(PersistedRoutes, "http://localhost/person/7", store);

        Assert.That(store.Saved, Is.Empty, "deferred values are live tasks and cannot travel");
        Assert.That(PersonLoader.Calls, Is.EqualTo(2));
        Assert.That(client.Name, Is.EqualTo("Person 7 by call 2"));
    }

    [Test]
    public async Task Persisted_data_is_used_only_for_the_url_it_was_loaded_for()
    {
        var server = await MountAsync(PersistedRoutes, "http://localhost/person/7");
        var store = await HandOverAsync(server);

        var client = await MountAsync(PersistedRoutes, "http://localhost/person/8", store);

        Assert.That(PersonLoader.Calls, Is.EqualTo(2));
        Assert.That(client.Name, Is.EqualTo("Person 8 by call 2"));
    }

    [Test]
    public async Task The_persisted_copy_is_consumed_by_the_first_pass()
    {
        var server = await MountAsync(PersistedRoutes, "http://localhost/person/7");
        var store = await HandOverAsync(server);
        var client = await MountAsync(PersistedRoutes, "http://localhost/person/7", store);
        Assert.That(PersonLoader.Calls, Is.EqualTo(1));

        await client.Context.Services.GetRequiredService<INavigationService>().ReloadAsync();

        Assert.That(PersonLoader.Calls, Is.EqualTo(2), "a reload loads again; the restored copy was taken by the first pass");
    }

    [Test]
    public void PersistLoaderData_without_a_loader_is_a_startup_error()
    {
        var services = new ServiceCollection();

        var error = Assert.Throws<RouteCompilationException>(() => services.AddModRouter(map => map
            .Route("/person/{id:int}", typeof(PersonPage), name: "person").PersistLoaderData<Person>()))!;

        Assert.That(error.Message, Does.Contain("RT0025").And.Contain("needs a loader"));
    }

    [Test]
    public void PersistLoaderData_twice_on_one_route_is_a_startup_error()
    {
        var services = new ServiceCollection();

        var error = Assert.Throws<RouteCompilationException>(() => services.AddModRouter(map => map
            .Route("/person/{id:int}", typeof(PersonPage), name: "person").Loader<PersonLoader>().PersistLoaderData<Person>().PersistLoaderData<Person>()))!;

        Assert.That(error.Message, Does.Contain("RT0025").And.Contain("only once"));
    }

    [Route("/attributed/{id:int}")]
    [RouteLoader<PersonLoader>]
    [RoutePersistLoaderData<Person>]
    public sealed class AttributedPage : PersonPage;

    [Test]
    public void The_attribute_declares_the_same_persistence_as_the_fluent_call()
    {
        var map = RouteMap.Create();
        map.AddPages(typeof(LoaderStatePersistenceTests).Assembly, type => type == typeof(AttributedPage));

        var definition = map.Build().Single();

        Assert.That(definition.Metadata.Contains<RoutePersistedDataMetadata>(), Is.True);
        Assert.That(definition.Metadata.GetRequired<RoutePersistedDataMetadata>().DataType, Is.EqualTo(typeof(Person)));
    }
}
