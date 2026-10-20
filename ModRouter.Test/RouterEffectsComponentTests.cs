using System.Text.Json;
using Bunit;
using ModRouter.Components;
using ModRouter.Core.Definitions;
using ModRouter.Core.Guards;
using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using static ModRouter.Test.ComponentTestKit;

namespace ModRouter.Test;

/// <summary>What <c>ModRouter</c> asks the browser to do after each committed state, observed through a faked JS module.</summary>
[TestFixture]
public sealed class RouterEffectsComponentTests
{
    const string ModulePath = "./_content/ModRouter/mod-router.js";

    BunitContext _ctx = default!;
    FakeNavigationAdapter _adapter = default!;
    BunitJSModuleInterop _module = default!;
    GuardScript _guards = default!;

    [SetUp]
    public void SetUp()
    {
        Probes.Reset();
        _ctx = new BunitContext();
        _module = _ctx.JSInterop.SetupModule(ModulePath);
        _module.Mode = JSRuntimeMode.Loose;
        _guards = new GuardScript();
        _ctx.Services.AddSingleton(_guards);
    }

    [TearDown]
    public async Task TearDown()
    {
        Assert.That(_ctx.Renderer.UnhandledException.IsCompleted, Is.False, () => "a render exception was swallowed: " + _ctx.Renderer.UnhandledException.Result);

        await _ctx.DisposeAsync();
    }

    RouteStateStore Store => _ctx.Services.GetRequiredService<RouteStateStore>();

    static void Routes(RouteMap map) => map
        .Route("/", typeof(HomeProbe), name: "home", title: "Home")
        .Route("/about", typeof(ShowProbe), name: "about", title: "About")
        .Route("/same-a", typeof(ShowProbe), name: "same-a", title: "Same")
        .Route("/same-b", typeof(ShowProbe), name: "same-b", title: "Same")
        .Route("/quiet", typeof(ShowProbe), name: "quiet", title: "Quiet").NoAnnounce()
        .Route("/secret", typeof(ShowProbe), name: "secret", title: "Secret").Guard<AuthGuard>()
        .Route("/slow", typeof(ShowProbe), name: "slow", title: "Slow").Guard<AuditGuard>()
        .Route("/untitled", typeof(ShowProbe), name: "untitled");

    IRenderedComponent<Components.ModRouter> Mount(
        Action<ModRouterOptions>? configure = null,
        string uri = "http://localhost/",
        Action<ComponentParameterCollectionBuilder<Components.ModRouter>>? parameters = null,
        Action<RouteMap>? routes = null)
    {
        _ctx.Services.AddModRouter(routes ?? Routes, configure);
        _adapter = new FakeNavigationAdapter("http://localhost/", uri) { ExecuteNavigateTo = true };
        _ctx.Services.AddSingleton<INavigationPlatformAdapter>(_adapter);

        return _ctx.Render<Components.ModRouter>(p =>
        {
            p.Add(r => r.DefaultLayout, typeof(MainLayoutProbe));
            parameters?.Invoke(p);
        });
    }

    async Task GoAsync(string uri, bool historyTraversal = false)
    {
        await _adapter.NavigateAsync(uri, intercepted: !historyTraversal, historyTraversal: historyTraversal);
        await _adapter.SettledAsync();
    }

    IReadOnlyList<JsonElement> Calls(string identifier)
        => [.. _module.Invocations[identifier].Select(call => JsonSerializer.SerializeToElement(call.Arguments.Count == 1 ? call.Arguments[0] : call.Arguments))];

    IReadOnlyList<JsonElement> Effects => Calls("afterNavigation");

