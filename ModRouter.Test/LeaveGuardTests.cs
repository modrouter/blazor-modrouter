using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using ModRouter.Components;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Guards;
using ModRouter.Core.Loading;
using ModRouter.Discovery;
using ModRouter.Navigation;

namespace ModRouter.Test;

/// <summary>
/// A leave guard asks before a level on screen is left: the "unsaved changes" check. It runs before the redirect rules and
/// the enter guards, deepest level first, and not for a parameter change on the same route.
/// </summary>
[TestFixture]
public sealed class LeaveGuardTests
{
    /// <summary>The scoped state of the page's own form, as an app would keep it.</summary>
    public sealed class FormState
    {
        public bool IsDirty { get; set; }

        public List<string> Asked { get; } = [];

        public List<string> EnterGuards { get; } = [];
    }

    public sealed class UnsavedChangesGuard(FormState form) : IRouteLeaveGuard
    {
        public ValueTask<RouteLeaveResult> CanDeactivateAsync(RouteLeaveContext context)
        {
            form.Asked.Add($"{context.Level.RouteName}@{context.Depth}");

            return ValueTask.FromResult(form.IsDirty ? RouteLeaveResult.Cancel() : RouteLeaveResult.Allow());
        }
    }

    public sealed class ParentGuard(FormState form) : IRouteLeaveGuard
    {
        public ValueTask<RouteLeaveResult> CanDeactivateAsync(RouteLeaveContext context)
        {
            form.Asked.Add("parent");

            return ValueTask.FromResult(RouteLeaveResult.Allow());
        }
    }

    public sealed class ChildGuard(FormState form) : IRouteLeaveGuard
    {
        public ValueTask<RouteLeaveResult> CanDeactivateAsync(RouteLeaveContext context)
        {
            form.Asked.Add("child");

            return ValueTask.FromResult(RouteLeaveResult.Allow());
        }
    }

    public sealed class RedirectingLeaveGuard : IRouteLeaveGuard
    {
        public ValueTask<RouteLeaveResult> CanDeactivateAsync(RouteLeaveContext context) => ValueTask.FromResult(RouteLeaveResult.Redirect("about"));
    }

    public sealed class ThrowingLeaveGuard : IRouteLeaveGuard
    {
        public ValueTask<RouteLeaveResult> CanDeactivateAsync(RouteLeaveContext context) => throw new InvalidOperationException("boom");
    }

    public sealed class WaitingLeaveGuard : IRouteLeaveGuard
    {
        public static bool SawCancellation { get; set; }

        public async ValueTask<RouteLeaveResult> CanDeactivateAsync(RouteLeaveContext context)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, context.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                SawCancellation = true;

                throw;
            }

