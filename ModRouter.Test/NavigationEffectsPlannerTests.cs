using ModRouter.Core.Compilation;
using ModRouter.Navigation;

namespace ModRouter.Test;

[TestFixture]
public sealed class NavigationEffectsPlannerTests
{
    static readonly CompiledRouteTable Table = RouteTestKit.Compile(map => map
        .Route("/", typeof(HomePage), name: "home", title: "Home")
        .Route("/quiet", typeof(OtherPage), name: "quiet", title: "Quiet").NoAnnounce()
        .Route("/custom", typeof(OtherPage), name: "custom", title: "Custom").Focus("#custom-heading")
        .Route("/customers", typeof(CustomersPage), name: "customers", title: "Customers", children: customers => customers
            .Route("{id:int}", typeof(CustomerDetails), p => $"Customer {p["id"]}", name: "customer").Focus("#customer-heading")));

    static RouteState State(string path, NavigationTrigger trigger, string? title = "T", string? fragment = null, ModNavigationOptions? options = null, NavigationTrigger? redirectOrigin = null)
        => new()
        {
            NavigationId = 1,
            AbsoluteUri = "http://localhost" + path,
            Path = path,
            Fragment = fragment,
            Chain = RouteTestKit.Match(Table, path),
            Trigger = trigger,
            Title = title,
            Options = options,
            RedirectOrigin = redirectOrigin,
        };

    static ModRouterOptions Options(Action<ModRouterOptions>? configure = null)
    {
        var options = new ModRouterOptions();
        configure?.Invoke(options);

        return options;
    }

    [TestCase(NavigationTrigger.Link)]
    [TestCase(NavigationTrigger.Programmatic)]
    [TestCase(NavigationTrigger.Redirect)]
    public void A_new_navigation_sets_the_title_announces_it_focuses_the_heading_and_scrolls_to_the_top(NavigationTrigger trigger)
    {
        var effects = NavigationEffectsPlanner.Plan(State("/", trigger, "Home"), Options());

        Assert.That(effects.Title, Is.EqualTo("Home"));
        Assert.That(effects.Announcement, Is.EqualTo("Home"));
        Assert.That(effects.Focus, Is.EqualTo(new FocusEffect("h1", 1000)));
        Assert.That(effects.Scroll, Is.EqualTo(new ScrollEffect(ScrollMode.Top, null, null, 1000)));
    }

    [Test]
    public void The_initial_load_sets_the_title_and_only_scrolls_to_a_fragment()
    {
        var plain = NavigationEffectsPlanner.Plan(State("/", NavigationTrigger.InitialLoad, "Home"), Options());
        var fragment = NavigationEffectsPlanner.Plan(State("/", NavigationTrigger.InitialLoad, "Home", "section"), Options());

        Assert.That(plain, Is.EqualTo(new NavigationEffects("Home", null, null, null)));
        Assert.That(fragment.Scroll, Is.EqualTo(new ScrollEffect(ScrollMode.Fragment, "section", null, 1000)));
        Assert.That(fragment.Focus, Is.Null);
        Assert.That(fragment.Announcement, Is.Null);
    }

    [TestCase(NavigationTrigger.HistoryRestore)]
    [TestCase(NavigationTrigger.BrowserBack)]
    [TestCase(NavigationTrigger.BrowserForward)]
    public void Back_and_forward_set_the_title_and_restore_scroll_but_never_announce_or_move_focus(NavigationTrigger trigger)
    {
        var effects = NavigationEffectsPlanner.Plan(State("/", trigger, "Home", "ignored"), Options());

        Assert.That(effects, Is.EqualTo(new NavigationEffects("Home", null, null, new ScrollEffect(ScrollMode.Restore, null, null, 1000))));
    }

    // ---- a redirect chain takes the row of the navigation that started it ----------------------------------------

    [Test]
    public void A_redirect_that_started_at_the_initial_load_sets_the_title_and_only_scrolls_to_a_fragment()
    {
        var plain = NavigationEffectsPlanner.Plan(State("/", NavigationTrigger.Redirect, "Home", redirectOrigin: NavigationTrigger.InitialLoad), Options());
        var fragment = NavigationEffectsPlanner.Plan(State("/", NavigationTrigger.Redirect, "Home", "section", redirectOrigin: NavigationTrigger.InitialLoad), Options());

        Assert.That(plain, Is.EqualTo(new NavigationEffects("Home", null, null, null)));
        Assert.That(fragment, Is.EqualTo(new NavigationEffects("Home", null, null, new ScrollEffect(ScrollMode.Fragment, "section", null, 1000))));
    }

