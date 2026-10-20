using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ModRouter.Components;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Guards;
using ModRouter.Core.Loading;
using ModRouter.Discovery;
using ModRouter.Navigation;
using static ModRouter.Test.ComponentTestKit;

namespace ModRouter.Test;

/// <summary>
/// <c>CacheFor</c> keeps the critical data of a loader per level, parameters and query: a fresh entry skips the loader, a stale
/// one is shown at once and refreshed in the background. <c>RevalidateAsync</c> and <c>PreloadAsync</c> use the same cache.
/// </summary>
[TestFixture]
public sealed class LoaderCacheTests
{
    public sealed record Item(int Id, int Call);

    public sealed class ItemLoader : IRouteLoader
    {
        public static int Calls;

        public static bool Fail;

        public ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context)
        {
            var call = Interlocked.Increment(ref Calls);

            if (Fail)
            {
                throw new InvalidOperationException("boom");
            }

            return ValueTask.FromResult(RouteLoadResult.Create(new Item(Convert.ToInt32(context.Parameters.Values["id"], System.Globalization.CultureInfo.InvariantCulture), call)));
        }
    }

    public sealed class DeferringLoader : IRouteLoader
    {
        public static int Calls;

        public ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context)
        {
            var call = Interlocked.Increment(ref Calls);

            return ValueTask.FromResult(RouteLoadResult.Create(new Item(1, call)).Defer("later", Task.FromResult(call)));
        }
    }

    public sealed class GatedLoader : IRouteLoader
    {
        public static TaskCompletionSource Gate { get; set; } = new();

        public async ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context)
        {
            await Gate.Task.WaitAsync(context.CancellationToken);

            return RouteLoadResult.Create(new Item(0, 0));
        }
    }

    public sealed class CountingGuard : IRouteGuard
    {
        public static int Calls;

        public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context)
        {
            Interlocked.Increment(ref Calls);

            return ValueTask.FromResult(RouteGuardResult.Allow());
        }
    }

    static readonly TimeSpan Minute = TimeSpan.FromMinutes(1);

    static void Routes(RouteMap map) => map
        .Route("/", typeof(ShowProbe), name: "home")
        .Route("/about", typeof(ShowProbe), name: "about")
        .Route("/items/{id:int}", typeof(ShowProbe), name: "item").Loader<ItemLoader>().CacheFor(Minute, TimeSpan.FromMinutes(5))
        .Route("/fresh/{id:int}", typeof(ShowProbe), name: "fresh").Loader<ItemLoader>().CacheFor(Minute)
        .Route("/plain/{id:int}", typeof(ShowProbe), name: "plain").Loader<ItemLoader>()
        .Route("/deferred", typeof(ShowProbe), name: "deferred").Loader<DeferringLoader>().CacheFor(Minute)
        .Route("/guarded/{id:int}", typeof(ShowProbe), name: "guarded").Guard<CountingGuard>().Loader<ItemLoader>().CacheFor(Minute)
        .Route("/slow", typeof(ShowProbe), name: "slow").Loader<GatedLoader>();

    BunitContext _ctx = default!;
    FakeTimeProvider _time = default!;
    FakeNavigationAdapter _adapter = default!;
    IDisposable _lease = default!;

    INavigationService Navigation => _ctx.Services.GetRequiredService<INavigationService>();

    Item? Shown => Navigation.Current?.LevelData.FirstOrDefault()?.Data as Item;

    async Task StartAsync(Action<ModRouterOptions>? configure = null)
    {
        _time = new FakeTimeProvider();
        _ctx = new BunitContext();
        _ctx.Services.AddSingleton<TimeProvider>(_time);
        _ctx.Services.AddModRouter(Routes, configure);
        _adapter = new FakeNavigationAdapter("http://localhost/", "http://localhost/") { ExecuteNavigateTo = true };
        _ctx.Services.AddSingleton<INavigationPlatformAdapter>(_adapter);

        var coordinator = _ctx.Services.GetRequiredService<NavigationCoordinator>();
        _lease = coordinator.Attach();
        await coordinator.NavigateInitialAsync();
    }

    [SetUp]
    public void SetUp()
    {
        ItemLoader.Calls = 0;
        ItemLoader.Fail = false;
        DeferringLoader.Calls = 0;
        CountingGuard.Calls = 0;
        GatedLoader.Gate = new TaskCompletionSource();
    }

    [TearDown]
    public async Task TearDown()
    {
        GatedLoader.Gate.TrySetResult();
        _lease?.Dispose();

        if (_ctx is not null)
        {
            await _ctx.DisposeAsync();
        }
    }

    // ---- the cache ------------------------------------------------------------------------------------------------

    [Test]
    public async Task A_fresh_entry_skips_the_loader()
    {
        await StartAsync();
        await Navigation.NavigateToAsync("items/1");
        await Navigation.NavigateToAsync("about");

        _time.Advance(TimeSpan.FromSeconds(30));
        var result = await Navigation.NavigateToAsync("items/1");

        Assert.That(result.Succeeded, Is.True);
        Assert.That(ItemLoader.Calls, Is.EqualTo(1), "the second visit used the cache");
        Assert.That(Shown, Is.EqualTo(new Item(1, 1)));
    }

    [Test]
    public async Task Parameters_and_query_make_separate_entries()
    {
        await StartAsync();

        await Navigation.NavigateToAsync("items/1");
        await Navigation.NavigateToAsync("items/2");
        await Navigation.NavigateToAsync("items/1?tab=a");
        await Navigation.NavigateToAsync("items/1");

        Assert.That(ItemLoader.Calls, Is.EqualTo(3), "items/1, items/2 and items/1?tab=a each loaded once; the last visit hit the first entry");
    }

    [Test]
    public async Task A_route_without_CacheFor_always_loads()
    {
        await StartAsync();

        await Navigation.NavigateToAsync("plain/1");
        await Navigation.NavigateToAsync("about");
        await Navigation.NavigateToAsync("plain/1");

        Assert.That(ItemLoader.Calls, Is.EqualTo(2));
    }

    [Test]
    public async Task An_entry_past_both_windows_is_a_miss()
    {
        await StartAsync();
        await Navigation.NavigateToAsync("fresh/1");
        await Navigation.NavigateToAsync("about");

        _time.Advance(Minute + TimeSpan.FromSeconds(1));
        await Navigation.NavigateToAsync("fresh/1");

        Assert.That(ItemLoader.Calls, Is.EqualTo(2), "no stale window: the loader ran again before the page showed");
        Assert.That(Shown, Is.EqualTo(new Item(1, 2)));
    }

    [Test]
    public async Task A_result_with_a_deferred_value_is_never_cached()
    {
        await StartAsync();

        await Navigation.NavigateToAsync("deferred");
        await Navigation.NavigateToAsync("about");
        await Navigation.NavigateToAsync("deferred");

        Assert.That(DeferringLoader.Calls, Is.EqualTo(2));
    }

    [Test]
    public async Task The_cache_is_capped_and_the_oldest_entry_goes_first()
    {
        await StartAsync(options => options.LoaderCache.MaxEntries = 2);

        await Navigation.NavigateToAsync("items/1");
        _time.Advance(TimeSpan.FromSeconds(1));
        await Navigation.NavigateToAsync("items/2");
        _time.Advance(TimeSpan.FromSeconds(1));
        await Navigation.NavigateToAsync("items/3");
        Assert.That(ItemLoader.Calls, Is.EqualTo(3));

        await Navigation.NavigateToAsync("items/3");
        Assert.That(ItemLoader.Calls, Is.EqualTo(3), "the newest entry is still there");

        await Navigation.NavigateToAsync("items/1");
        Assert.That(ItemLoader.Calls, Is.EqualTo(4), "items/1 was evicted");
    }

    [Test]
    public async Task ClearLoaderCache_forgets_everything()
    {
        await StartAsync();
        await Navigation.NavigateToAsync("items/1");
        await Navigation.NavigateToAsync("about");

        Navigation.ClearLoaderCache();
        await Navigation.NavigateToAsync("items/1");

        Assert.That(ItemLoader.Calls, Is.EqualTo(2));
    }

    [Test]
    public async Task A_reload_bypasses_the_cache_and_refreshes_it()
    {
        await StartAsync();
        await Navigation.NavigateToAsync("items/1");

        await Navigation.ReloadAsync();
        Assert.That(ItemLoader.Calls, Is.EqualTo(2), "a reload asks for fresh data");

        await Navigation.NavigateToAsync("about");
        await Navigation.NavigateToAsync("items/1");

        Assert.That(ItemLoader.Calls, Is.EqualTo(2));
        Assert.That(Shown, Is.EqualTo(new Item(1, 2)), "the reloaded value is what was cached");
    }

    // ---- stale while revalidate -------------------------------------------------------------------------------------

    [Test]
    public async Task A_stale_entry_shows_at_once_and_is_refreshed_in_the_background()
    {
        await StartAsync();
        await Navigation.NavigateToAsync("items/1");
        await Navigation.NavigateToAsync("about");

        _time.Advance(TimeSpan.FromMinutes(2));
        var result = await Navigation.NavigateToAsync("items/1");

        Assert.That(result.Succeeded, Is.True);
        await EventuallyAsync(() => Shown, Is.EqualTo(new Item(1, 2)));
        Assert.That(ItemLoader.Calls, Is.EqualTo(2));
        Assert.That(Navigation.Current!.Path, Is.EqualTo("/items/1"));
        Assert.That(Navigation.IsNavigating, Is.False, "a background refresh shows no navigation indicator");
    }

    [Test]
    public async Task A_refresh_that_fails_leaves_the_stale_page_alone()
    {
        await StartAsync();
        await Navigation.NavigateToAsync("items/1");
        await Navigation.NavigateToAsync("about");
        _time.Advance(TimeSpan.FromMinutes(2));
        ItemLoader.Fail = true;

        await Navigation.NavigateToAsync("items/1");
        await EventuallyAsync(() => ItemLoader.Calls, Is.EqualTo(2));

        Assert.That(Navigation.Current!.Error, Is.Null);
        Assert.That(Shown, Is.EqualTo(new Item(1, 1)), "the stale data stays on screen");
    }

    // ---- RevalidateAsync --------------------------------------------------------------------------------------------

    [Test]
    public async Task Revalidate_reloads_without_guards_and_replaces_the_data()
    {
        await StartAsync();
        await Navigation.NavigateToAsync("guarded/1");
        Assert.That(CountingGuard.Calls, Is.EqualTo(1));

        var result = await Navigation.RevalidateAsync();

        Assert.That(result.Succeeded, Is.True);
        Assert.That(CountingGuard.Calls, Is.EqualTo(1), "no guard runs on a revalidation");
        Assert.That(ItemLoader.Calls, Is.EqualTo(2));
        Assert.That(Shown, Is.EqualTo(new Item(1, 2)));
        Assert.That(Navigation.Current!.Path, Is.EqualTo("/guarded/1"));
        Assert.That(_adapter.History, Has.Count.EqualTo(2), "no history entry was added");
    }

    [Test]
    public async Task Revalidate_keeps_the_page_when_a_loader_fails()
    {
        await StartAsync();
        await Navigation.NavigateToAsync("items/1");
        ItemLoader.Fail = true;

        var result = await Navigation.RevalidateAsync();

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Failed));
        Assert.That(Navigation.Current!.Error, Is.Null);
        Assert.That(Shown, Is.EqualTo(new Item(1, 1)));
    }

    [Test]
    public async Task Revalidate_does_nothing_while_a_navigation_is_in_flight()
    {
        await StartAsync();
        await Navigation.NavigateToAsync("items/1");
        var held = Navigation.NavigateToAsync("slow");
        Assert.That(held.IsCompleted, Is.False);

        var result = await Navigation.RevalidateAsync();

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Cancelled));
        GatedLoader.Gate.SetResult();
        Assert.That((await held).Succeeded, Is.True, "the navigation was not disturbed");
    }

    // ---- PreloadAsync -----------------------------------------------------------------------------------------------

    [Test]
    public async Task A_preload_fills_the_cache_without_navigating_or_running_a_guard()
    {
        await StartAsync();

        await Navigation.PreloadAsync("guarded/5");

        Assert.That(ItemLoader.Calls, Is.EqualTo(1));
        Assert.That(CountingGuard.Calls, Is.Zero, "guards never run on a preload");
        Assert.That(Navigation.Current!.Path, Is.EqualTo("/"));
        Assert.That(Navigation.IsNavigating, Is.False);

        await Navigation.NavigateToAsync("guarded/5");

        Assert.That(ItemLoader.Calls, Is.EqualTo(1), "the navigation found the preloaded data");
        Assert.That(Shown, Is.EqualTo(new Item(5, 1)));
        Assert.That(CountingGuard.Calls, Is.EqualTo(1), "the navigation itself still runs its guards");
    }

    [Test]
    public async Task Preloading_what_is_fresh_loads_nothing_and_a_stale_entry_is_refreshed()
    {
        await StartAsync();
        await Navigation.PreloadAsync("items/5");
        await Navigation.PreloadAsync("items/5");
        Assert.That(ItemLoader.Calls, Is.EqualTo(1));

        _time.Advance(TimeSpan.FromMinutes(2));
        await Navigation.PreloadAsync("items/5");

        Assert.That(ItemLoader.Calls, Is.EqualTo(2), "a stale entry counts as a miss for a preload");

        await Navigation.NavigateToAsync("items/5");
        Assert.That(ItemLoader.Calls, Is.EqualTo(2), "and the page now finds a fresh one, so nothing refreshes behind it");
    }

    [Test]
    public async Task A_preload_of_a_level_that_does_not_cache_loads_nothing()
    {
        await StartAsync();

        await Navigation.PreloadAsync("plain/1");

        Assert.That(ItemLoader.Calls, Is.Zero);
    }

    [Test]
    public async Task A_preload_does_not_disturb_a_navigation_in_flight()
    {
        await StartAsync();
        var held = Navigation.NavigateToAsync("slow");
        Assert.That(held.IsCompleted, Is.False);

        await Navigation.PreloadAsync("items/9");
        GatedLoader.Gate.SetResult();

        Assert.That((await held).Status, Is.EqualTo(NavigationStatus.Succeeded));
        Assert.That(ItemLoader.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task An_unmatched_or_external_target_preloads_nothing()
    {
        await StartAsync();

        await Navigation.PreloadAsync("nowhere");
        await Navigation.PreloadAsync("https://elsewhere.example/items/1");

        Assert.That(ItemLoader.Calls, Is.Zero);
    }

    // ---- RouteLink ----------------------------------------------------------------------------------------------------

    [Test]
    public async Task A_link_preloads_on_intent_after_the_delay_and_not_when_the_pointer_leaves_first()
    {
        await StartAsync();
        var link = _ctx.Render<RouteLink>(p => p.Add(l => l.Route, "item").Add(l => l.Parameters, new { id = 4 }).Add(l => l.Preload, RoutePreload.Intent));
        Assert.That(link.Markup, Does.Contain("blazor:onmouseenter"), "the intent handlers are attached");

        // The handlers run on the renderer's dispatcher: wait for each to finish before the clock moves.
        await link.Find("a").MouseEnterAsync();
        _time.Advance(TimeSpan.FromMilliseconds(20));
        await link.Find("a").MouseLeaveAsync();
        _time.Advance(TimeSpan.FromMilliseconds(200));
        Assert.That(ItemLoader.Calls, Is.Zero, "the pointer left before the delay passed");

        await link.Find("a").MouseEnterAsync();
        _time.Advance(TimeSpan.FromMilliseconds(60));
        await EventuallyAsync(() => ItemLoader.Calls, Is.EqualTo(1));

        await Navigation.NavigateToAsync("items/4");
        Assert.That(ItemLoader.Calls, Is.EqualTo(1), "the click found the preloaded data");
    }

    [Test]
    public async Task A_link_with_Render_preloads_when_it_first_renders()
    {
        await StartAsync();

        _ctx.Render<RouteLink>(p => p.Add(l => l.Route, "item").Add(l => l.Parameters, new { id = 6 }).Add(l => l.Preload, RoutePreload.Render));

        await EventuallyAsync(() => ItemLoader.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task A_link_without_Preload_attaches_no_event_handlers()
    {
        await StartAsync();

        var link = _ctx.Render<RouteLink>(p => p.Add(l => l.Route, "item").Add(l => l.Parameters, new { id = 4 }));

        Assert.That(link.Markup, Does.Not.Contain("blazor:onmouseenter").And.Not.Contain("blazor:onfocus").And.Not.Contain("blazor:ontouchstart"));
    }

    // ---- RouteLink (the component preloads the same way) ------------------------------------------------------

    [Test]
    public async Task The_RouteLink_preloads_on_intent_after_the_delay_and_not_when_the_pointer_leaves_first()
    {
        await StartAsync();
        var link = _ctx.Render<Components.RouteLink>(p => p
            .Add(l => l.Route, "item").Add(l => l.Parameters, new { id = 4 }).Add(l => l.Preload, RoutePreload.Intent).AddChildContent("go"));
        Assert.That(link.Markup, Does.Contain("blazor:onmouseenter"), "the intent handlers are attached");

        await link.Find("a").MouseEnterAsync();
        _time.Advance(TimeSpan.FromMilliseconds(20));
        await link.Find("a").MouseLeaveAsync();
        _time.Advance(TimeSpan.FromMilliseconds(200));
        Assert.That(ItemLoader.Calls, Is.Zero, "the pointer left before the delay passed");

        await link.Find("a").FocusAsync();
        _time.Advance(TimeSpan.FromMilliseconds(60));
        await EventuallyAsync(() => ItemLoader.Calls, Is.EqualTo(1));

        await Navigation.NavigateToAsync("items/4");
        Assert.That(ItemLoader.Calls, Is.EqualTo(1), "the click found the preloaded data");
    }

    [Test]
    public async Task The_RouteLink_with_Render_preloads_when_it_first_renders_and_one_without_Preload_attaches_no_handlers()
    {
        await StartAsync();

        var rendered = _ctx.Render<Components.RouteLink>(p => p
            .Add(l => l.Route, "item").Add(l => l.Parameters, new { id = 6 }).Add(l => l.Preload, RoutePreload.Render).AddChildContent("go"));
        await EventuallyAsync(() => ItemLoader.Calls, Is.EqualTo(1));
        Assert.That(rendered.Markup, Does.Not.Contain("blazor:onmouseenter"), "a render preload needs no pointer handlers");

        var plain = _ctx.Render<Components.RouteLink>(p => p
            .Add(l => l.Route, "item").Add(l => l.Parameters, new { id = 4 }).AddChildContent("go"));

        Assert.That(plain.Markup, Does.Not.Contain("blazor:onmouseenter").And.Not.Contain("blazor:onfocus").And.Not.Contain("blazor:ontouchstart"));
    }

    // ---- declaration ------------------------------------------------------------------------------------------------

    [Test]
    public void CacheFor_needs_a_positive_fresh_window_and_a_non_negative_stale_one()
    {
        var map = RouteMap.Create().Route("/x", typeof(ShowProbe)).Loader<ItemLoader>();

        Assert.Throws<ArgumentOutOfRangeException>(() => map.CacheFor(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.CacheFor(Minute, TimeSpan.FromSeconds(-1)));
    }

    [Test]
    public void CacheFor_without_a_loader_is_a_startup_error()
    {
        var services = new ServiceCollection();

        var error = Assert.Throws<RouteCompilationException>(() => services.AddModRouter(map => map.Route("/x", typeof(ShowProbe)).CacheFor(Minute)))!;

        Assert.That(error.Message, Does.Contain("RT0026").And.Contain("needs a loader"));
    }

    [Test]
    public void The_cache_needs_room_for_at_least_one_entry()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddModRouter(configure: options => options.LoaderCache.MaxEntries = 0));
    }

    [Microsoft.AspNetCore.Components.Route("/attributed")]
    [RouteLoader<ItemLoader>]
    [RouteLoaderCache(60, 30)]
    public sealed class AttributedPage : Microsoft.AspNetCore.Components.ComponentBase;

    [Test]
    public void The_attribute_declares_the_same_windows_as_the_fluent_call()
    {
        var map = RouteMap.Create();
        map.AddPages(typeof(LoaderCacheTests).Assembly, type => type == typeof(AttributedPage));

        var rules = map.Build().Single().Metadata.GetRequired<RouteLoaderCacheMetadata>();

        Assert.That(rules, Is.EqualTo(new RouteLoaderCacheMetadata(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(30))));
    }
}