            return RouteLeaveResult.Allow();
        }
    }

    public sealed class EnterGuardThatRecords(FormState form) : IRouteGuard
    {
        public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context)
        {
            form.EnterGuards.Add(context.Level.RouteName ?? "?");

            return ValueTask.FromResult(RouteGuardResult.Allow());
        }
    }

    public sealed class ThrowingLoader : IRouteLoader
    {
        public ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context) => throw new InvalidOperationException("boom");
    }

    static void Routes(RouteMap map) => map
        .Route("/", typeof(ShowProbe), name: "home")
        .Route("/about", typeof(ShowProbe), name: "about").Guard<EnterGuardThatRecords>()
        .Route("/form", typeof(ShowProbe), name: "form").LeaveGuard<UnsavedChangesGuard>()
        .Route("/items/{id:int}", typeof(ShowProbe), name: "item").LeaveGuard<UnsavedChangesGuard>()
        .Route("/section", typeof(ShowProbe), name: "section", children: section => section
            .Route("page", typeof(ShowProbe), name: "page").LeaveGuard<ChildGuard>())
        .LeaveGuard<ParentGuard>()
        .Route("/hop", typeof(ShowProbe), name: "hop").LeaveGuard<RedirectingLeaveGuard>()
        .Route("/thrower", typeof(ShowProbe), name: "thrower").LeaveGuard<ThrowingLeaveGuard>()
        .Route("/waiter", typeof(ShowProbe), name: "waiter").LeaveGuard<WaitingLeaveGuard>()
        .Route("/broken", typeof(ShowProbe), name: "broken").Loader<ThrowingLoader>().LeaveGuard<UnsavedChangesGuard>()
        .Redirect("/old", "about");

    BunitContext _ctx = default!;
    RouterHarness _router = default!;

    INavigationService Navigation => _ctx.Services.GetRequiredService<INavigationService>();

    FormState Form => _ctx.Services.GetRequiredService<FormState>();

    [SetUp]
    public async Task SetUp()
    {
        WaitingLeaveGuard.SawCancellation = false;
        _ctx = new BunitContext();
        _ctx.Services.AddScoped<FormState>();
        _router = await RouterHarness.StartAsync(_ctx, Routes, "http://localhost/");
    }

    [TearDown]
    public async Task TearDown()
    {
        await _router.DisposeAsync();
        await _ctx.DisposeAsync();
    }

    [Test]
    public async Task A_clean_page_may_be_left()
    {
        await Navigation.NavigateToAsync("form");
        Form.Asked.Clear();

        var result = await Navigation.NavigateToAsync("about");

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Succeeded));
        Assert.That(Form.Asked, Is.EqualTo(new[] { "form@0" }));
        Assert.That(Navigation.Current!.Path, Is.EqualTo("/about"));
    }

    [Test]
    public async Task A_dirty_page_keeps_the_old_page_and_url()
    {
        await Navigation.NavigateToAsync("form");
        Form.IsDirty = true;

        var result = await Navigation.NavigateToAsync("about");

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Cancelled));
        Assert.That(Navigation.Current!.Path, Is.EqualTo("/form"));
    }

    [Test]
    public async Task A_target_that_matches_nothing_leaves_the_page_too()
    {
        await Navigation.NavigateToAsync("form");
        Form.IsDirty = true;

        var result = await Navigation.NavigateToAsync("nowhere");

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Cancelled));
        Assert.That(Navigation.Current!.Path, Is.EqualTo("/form"));
    }

    [Test]
    public async Task A_parameter_change_on_the_same_route_does_not_leave_it()
    {
        await Navigation.NavigateToAsync("items/1");
        Form.IsDirty = true;
        Form.Asked.Clear();

        var result = await Navigation.NavigateToAsync("items/2");

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Succeeded));
        Assert.That(Form.Asked, Is.Empty);
        Assert.That(Navigation.Current!.Path, Is.EqualTo("/items/2"));
    }

    [Test]
    public async Task Opening_a_child_does_not_leave_the_parent_and_leaving_it_asks_the_deepest_level_first()
    {
        await Navigation.NavigateToAsync("section");
        Form.Asked.Clear();

        await Navigation.NavigateToAsync("section/page");
        Assert.That(Form.Asked, Is.Empty, "the parent stays while its child opens");

        await Navigation.NavigateToAsync("about");

        Assert.That(Form.Asked, Is.EqualTo(new[] { "child", "parent" }));
    }

    [Test]
    public async Task Leave_guards_run_before_enter_guards()
    {
        await Navigation.NavigateToAsync("form");
        Form.IsDirty = true;

        await Navigation.NavigateToAsync("about");

        Assert.That(Form.EnterGuards, Is.Empty, "the dirty page said no before the target's own guard was asked");

        Form.IsDirty = false;
        await Navigation.NavigateToAsync("about");

        Assert.That(Form.EnterGuards, Is.EqualTo(new[] { "about" }));
    }

    [Test]
    public async Task A_leave_guard_can_redirect()
    {
        await Navigation.NavigateToAsync("hop");

        var result = await Navigation.NavigateToAsync("items/1");
        await _router.Adapter.SettledAsync();

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Redirected));
        Assert.That(Navigation.Current!.Path, Is.EqualTo("/about"));
    }

    [Test]
    public async Task The_guard_runs_before_a_redirect_rule_sends_the_target_elsewhere()
    {
        await Navigation.NavigateToAsync("form");
        Form.IsDirty = true;

        var result = await Navigation.NavigateToAsync("old");

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Cancelled));
        Assert.That(Navigation.Current!.Path, Is.EqualTo("/form"));

        Form.IsDirty = false;
        Form.Asked.Clear();

        await Navigation.NavigateToAsync("old");
        await _router.Adapter.SettledAsync();

        Assert.That(Form.Asked, Is.EqualTo(new[] { "form@0" }), "asked once, for the first hop only");
        Assert.That(Navigation.Current!.Path, Is.EqualTo("/about"));
    }

    [Test]
    public async Task A_reload_leaves_nothing()
    {
        await Navigation.NavigateToAsync("form");
        Form.IsDirty = true;
        Form.Asked.Clear();

        await Navigation.ReloadAsync();

        Assert.That(Form.Asked, Is.Empty);
        Assert.That(Navigation.Current!.Path, Is.EqualTo("/form"));
    }

    [Test]
    public async Task A_guard_that_throws_fails_the_navigation_and_the_page_stays()
    {
        await Navigation.NavigateToAsync("thrower");

        var result = await Navigation.NavigateToAsync("about");

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Failed));
        Assert.That(result.Exception, Is.TypeOf<InvalidOperationException>());
        Assert.That(Navigation.Current!.Path, Is.EqualTo("/thrower"));
    }

    [Test]
    public async Task A_guard_that_waits_holds_the_navigation_and_a_newer_one_cancels_its_token()
    {
        await Navigation.NavigateToAsync("waiter");

        var held = Navigation.NavigateToAsync("about");
        Assert.That(held.IsCompleted, Is.False, "the old page stays while the guard waits");
        Assert.That(Navigation.IsNavigating, Is.True);

        var newer = Navigation.NavigateToAsync("items/9");

        Assert.That((await held).Status, Is.EqualTo(NavigationStatus.Superseded));
        Assert.That(WaitingLeaveGuard.SawCancellation, Is.True);
        Assert.That(newer.IsCompleted, Is.False, "the newer navigation leaves the same page, so it waits on the guard too");

    }

    [Test]
    public async Task A_level_that_showed_an_error_has_nothing_to_protect()
    {
        var failed = await Navigation.NavigateToAsync("broken");
        Assert.That(failed.Status, Is.EqualTo(NavigationStatus.Failed));
        Form.IsDirty = true;
        Form.Asked.Clear();

        var result = await Navigation.NavigateToAsync("about");

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Succeeded));
        Assert.That(Form.Asked, Is.Empty);
    }

    // ---- declaration ------------------------------------------------------------------------------------------------

    [Route("/attributed")]
    [RouteLeaveGuard<UnsavedChangesGuard>]
    [RouteLeaveGuard<ParentGuard>]
    public sealed class AttributedPage : ComponentBase;

    [Route("/bad-attributed")]
    [RouteLeaveGuard(typeof(string))]
    public sealed class BadlyAttributedPage : ComponentBase;

    [Test]
    public void The_attribute_declares_the_same_guards_as_the_fluent_call_in_order()
    {
        var map = RouteMap.Create();
        map.AddPages(typeof(LeaveGuardTests).Assembly, type => type == typeof(AttributedPage));

        var guards = map.Build().Single().Metadata.GetAll<RouteLeaveGuardMetadata>().Select(m => m.GuardType).ToList();

        Assert.That(guards, Is.EqualTo(new[] { typeof(UnsavedChangesGuard), typeof(ParentGuard) }));
    }

    [Test]
    public void An_attribute_naming_a_type_that_is_not_a_leave_guard_is_a_discovery_error()
    {
        var error = Assert.Throws<RouteCompilationException>(() => RouteMap.Create().AddPages(typeof(LeaveGuardTests).Assembly, type => type == typeof(BadlyAttributedPage)))!;

        Assert.That(error.Message, Does.Contain("RT0022").And.Contain("IRouteLeaveGuard"));
    }

    [Test]
    public async Task A_markup_entry_declares_a_leave_guard_too()
    {
        await using var context = new BunitContext();
        context.Services.AddScoped<FormState>();
        context.Services.AddModRouter();
        context.Services.AddSingleton<INavigationPlatformAdapter>(new FakeNavigationAdapter("http://localhost/", "http://localhost/form") { ExecuteNavigateTo = true });

        context.Render(builder =>
        {
            builder.OpenComponent<Components.ModRouter>(0);
            builder.AddComponentParameter(1, nameof(Components.ModRouter.Routes), (RenderFragment)(routes =>
            {
                routes.OpenComponent<ModRouteEntry>(0);
                routes.AddComponentParameter(1, nameof(ModRouteEntry.Path), "/form");
                routes.AddComponentParameter(2, nameof(ModRouteEntry.Component), typeof(ShowProbe));
                routes.AddComponentParameter(3, nameof(ModRouteEntry.LeaveGuard), typeof(UnsavedChangesGuard));
                routes.CloseComponent();
                routes.OpenComponent<ModRouteEntry>(4);
                routes.AddComponentParameter(5, nameof(ModRouteEntry.Path), "/about");
                routes.AddComponentParameter(6, nameof(ModRouteEntry.Component), typeof(ShowProbe));
                routes.CloseComponent();
            }));
            builder.CloseComponent();
        });

        var navigation = context.Services.GetRequiredService<INavigationService>();
        context.Services.GetRequiredService<FormState>().IsDirty = true;

        var result = await navigation.NavigateToAsync("about");

        Assert.That(result.Status, Is.EqualTo(NavigationStatus.Cancelled));
    }
}