    [TestCase(NavigationTrigger.HistoryRestore)]
    [TestCase(NavigationTrigger.BrowserBack)]
    [TestCase(NavigationTrigger.BrowserForward)]
    public void A_redirect_that_started_at_back_or_forward_sets_the_title_and_restores_scroll_but_never_announces_or_moves_focus(NavigationTrigger origin)
    {
        var effects = NavigationEffectsPlanner.Plan(State("/", NavigationTrigger.Redirect, "Home", "ignored", redirectOrigin: origin), Options());

        Assert.That(effects, Is.EqualTo(new NavigationEffects("Home", null, null, new ScrollEffect(ScrollMode.Restore, null, null, 1000))));
    }

    [TestCase(NavigationTrigger.Link)]
    [TestCase(NavigationTrigger.Programmatic)]
    [TestCase(NavigationTrigger.Reload)]
    [TestCase(null)]
    public void A_redirect_that_started_anywhere_else_is_a_new_navigation(NavigationTrigger? origin)
    {
        var effects = NavigationEffectsPlanner.Plan(State("/", NavigationTrigger.Redirect, "Home", redirectOrigin: origin), Options());

        Assert.That(effects, Is.EqualTo(new NavigationEffects("Home", "Home", new FocusEffect("h1", 1000), new ScrollEffect(ScrollMode.Top, null, null, 1000))));
    }

    [Test]
    public void The_origin_only_matters_for_a_redirect()
    {
        var effects = NavigationEffectsPlanner.Plan(State("/", NavigationTrigger.Link, "Home", redirectOrigin: NavigationTrigger.InitialLoad), Options());

        Assert.That(effects.Focus, Is.Not.Null);
        Assert.That(effects.Announcement, Is.EqualTo("Home"));
    }

    [Test]
    public void A_reload_does_nothing_at_all()
    {
        var effects = NavigationEffectsPlanner.Plan(State("/", NavigationTrigger.Reload, "Home"), Options());

        Assert.That(effects.IsEmpty, Is.True);
        Assert.That(effects, Is.EqualTo(NavigationEffects.None));
    }

    [Test]
    public void A_fragment_wins_over_the_top_for_a_new_navigation()
    {
        var effects = NavigationEffectsPlanner.Plan(State("/", NavigationTrigger.Link, "Home", "target"), Options());

        Assert.That(effects.Scroll, Is.EqualTo(new ScrollEffect(ScrollMode.Fragment, "target", null, 1000)));
    }

    [Test]
    public void An_untitled_state_sets_no_title_and_announces_nothing()
    {
        var effects = NavigationEffectsPlanner.Plan(State("/", NavigationTrigger.Link, title: null), Options());

        Assert.That(effects.Title, Is.Null);
        Assert.That(effects.Announcement, Is.Null);
        Assert.That(effects.Focus, Is.Not.Null);
    }

    [Test]
    public void The_global_switches_turn_each_effect_off_independently()
    {
        var state = State("/", NavigationTrigger.Link, "Home");

        Assert.That(NavigationEffectsPlanner.Plan(state, Options(o => o.Announce.Enabled = false)).Announcement, Is.Null);
        Assert.That(NavigationEffectsPlanner.Plan(state, Options(o => o.Announce.Enabled = false)).Focus, Is.Not.Null);
        Assert.That(NavigationEffectsPlanner.Plan(state, Options(o => o.Focus.Enabled = false)).Focus, Is.Null);
        Assert.That(NavigationEffectsPlanner.Plan(state, Options(o => o.Focus.Enabled = false)).Scroll, Is.Not.Null);
        Assert.That(NavigationEffectsPlanner.Plan(state, Options(o => o.Scroll.Enabled = false)).Scroll, Is.Null);
        Assert.That(NavigationEffectsPlanner.Plan(state, Options(o => o.Scroll.Enabled = false)).Title, Is.EqualTo("Home"));
    }

    [Test]
    public void Restore_has_its_own_switch_and_scroll_enabled_gates_it_too()
    {
        var state = State("/", NavigationTrigger.HistoryRestore, "Home");

        Assert.That(NavigationEffectsPlanner.Plan(state, Options(o => o.Scroll.Restore = false)).Scroll, Is.Null);
        Assert.That(NavigationEffectsPlanner.Plan(state, Options(o => o.Scroll.Enabled = false)).Scroll, Is.Null);
    }