    static string? Text(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static JsonElement Member(JsonElement element, string property) => element.GetProperty(property);

    string Announcement(IRenderedComponent<Components.ModRouter> cut) => One(cut, "[data-mod-router-announcer]").TextContent;

    // ---- view transitions --------------------------------------------------------------------------------------

    void BrowserSupportsViewTransitions(bool supported = true) => _module.Setup<bool>("beginTransition", _ => true).SetResult(supported);

    [Test]
    public async Task A_page_change_runs_inside_a_view_transition_that_ends_after_the_render()
    {
        BrowserSupportsViewTransitions();
        var cut = Mount(o => o.ViewTransitions.Enabled = true);
        Assert.That(Calls("beginTransition"), Is.Empty, "the first load is never animated");

        await GoAsync("about");

        await EventuallyAsync(() => Calls("endTransition").Count, Is.EqualTo(1));
        Assert.That(Calls("beginTransition"), Has.Count.EqualTo(1));
        Assert.That(Calls("beginTransition")[0].GetInt32(), Is.EqualTo(300), "the timeout is passed to the module in milliseconds");
        Assert.That(One(cut, "#show").TextContent, Is.EqualTo("show"), "the new page is on screen");

        var all = _module.Invocations.Select(i => i.Identifier).ToList();
        Assert.That(all.IndexOf("endTransition"), Is.GreaterThan(all.LastIndexOf("afterNavigation")), "the picture of the new page is taken after its scroll and focus are set");
    }

    [Test]
    public async Task Without_the_option_no_transition_is_started()
    {
        BrowserSupportsViewTransitions();
        Mount();

        await GoAsync("about");

        Assert.That(Calls("beginTransition"), Is.Empty);
        Assert.That(Calls("endTransition"), Is.Empty);
    }

    [Test]
    public async Task A_change_of_query_or_fragment_only_is_not_animated()
    {
        BrowserSupportsViewTransitions();
        Mount(o => o.ViewTransitions.Enabled = true, uri: "http://localhost/about");

        await GoAsync("about?tab=2");
        await GoAsync("about?tab=2#top");

        Assert.That(Calls("beginTransition"), Is.Empty);
    }

    [Test]
    public async Task A_change_into_or_out_of_an_error_page_is_not_animated()
    {
        BrowserSupportsViewTransitions();
        _guards.Set("auth", RouteGuardResult.NotAuthorized("no"));
        var cut = Mount(o => o.ViewTransitions.Enabled = true);

        await GoAsync("secret");
        await EventuallyAsync(() => All(cut, "#show").Count, Is.EqualTo(0));
        await GoAsync("about");
        await EventuallyAsync(() => All(cut, "#show").Count, Is.EqualTo(1));

        Assert.That(Calls("beginTransition"), Is.Empty);
    }

    [Test]
    public async Task A_browser_that_declines_the_transition_leaves_nothing_to_end()
    {
        BrowserSupportsViewTransitions(supported: false);
        var cut = Mount(o => o.ViewTransitions.Enabled = true);

        await GoAsync("about");

        await EventuallyAsync(() => Calls("beginTransition").Count, Is.EqualTo(1));
        await EventuallyAsync(() => One(cut, "#show").TextContent, Is.EqualTo("show"));
        Assert.That(Calls("endTransition"), Is.Empty, "no transition was open, so none is ended");
    }

    [Test]
    public async Task A_configured_timeout_is_passed_and_a_failing_module_call_still_renders_the_page()
    {
        _module.SetupVoid("endTransition").SetException(new Microsoft.JSInterop.JSException("boom"));
        BrowserSupportsViewTransitions();
        var cut = Mount(o =>
        {
            o.ViewTransitions.Enabled = true;
            o.ViewTransitions.Timeout = TimeSpan.FromSeconds(2);
        });

        await GoAsync("about");

        await EventuallyAsync(() => Calls("endTransition").Count, Is.EqualTo(1));
        Assert.That(Calls("beginTransition")[0].GetInt32(), Is.EqualTo(2000));
        Assert.That(One(cut, "#show").TextContent, Is.EqualTo("show"));
    }

    [Test]
    public void The_view_transition_timeout_must_be_positive()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddModRouter(Routes, o => o.ViewTransitions.Timeout = TimeSpan.Zero));
    }

    // ---- per trigger -------------------------------------------------------------------------------------------

    [Test]
    public void The_initial_load_sets_the_title_and_starts_tracking_scroll_but_moves_nothing()
    {
        var cut = Mount(o => o.Title.Format = "{0} - Acme");

        var effects = Effects.Single();
        Assert.That(Text(effects, "title"), Is.EqualTo("Home - Acme"));
        Assert.That(Member(effects, "focus").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(Member(effects, "scroll").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(Calls("trackScroll"), Has.Count.EqualTo(1));
        Assert.That(Announcement(cut), Is.Empty, "nothing is announced for the first page");
    }

    [Test]
    public async Task A_link_navigation_sets_the_title_announces_it_focuses_the_heading_and_scrolls_to_the_top()
    {
        var cut = Mount();

        await GoAsync("about");

        var effects = Effects.Last();
        Assert.That(Effects, Has.Count.EqualTo(2));
        Assert.That(Text(effects, "title"), Is.EqualTo("About"));
        Assert.That(Text(Member(effects, "focus"), "selector"), Is.EqualTo("h1"));
        Assert.That(Member(effects, "focus").GetProperty("timeoutMs").GetInt32(), Is.EqualTo(1000));
        Assert.That(Text(Member(effects, "scroll"), "mode"), Is.EqualTo("top"));
        Assert.That(Announcement(cut), Is.EqualTo("About"));
    }

    [Test]
    public async Task Back_and_forward_restore_scroll_without_announcing_or_moving_focus()
    {
        var cut = Mount();
        await GoAsync("about");
        var announced = Announcement(cut);

        await GoAsync("/", historyTraversal: true);

        var effects = Effects.Last();
        Assert.That(Store.Current!.Trigger, Is.EqualTo(NavigationTrigger.HistoryRestore));
        Assert.That(Text(effects, "title"), Is.EqualTo("Home"));
        Assert.That(Member(effects, "focus").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(Text(Member(effects, "scroll"), "mode"), Is.EqualTo("restore"));
        Assert.That(Announcement(cut), Is.EqualTo(announced));
    }

    [Test]
    public async Task A_redirect_at_the_initial_load_sets_the_title_but_neither_announces_nor_moves_focus()
    {
        _guards.Set("auth", RouteGuardResult.Redirect("about"));
        var cut = Mount(uri: "http://localhost/secret");

        await _adapter.SettledAsync();

        var effects = Effects.Single();
        Assert.That(Store.Current!.Path, Is.EqualTo("/about"));
        Assert.That(Store.Current.Trigger, Is.EqualTo(NavigationTrigger.Redirect));
        Assert.That(Text(effects, "title"), Is.EqualTo("About"));
        Assert.That(Member(effects, "focus").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(Member(effects, "scroll").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(Announcement(cut), Is.Empty, "the first page the user sees is not announced, redirected or not");
    }

    [Test]
    public async Task A_redirect_from_a_link_is_announced_and_focused_like_any_new_page()
    {
        var cut = Mount();
        _guards.Set("auth", RouteGuardResult.Redirect("about"));

        await GoAsync("secret");

        var effects = Effects.Last();
        Assert.That(Store.Current!.Trigger, Is.EqualTo(NavigationTrigger.Redirect));
        Assert.That(Text(Member(effects, "focus"), "selector"), Is.EqualTo("h1"));
        Assert.That(Announcement(cut), Is.EqualTo("About"));
    }

    [Test]
    public async Task A_fragment_scrolls_to_its_target_instead_of_the_top()
    {
        Mount();

        await GoAsync("about#notes");

        var scroll = Member(Effects.Last(), "scroll");
        Assert.That(Text(scroll, "mode"), Is.EqualTo("fragment"));
        Assert.That(Text(scroll, "fragment"), Is.EqualTo("notes"));
    }

    [Test]
    public async Task A_retry_runs_no_effects_at_all()
    {
        Mount();
        _guards.Set("auth", RouteGuardResult.NotAuthorized());
        await GoAsync("secret");
        var before = Effects.Count;

        _guards.Set("auth", RouteGuardResult.Allow());
        await Store.Current!.Error!.Retry();

        Assert.That(Store.Current.Trigger, Is.EqualTo(NavigationTrigger.Reload));
        Assert.That(Effects, Has.Count.EqualTo(before));
    }

    // ---- once per committed state ------------------------------------------------------------------------------

    [Test]
    public async Task A_state_runs_its_effects_once_however_often_the_router_renders()
    {
        var cut = Mount();
        await GoAsync("about");
        var count = Effects.Count;

        cut.Render(p => p.Add(r => r.NotFound, b => b.AddContent(0, "x")));
        cut.Render(p => p.Add(r => r.NotFound, b => b.AddContent(0, "y")));

        Assert.That(Effects, Has.Count.EqualTo(count));
    }

    [Test]
    public async Task A_navigation_that_was_superseded_before_it_committed_runs_no_effects()
    {
        Mount();
        var gate = new TaskCompletionSource();
        _guards.Set("audit", async _ =>
        {
            await gate.Task;

            return RouteGuardResult.Allow();
        });

        var slow = _adapter.NavigateAsync("slow", intercepted: true);
        await GoAsync("about");
        gate.SetResult();
        await slow;

        Assert.That(Effects.Select(e => Text(e, "title")), Is.EqualTo(new[] { "Home", "About" }), "the superseded page never set a title");
    }

    // ---- announcement ------------------------------------------------------------------------------------------

    [Test]
    public async Task The_same_title_twice_in_a_row_is_announced_by_changing_the_text()
    {
        var cut = Mount();

        await GoAsync("same-a");
        var first = Announcement(cut);
        await GoAsync("same-b");
        var second = Announcement(cut);
        await GoAsync("about");
        await GoAsync("same-a");
        var fourth = Announcement(cut);

        Assert.That(first.Trim(' '), Is.EqualTo("Same"));
        Assert.That(second.Trim(' '), Is.EqualTo("Same"));
        Assert.That(second, Is.Not.EqualTo(first));
        Assert.That(fourth.Trim(' '), Is.EqualTo("Same"));
    }

    [Test]
    public async Task The_announcement_can_be_switched_off_globally()
    {
        var cut = Mount(o => o.Announce.Enabled = false);

        await GoAsync("about");

        Assert.That(Announcement(cut), Is.Empty);
        Assert.That(Text(Effects.Last(), "title"), Is.EqualTo("About"), "the title is not part of the announcement switch");
    }

    [Test]
    public async Task A_route_without_an_announcement_stays_silent_but_still_moves_focus()
    {
        var cut = Mount();

        await GoAsync("quiet");

        Assert.That(Announcement(cut), Is.Empty);
        Assert.That(Member(Effects.Last(), "focus").ValueKind, Is.EqualTo(JsonValueKind.Object));
    }

    [Test]
    public async Task An_untitled_route_announces_nothing_and_leaves_the_title_alone()
    {
        var cut = Mount();

        await GoAsync("untitled");

        Assert.That(Announcement(cut), Is.Empty);
        Assert.That(Member(Effects.Last(), "title").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task A_custom_template_renders_the_announcement_instead_of_the_default_region()
    {
        var cut = Mount(parameters: p => p.Add(r => r.AnnounceTemplate, text => b => b.AddMarkupContent(0, $"<p id=\"mine\" aria-live=\"assertive\">{text}</p>")));

        await GoAsync("about");

        Assert.That(One(cut, "#mine").TextContent, Is.EqualTo("About"));
        Assert.That(All(cut, "[data-mod-router-announcer]"), Is.Empty);
    }

    [Test]
    public void The_default_region_is_a_polite_status_region_that_exists_before_anything_is_announced()
    {
        var cut = Mount();

        var region = One(cut, "[data-mod-router-announcer]");

        Assert.That(region.GetAttribute("role"), Is.EqualTo("status"));
        Assert.That(region.GetAttribute("aria-live"), Is.EqualTo("polite"));
        Assert.That(region.GetAttribute("style"), Does.Contain("overflow:hidden"));
    }

    // ---- configuration reaches the browser ---------------------------------------------------------------------

    [Test]
    public async Task The_configured_selector_container_and_timeout_are_sent()
    {
        Mount(o =>
        {
            o.Focus.Selector = "main h1";
            o.Focus.Timeout = TimeSpan.FromMilliseconds(300);
            o.Scroll.Container = "#scroller";
        });

        await GoAsync("about");

        var effects = Effects.Last();
        Assert.That(Text(Member(effects, "focus"), "selector"), Is.EqualTo("main h1"));
        Assert.That(Member(effects, "focus").GetProperty("timeoutMs").GetInt32(), Is.EqualTo(300));
        Assert.That(Text(Member(effects, "scroll"), "container"), Is.EqualTo("#scroller"));
        Assert.That(Calls("trackScroll").Select(call => call.GetString()), Is.All.EqualTo("#scroller"));
    }

    [Test]
    public async Task Scroll_tracking_is_asked_for_on_every_run_so_a_container_that_appears_late_is_still_tracked()
    {
        Mount(o => o.Scroll.Container = "#scroller");

        await GoAsync("about");
        await GoAsync("/");

        Assert.That(Calls("trackScroll"), Has.Count.EqualTo(Effects.Count).And.Count.EqualTo(3));
        Assert.That(Calls("trackScroll").Select(call => call.GetString()), Is.All.EqualTo("#scroller"));
    }

    [Test]
    public async Task Scroll_tracking_is_not_asked_for_when_restore_is_off()
    {
        Mount(o => o.Scroll.Restore = false);

        await GoAsync("about");

        Assert.That(Calls("trackScroll"), Is.Empty);
        Assert.That(Effects, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task Disabled_effects_are_not_sent_and_scroll_tracking_is_not_started()
    {
        Mount(o =>
        {
            o.Focus.Enabled = false;
            o.Scroll.Enabled = false;
        });

        await GoAsync("about");

        Assert.That(Calls("trackScroll"), Is.Empty);
        Assert.That(Member(Effects.Last(), "focus").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(Member(Effects.Last(), "scroll").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task A_route_can_pick_its_own_focus_target()
    {
        Mount(routes: map => map
            .Route("/", typeof(HomeProbe), name: "home", title: "Home")
            .Route("/custom", typeof(ShowProbe), name: "custom", title: "Custom").Focus("#custom-heading"));

        await GoAsync("custom");

        Assert.That(Text(Member(Effects.Last(), "focus"), "selector"), Is.EqualTo("#custom-heading"));
    }

    [Test]
    public async Task A_navigation_can_skip_focus_and_scroll_through_the_navigation_service()
    {
        Mount();
        var service = _ctx.Services.GetRequiredService<INavigationService>();

        service.NavigateTo("about", new ModNavigationOptions { Focus = false, Scroll = false });
        await _adapter.SettledAsync();

        var effects = Effects.Last();
        Assert.That(Text(effects, "title"), Is.EqualTo("About"));
        Assert.That(Member(effects, "focus").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(Member(effects, "scroll").ValueKind, Is.EqualTo(JsonValueKind.Null));
    }

    [Test]
    public async Task A_url_that_matches_nothing_sets_the_not_found_title()
    {
        Mount();

        await GoAsync("nope");

        Assert.That(Text(Effects.Last(), "title"), Is.EqualTo("Not found"));
    }

    // ---- resilience --------------------------------------------------------------------------------------------

    [Test]
    public async Task A_missing_javascript_module_costs_the_effects_but_never_a_navigation()
    {
        var context = new BunitContext();
        context.Services.AddModRouter(Routes);
        var adapter = new FakeNavigationAdapter("http://localhost/", "http://localhost/") { ExecuteNavigateTo = true };
        context.Services.AddSingleton<INavigationPlatformAdapter>(adapter);
        context.Services.AddSingleton(new GuardScript());

        var cut = context.Render<Components.ModRouter>(p => p.Add(r => r.DefaultLayout, typeof(MainLayoutProbe)));
        await adapter.NavigateAsync("about", intercepted: true);

        Assert.That(One(cut, "#show").TextContent, Is.EqualTo("show"));
        Assert.That(context.Services.GetRequiredService<RouteStateStore>().Current!.Path, Is.EqualTo("/about"));
        Assert.That(One(cut, "[data-mod-router-announcer]").TextContent, Is.EqualTo("About"), "the announcement is rendered by the component, not by the module");

        await context.DisposeAsync();
    }
}
