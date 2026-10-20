using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ModRouter.Core.Definitions;
using ModRouter.Core.Generation;
using ModRouter.Core.Guards;
using ModRouter.Core.Loading;
using ModRouter.Navigation;

namespace ModRouter.Test;

/// <summary>
/// <c>NavigateToAsync</c> tells how a navigation ended, and the history state of <see cref="ModNavigationOptions"/> travels
/// with it.
/// </summary>
[TestFixture]
public sealed class NavigateToAsyncTests
{
    sealed class DenyGuard : IRouteGuard
    {
        public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context) => ValueTask.FromResult(RouteGuardResult.NotAuthorized("no"));
    }

    sealed class CancelGuard : IRouteGuard
    {
        public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context) => ValueTask.FromResult(RouteGuardResult.Cancel());
    }

    sealed class RedirectGuard : IRouteGuard
    {
        public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context) => ValueTask.FromResult(RouteGuardResult.Redirect("about"));
    }

    sealed class ThrowingLoader : IRouteLoader
    {
        public ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context) => throw new InvalidOperationException("boom");
    }

    sealed class GatedLoader : IRouteLoader
    {
        public static TaskCompletionSource Gate { get; set; } = new();

        public async ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context)
        {
            await Gate.Task.WaitAsync(context.CancellationToken);

            return RouteLoadResult.Create();
        }
    }

    static void Routes(RouteMap map) => map
        .Route("/", typeof(ShowProbe), name: "home")
        .Route("/about", typeof(ShowProbe), name: "about")
        .Route("/items/{id:int}", typeof(ShowProbe), name: "item")
        .Route("/denied", typeof(ShowProbe), name: "denied").Guard<DenyGuard>()
        .Route("/cancel", typeof(ShowProbe), name: "cancel").Guard<CancelGuard>()
        .Route("/hop", typeof(ShowProbe), name: "hop").Guard<RedirectGuard>()
        .Route("/boom", typeof(ShowProbe), name: "boom").Loader<ThrowingLoader>()
        .Route("/slow", typeof(ShowProbe), name: "slow").Loader<GatedLoader>();

    BunitContext _ctx = default!;
    RouterHarness _router = default!;

    INavigationService Navigation => _ctx.Services.GetRequiredService<INavigationService>();

    [SetUp]
    public async Task SetUp()
    {
        GatedLoader.Gate = new TaskCompletionSource();
        _ctx = new BunitContext();
        _router = await RouterHarness.StartAsync(_ctx, Routes, "http://localhost/");
    }

    [TearDown]
    public async Task TearDown()
    {
        GatedLoader.Gate.TrySetResult();
        await _router.DisposeAsync();
        await _ctx.DisposeAsync();
    }

    [Test]
    public async Task A_page_that_shows_is_a_success()
    {
        var result = await Navigation.NavigateToAsync("about");

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Succeeded));
        Assert.That(result.Succeeded, Is.True);
        Assert.That(Navigation.Current!.Path, Is.EqualTo("/about"));
    }

    [Test]
    public async Task Navigating_to_the_page_already_shown_succeeds_at_once()
    {
        await Navigation.NavigateToAsync("about");

        var result = await Navigation.NavigateToAsync("about");

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Succeeded));
    }

    [Test]
    public async Task An_unmatched_url_reports_not_found_and_changes_the_url()
    {
        var result = await Navigation.NavigateToAsync("nowhere");

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.NotFound));
        Assert.That(Navigation.Current!.Failure, Is.EqualTo(RouteFailure.NotFound));
    }

    [Test]
    public async Task A_denied_level_reports_not_authorized_with_its_error()
    {
        var result = await Navigation.NavigateToAsync("denied");

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.NotAuthorized));
        Assert.That(result.Error!.Kind, Is.EqualTo(RouteErrorKind.NotAuthorized));
        Assert.That(result.Succeeded, Is.False);
    }

    [Test]
    public async Task A_guard_that_cancels_leaves_the_old_page_and_reports_cancelled()
    {
        await Navigation.NavigateToAsync("about");

        var result = await Navigation.NavigateToAsync("cancel");

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Cancelled));
        Assert.That(Navigation.Current!.Path, Is.EqualTo("/about"));
    }

    [Test]
    public async Task A_guard_that_redirects_reports_the_target_and_that_navigation_goes_on_by_itself()
    {
        await Navigation.NavigateToAsync("items/1");

        var result = await Navigation.NavigateToAsync("hop");
        await _router.Adapter.SettledAsync();

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Redirected));
        Assert.That(result.RedirectedTo, Does.EndWith("/about"));
        Assert.That(Navigation.Current!.Path, Is.EqualTo("/about"));
    }

    [Test]
    public async Task A_loader_that_throws_commits_an_error_and_reports_failed()
    {
        var result = await Navigation.NavigateToAsync("boom");

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Failed));
        Assert.That(result.Error!.Kind, Is.EqualTo(RouteErrorKind.LoadFailed));
    }

    [Test]
    public async Task A_newer_navigation_supersedes_one_still_loading()
    {
        var first = Navigation.NavigateToAsync("slow");
        Assert.That(first.IsCompleted, Is.False, "the loader holds it");

        var second = await Navigation.NavigateToAsync("about");

        Assert.That(second.Status, Is.EqualTo(NavigationStatus.Succeeded));
        Assert.That((await first).Status, Is.EqualTo(NavigationStatus.Superseded));
        Assert.That(Navigation.Current!.Path, Is.EqualTo("/about"));
    }

    [Test]
    public async Task A_navigation_in_flight_when_the_router_goes_away_is_cancelled()
    {
        var pending = Navigation.NavigateToAsync("slow");
        Assert.That(pending.IsCompleted, Is.False);

        await _router.DisposeAsync();

        Assert.That((await pending).Status, Is.EqualTo(NavigationStatus.Cancelled));
    }

    [Test]
    public async Task The_token_stops_the_wait_without_touching_the_navigation()
    {
        using var source = new CancellationTokenSource();
        var pending = Navigation.NavigateToAsync("slow", cancellationToken: source.Token);

        await source.CancelAsync();

        Assert.CatchAsync<OperationCanceledException>(async () => await pending);
    }

    [Test]
    public async Task A_named_route_navigates_and_reports()
    {
        var result = await Navigation.NavigateToRouteAsync("item", new { id = 3 });

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Succeeded));
        Assert.That(Navigation.Current!.Path, Is.EqualTo("/items/3"));
    }

    [Test]
    public void An_unknown_route_name_throws_before_anything_navigates()
    {
        var calls = _router.Adapter.NavigateCalls.Count;

        Assert.ThrowsAsync<RouteUrlGenerationException>(() => Navigation.NavigateToRouteAsync("nope"));
        Assert.That(_router.Adapter.NavigateCalls, Has.Count.EqualTo(calls));
    }

    [Test]
    public async Task A_forced_load_is_handed_to_the_browser_and_succeeds_at_once()
    {
        var result = await Navigation.NavigateToAsync("about", new ModNavigationOptions { ForceLoad = true });

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Succeeded));
        Assert.That(_router.Adapter.NavigateCalls[^1].ForceLoad, Is.True);
    }

    [Test]
    public async Task Without_a_mounted_router_there_is_nothing_to_wait_for()
    {
        await using var bare = new BunitContext();
        bare.Services.AddModRouter(Routes);
        bare.Services.AddSingleton<INavigationPlatformAdapter>(new FakeNavigationAdapter());

        Assert.ThrowsAsync<InvalidOperationException>(() => bare.Services.GetRequiredService<INavigationService>().NavigateToAsync("about"));
    }

    // ---- history state ---------------------------------------------------------------------------------------------

    [Test]
    public async Task The_history_state_is_stored_with_the_entry_and_read_back_from_the_state()
    {
        var result = await Navigation.NavigateToAsync("items/1", new ModNavigationOptions { HistoryState = "page=3" });

        Assert.That(result.Succeeded, Is.True);
        Assert.That(_router.Adapter.NavigateCalls[^1].State, Is.EqualTo("page=3"));
        Assert.That(Navigation.Current!.HistoryEntryState, Is.EqualTo("page=3"));
    }

    [Test]
    public async Task The_synchronous_overload_carries_the_history_state_too()
    {
        Navigation.NavigateTo("items/2", new ModNavigationOptions { HistoryState = "x", ReplaceHistory = true });
        await _router.Adapter.SettledAsync();

        Assert.That(_router.Adapter.NavigateCalls[^1], Is.EqualTo(("items/2", true, false, "x")));
        Assert.That(Navigation.Current!.HistoryEntryState, Is.EqualTo("x"));
    }
}