    [Test]
    public void The_configured_container_selector_and_timeout_flow_into_the_effects()
    {
        var options = Options(o =>
        {
            o.Scroll.Container = "#scroller";
            o.Focus.Selector = "main h1";
            o.Focus.Timeout = TimeSpan.FromMilliseconds(250);
        });

        var effects = NavigationEffectsPlanner.Plan(State("/", NavigationTrigger.Link, "Home"), options);

        Assert.That(effects.Focus, Is.EqualTo(new FocusEffect("main h1", 250)));
        Assert.That(effects.Scroll, Is.EqualTo(new ScrollEffect(ScrollMode.Top, null, "#scroller", 250)));
    }

    [Test]
    public void A_route_can_switch_its_announcement_off()
    {
        var effects = NavigationEffectsPlanner.Plan(State("/quiet", NavigationTrigger.Link, "Quiet"), Options());

        Assert.That(effects.Announcement, Is.Null);
        Assert.That(effects.Title, Is.EqualTo("Quiet"));
        Assert.That(effects.Focus, Is.Not.Null);
    }

    [Test]
    public void A_route_can_choose_its_own_focus_target_and_the_deepest_level_wins()
    {
        Assert.That(NavigationEffectsPlanner.Plan(State("/custom", NavigationTrigger.Link, "Custom"), Options()).Focus!.Selector, Is.EqualTo("#custom-heading"));
        Assert.That(NavigationEffectsPlanner.Plan(State("/customers/7", NavigationTrigger.Link, "Customer 7"), Options()).Focus!.Selector, Is.EqualTo("#customer-heading"));
        Assert.That(NavigationEffectsPlanner.Plan(State("/customers", NavigationTrigger.Link, "Customers"), Options(o => o.Focus.Selector = "h2")).Focus!.Selector, Is.EqualTo("h2"));
    }

    [Test]
    public void A_navigation_can_opt_out_of_each_effect()
    {
        var state = State("/", NavigationTrigger.Link, "Home", options: new ModNavigationOptions { Focus = false, Scroll = false, Announce = false });

        var effects = NavigationEffectsPlanner.Plan(state, Options());

        Assert.That(effects, Is.EqualTo(new NavigationEffects("Home", null, null, null)));
    }

    [Test]
    public void A_navigation_option_only_turns_things_off_it_cannot_override_a_global_switch()
    {
        var state = State("/", NavigationTrigger.Link, "Home", options: new ModNavigationOptions());

        var effects = NavigationEffectsPlanner.Plan(state, Options(o => o.Focus.Enabled = false));

        Assert.That(effects.Focus, Is.Null);
    }

    [Test]
    public void A_negative_timeout_is_treated_as_zero()
    {
        var effects = NavigationEffectsPlanner.Plan(State("/", NavigationTrigger.Link, "Home"), Options(o => o.Focus.Timeout = TimeSpan.FromSeconds(-1)));

        Assert.That(effects.Focus!.TimeoutMilliseconds, Is.Zero);
    }

    [TestCase(int.MaxValue + 1d)]
    [TestCase(double.MaxValue)]
    public void A_timeout_beyond_int_range_is_clamped_to_the_largest_wait(double milliseconds)
    {
        var timeout = milliseconds >= TimeSpan.MaxValue.TotalMilliseconds ? TimeSpan.MaxValue : TimeSpan.FromMilliseconds(milliseconds);
        var effects = NavigationEffectsPlanner.Plan(State("/", NavigationTrigger.Link, "Home"), Options(o => o.Focus.Timeout = timeout));

        Assert.That(effects.Focus!.TimeoutMilliseconds, Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void A_state_without_a_chain_still_plans_the_effects()
    {
        var state = new RouteState
        {
            NavigationId = 1,
            AbsoluteUri = "http://localhost/nope",
            Path = "/nope",
            Failure = RouteFailure.NotFound,
            Trigger = NavigationTrigger.Link,
            Title = "Not found",
        };

        var effects = NavigationEffectsPlanner.Plan(state, Options());

        Assert.That(effects.Title, Is.EqualTo("Not found"));
        Assert.That(effects.Announcement, Is.EqualTo("Not found"));
        Assert.That(effects.Focus!.Selector, Is.EqualTo("h1"));
    }

    [Test]
    public void Null_arguments_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => NavigationEffectsPlanner.Plan(null!, Options()));
        Assert.Throws<ArgumentNullException>(() => NavigationEffectsPlanner.Plan(State("/", NavigationTrigger.Link), null!));
    }
}
