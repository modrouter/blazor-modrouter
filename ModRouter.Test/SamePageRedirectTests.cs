using ModRouter.Core.Guards;
using ModRouter.Core.Loading;
using ModRouter.Diagnostics;
using ModRouter.Navigation;

namespace ModRouter.Test;

/// <summary>
/// A redirect whose target is exactly where the user already is (the committed page's path and query, and the location
/// the platform shows, fragment included) goes nowhere: the redirecting navigation is prevented, ends as
/// <see cref="NavigationRecordOutcome.Redirected"/>, and the history entry and its state are left alone. Any other target,
/// including the same page with a different query or fragment, is still navigated, and loop detection runs first.
/// </summary>
[TestFixture]
public sealed class SamePageRedirectTests
{
    const string Base = "http://localhost/";

    static Pipeline Create(string? uri = null)
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/", typeof(HomePage), name: "home")
            .Route("/a", typeof(OtherPage), name: "a")
            .Route("/b", typeof(OtherPage), name: "b").Guard<AuthGuard>()
            .Route("/c", typeof(CustomersPage), name: "c").Loader<CustomersLoader>()
            .Redirect("/to-a", "a"));

        return Pipeline.Create(table, uri: uri, configure: o => o.Diagnostics.HistorySize = 20);
    }

    /// <summary>The user is on <c>/a</c>, which committed with a history entry state of its own.</summary>
    static async Task<Pipeline> OnPageAAsync()
    {
        var p = Create();
        await p.Coordinator.NavigateInitialAsync();
        Assert.That(await p.Adapter.NavigateAsync("a", intercepted: true, historyState: "a-state"), Is.True);

        return p;
    }

    static NavigationRecord RecordOf(Pipeline p, string target)
        => p.Diagnostics.History.Single(record => record.Target == target);

    static void AssertNothingMoved(Pipeline p, RouteState shown, int commits, string redirecting)
    {
        var record = RecordOf(p, redirecting);

        Assert.Multiple(() =>
        {
            Assert.That(p.Adapter.History, Is.EqualTo(new[] { Base, Base + "a" }), "no duplicate history entry");
            Assert.That(p.Adapter.NavigateCalls, Is.Empty, "the platform was not asked to navigate");
            Assert.That(p.Commits, Has.Count.EqualTo(commits), "nothing committed");
            Assert.That(p.Current, Is.SameAs(shown));
            Assert.That(p.Adapter.HistoryEntryState, Is.EqualTo("a-state"), "the entry's state is untouched");
            Assert.That(p.Store.IsNavigating, Is.False);
            Assert.That((record.Outcome, record.OutcomeDetail), Is.EqualTo((NavigationRecordOutcome.Redirected, "/a")), "the redirecting navigation's record");
        });
    }

    [Test]
    public async Task A_guard_redirect_to_the_current_page_does_not_touch_the_history()
    {
        using var p = await OnPageAAsync();
        var shown = p.Current;
        var commits = p.Commits.Count;
        p.Guards.Set("auth", RouteGuardResult.Redirect("a"));

        Assert.That(await p.GoAsync("b"), Is.False, "the redirecting navigation is prevented");

        AssertNothingMoved(p, shown, commits, "/b");
    }

    [Test]
    public async Task A_redirect_rule_to_the_current_page_does_the_same()
    {
        using var p = await OnPageAAsync();
        var shown = p.Current;
        var commits = p.Commits.Count;

        Assert.That(await p.GoAsync("to-a"), Is.False);

        AssertNothingMoved(p, shown, commits, "/to-a");
    }

    [Test]
    public async Task A_redirect_hop_that_comes_back_to_the_current_page_does_the_same()
    {
        // b -> to-a (a redirect rule) -> a: the second hop is the one that would have pushed the duplicate.
        using var p = await OnPageAAsync();
        var shown = p.Current;
        var commits = p.Commits.Count;
        p.Guards.Set("auth", RouteGuardResult.Redirect("to-a"));

        Assert.That(await p.GoAsync("b"), Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(p.Adapter.History, Is.EqualTo(new[] { Base, Base + "a" }));
            Assert.That(p.Adapter.NavigateCalls.Select(c => c.Uri), Is.EqualTo(new[] { "to-a" }), "only the first hop navigated");
            Assert.That(p.Commits, Has.Count.EqualTo(commits));
            Assert.That(p.Current, Is.SameAs(shown));
            Assert.That(p.Store.IsNavigating, Is.False);
            Assert.That(RecordOf(p, "/to-a").Outcome, Is.EqualTo(NavigationRecordOutcome.Redirected));
        });
    }

    [Test]
    public async Task A_redirect_to_the_current_page_with_a_different_query_still_navigates()
    {
        using var p = await OnPageAAsync();
        p.Guards.Set("auth", RouteGuardResult.Redirect("a?x=1"));

        Assert.That(await p.GoAsync("b"), Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(p.Adapter.History, Is.EqualTo(new[] { Base, Base + "a", Base + "a?x=1" }));
            Assert.That((p.Current.Path, p.Current.Query, p.Current.Trigger), Is.EqualTo(("/a", "?x=1", NavigationTrigger.Redirect)));
            Assert.That(p.Store.IsNavigating, Is.False);
        });
    }

    [Test]
    public async Task A_redirect_that_differs_only_by_fragment_still_navigates()
    {
        using var p = await OnPageAAsync();
        var shown = p.Current;
        p.Guards.Set("auth", RouteGuardResult.Redirect("a#top"));

        Assert.That(await p.GoAsync("b"), Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(p.Adapter.NavigateCalls.Select(c => (c.Uri, c.ReplaceHistory)), Is.EqualTo(new[] { ("a#top", false) }), "a push: the same-page jump is kept");
            Assert.That(p.Adapter.History, Is.EqualTo(new[] { Base, Base + "a", Base + "a#top" }));
            Assert.That(p.Current, Is.SameAs(shown), "a fragment jump commits nothing");
            Assert.That(p.Store.IsNavigating, Is.False);
        });
    }

    [Test]
    public async Task The_fragment_compared_is_the_one_the_platform_shows_after_a_same_page_jump()
    {
        using var p = await OnPageAAsync();
        var shown = p.Current;

        // A same-page jump moves the platform to /a#top and commits nothing: the state on screen keeps no fragment.
        Assert.That(await p.GoAsync("a#top"), Is.True);
        Assert.That(p.Current, Is.SameAs(shown));

        // To the location shown, fragment included: nowhere to go.
        p.Guards.Set("auth", RouteGuardResult.Redirect("a#top"));
        Assert.That(await p.GoAsync("b"), Is.False);
        Assert.That(p.Adapter.History, Is.EqualTo(new[] { Base, Base + "a", Base + "a#top" }), "no duplicate of /a#top");
        Assert.That(p.Adapter.NavigateCalls, Is.Empty);

        // To the page without the fragment: a jump back to the top of the page, still navigated.
        p.Guards.Set("auth", RouteGuardResult.Redirect("a"));
        Assert.That(await p.GoAsync("b"), Is.False);
        Assert.That(p.Adapter.History, Is.EqualTo(new[] { Base, Base + "a", Base + "a#top", Base + "a" }));
        Assert.That(p.Current, Is.SameAs(shown));
    }

    [Test]
    public async Task A_redirect_to_the_location_the_platform_shows_but_the_router_has_not_committed_still_navigates()
    {
        using var p = await OnPageAAsync();
        var gate = new TaskCompletionSource();
        var loads = 0;
        p.Loaders.Set("customers", async context =>
        {
            if (++loads == 1)
            {
                await gate.Task.WaitAsync(context.CancellationToken);
            }

            return RouteLoadResult.Create("customers");
        });

        // The platform moved to /c without the router's handler (a navigation it never saw): its fallback navigation is
        // still loading, so /a stays committed while the platform shows /c.
        p.Adapter.RaiseLocationChanged("c");
        Assert.That(p.Current.Path, Is.EqualTo("/a"));

        // A link whose guard redirects to /c supersedes the fallback; /c is where the platform is but not the page on
        // screen, so the redirect must still run and commit it.
        p.Guards.Set("auth", RouteGuardResult.Redirect("c"));
        Assert.That(await p.GoAsync("b"), Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(p.Adapter.NavigateCalls.Select(c => c.Uri), Is.EqualTo(new[] { "c" }));
            Assert.That((p.Current.Path, p.Current.Trigger), Is.EqualTo(("/c", NavigationTrigger.Redirect)), "the screen shows the location the platform shows");
            Assert.That(p.Store.IsNavigating, Is.False);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task A_reload_or_first_load_whose_guard_redirects_to_its_own_url_is_still_a_loop(bool reload)
    {
        using var p = Create(Base + "b");

        if (reload)
        {
            await p.Coordinator.NavigateInitialAsync();
            Assert.That(p.Current.Error, Is.Null, "the first load was allowed");
            p.Guards.Set("auth", RouteGuardResult.Redirect("b"));
            await p.Coordinator.ReloadAsync();
        }
        else
        {
            p.Guards.Set("auth", RouteGuardResult.Redirect("b"));
            await p.Coordinator.NavigateInitialAsync();
        }

        await p.Adapter.SettledAsync();

        Assert.Multiple(() =>
        {
            Assert.That((p.Current.Path, p.Current.Failure, p.Current.Error?.Kind), Is.EqualTo(("/b", RouteFailure.RedirectLoop, RouteErrorKind.RedirectLoop)));
            Assert.That(p.Adapter.NavigateCalls, Is.Empty);
            Assert.That(p.Store.IsNavigating, Is.False);
        });
    }
}
