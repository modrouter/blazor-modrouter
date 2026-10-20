using System.Security.Claims;
using Bunit;
using ModRouter.Authorization;
using ModRouter.Components;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Guards;
using ModRouter.Core.Loading;
using ModRouter.Navigation;
using ModRouter.Test.AuthorizationPages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModRouter.Test;

/// <summary>
/// When the application's <c>AuthenticationStateProvider</c> raises <c>AuthenticationStateChanged</c>, the coordinator
/// re-runs only the <see cref="IAuthorizationRouteGuard"/>s of the committed chain, parent first, and reloads the URL
/// only when a level's outcome changed; deferred during an in-flight navigation, serialized with one follow-up,
/// unsubscribed on detach, and never run for an ordinary guard's denial.
/// </summary>
[TestFixture]
public sealed class AuthorizationRecheckTests
{
    /// <summary>The fake session the test guards read; the provider mirrors it as a principal.</summary>
    sealed class Session
    {
        public bool SignedIn { get; set; } = true;

        public bool Admin { get; set; } = true;
    }

    /// <summary>An authorization guard (re-run on authentication change) that admits administrators; counts its runs and can be held.</summary>
    sealed class SessionAdminGuard(Session session) : IAuthorizationRouteGuard
    {
        public static int Runs { get; set; }

        public static TaskCompletionSource? Gate { get; set; }

        public async ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context)
        {
            Runs++;

            if (Gate is { } gate)
            {
                await gate.Task.WaitAsync(context.CancellationToken);
            }

            return session.Admin ? RouteGuardResult.Allow() : RouteGuardResult.NotAuthorized("Administrators only.");
        }
    }

    /// <summary>An ordinary guard (never re-run) whose verdict the test scripts.</summary>
    sealed class OrdinaryGuard : IRouteGuard
    {
        public static int Runs { get; set; }

        public static Func<RouteGuardResult> Verdict { get; set; } = RouteGuardResult.Allow;

        public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context)
        {
            Runs++;

            return ValueTask.FromResult(Verdict());
        }
    }

    sealed class CountingLoader : IRouteLoader
    {
        public static int Runs { get; set; }

        public static TaskCompletionSource? Gate { get; set; }

        public async ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context)
        {
            Runs++;

            if (Gate is { } gate)
            {
                await gate.Task.WaitAsync(context.CancellationToken);
            }

            return RouteLoadResult.Create("loaded");
        }
    }

    /// <summary>A stage that redirects every navigation whose URI ends with <c>uriEnd</c> (a redirect a stage makes, as a guard's would be).</summary>
    sealed class RedirectingStage(string uriEnd, string target) : INavigationStage
    {
        public ValueTask<StageResult> RunAsync(RouteNavigationContext context)
            => ValueTask.FromResult(context.To.EndsWith(uriEnd, StringComparison.Ordinal) ? StageResult.Redirect(target) : StageResult.Continue);
    }

    sealed class SecurePage : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<p id=\"secure\">Secure</p>");
    }

    sealed class LoginPage : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<p id=\"login\">Sign in</p>");
    }

    /// <summary>The sites' RedirectToLogin: rendered by the not-authorized content for an anonymous user, it navigates to the login page.</summary>
    sealed class RedirectToLoginProbe : ComponentBase
    {
        public static int Renders { get; set; }

        [Inject]
        INavigationService Navigation { get; set; } = default!;

        protected override void OnInitialized()
        {
            Renders++;
            Navigation.NavigateTo("login", replaceHistory: true);
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<p id=\"redirecting\">Redirecting to login...</p>");
    }

    Session _session = default!;
    FakeAuthenticationStateProvider _provider = default!;
    ServiceProvider _services = default!;
    FakeNavigationAdapter _adapter = default!;
    RouteStateStore _store = default!;
    NavigationCoordinator _coordinator = default!;
    IDisposable? _lease;
    readonly List<RouteState> _commits = [];

    static void Routes(RouteMap map) => map
        .Route("/", typeof(HomePage), name: "home")
        .Route("/login", typeof(LoginPage), name: "login")
        .Route("/secure", typeof(SecurePage), name: "secure").Guard<OrdinaryGuard>().Guard<SessionAdminGuard>().Loader<CountingLoader>()
        .Route("/attributes", typeof(P03_RolesA), name: "attributes").Loader<CountingLoader>()
        .Route("/parent", typeof(P02_Authorize), name: "parent", children: parent => parent
            .Route("child", typeof(SecurePage), name: "child").Guard<SessionAdminGuard>());

    [SetUp]
    public void SetUp()
    {
        SessionAdminGuard.Runs = 0;
        SessionAdminGuard.Gate = null;
        OrdinaryGuard.Runs = 0;
        OrdinaryGuard.Verdict = RouteGuardResult.Allow;
        CountingLoader.Runs = 0;
        CountingLoader.Gate = null;
        RedirectToLoginProbe.Renders = 0;
        _commits.Clear();
        _session = new Session();
        _provider = new FakeAuthenticationStateProvider(Users.WithRoles("A"));
    }

    [TearDown]
    public void TearDown()
    {
        SessionAdminGuard.Gate?.TrySetResult();
        CountingLoader.Gate?.TrySetResult();
        _lease?.Dispose();
        _services?.Dispose();
    }

    /// <summary>
    /// A provider that raises <c>AuthenticationStateChanged</c> synchronously on every read, as some token-refreshing
    /// providers do; it stops notifying after <see cref="Cap"/> reads so that a feedback loop fails the test instead of hanging it.
    /// </summary>
    sealed class NotifyOnReadProvider(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public const int Cap = 50;

        public ClaimsPrincipal User { get; set; } = user;

        public int Reads { get; private set; }

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            Reads++;
            var state = Task.FromResult(new AuthenticationState(User));

            if (Reads < Cap)
            {
                NotifyAuthenticationStateChanged(state);
            }

            return state;
        }

        /// <summary>An external change (a sign-in elsewhere): raises the event without a read.</summary>
        public void Raise() => NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(User)));
    }

    async Task StartAsync(string uri, bool withProvider = true, AuthenticationStateProvider? provider = null, INavigationStage? stage = null)
    {
        var services = new ServiceCollection().AddSingleton(_session);

        if (withProvider)
        {
            services.AddTestAuthorization(provider ?? _provider);
        }

        _services = services.BuildServiceProvider();
        var map = RouteMap.Create();
        Routes(map);
        var table = RouteCompiler.Compile(AuthorizationRouteContributor.Apply(map.Build()));
        _adapter = new FakeNavigationAdapter("http://localhost/", uri) { ExecuteNavigateTo = true };
        _store = new RouteStateStore();
        _store.Changed += _commits.Add;
        _coordinator = new NavigationCoordinator(_adapter, _store, RouteTestKit.Frozen(table), new RouteComponentBinder(), _services, new ModRouterOptions(), stage is null ? [] : [stage], NullLoggerFactory.Instance);
        _lease = _coordinator.Attach();
        await _coordinator.NavigateInitialAsync();
    }

    /// <summary>Raises the event and waits for the re-check it starts (and any reload or deferred check after it).</summary>
    async Task ChangeAuthenticationAsync()
    {
        _provider.Notify();
        await _coordinator.AuthorizationRecheck;
        await _adapter.SettledAsync();
        await _coordinator.AuthorizationRecheck;
    }

    /// <summary>Waits until no re-check, reload or deferred check is left running.</summary>
    async Task SettleRechecksAsync()
    {
        Task seen;

        do
        {
            seen = _coordinator.AuthorizationRecheck;
            await seen;
            await _adapter.SettledAsync();
        }
        while (!ReferenceEquals(seen, _coordinator.AuthorizationRecheck));
    }

    int Reloads => _commits.Count(c => c.Trigger == NavigationTrigger.Reload);

    [Test]
    public async Task A_provider_that_notifies_on_every_read_does_not_feed_the_recheck_back_into_itself()
    {
        var provider = new NotifyOnReadProvider(Users.WithRoles("A"));
        await StartAsync("http://localhost/attributes", provider: provider);
        await SettleRechecksAsync();

        Assert.That(provider.Reads, Is.EqualTo(2), "the navigation's guard read, then one follow-up for the event that read raised");
        Assert.That(_commits, Has.Count.EqualTo(1));

        provider.Raise();
        await SettleRechecksAsync();

        Assert.That(provider.Reads, Is.EqualTo(4), "an external event: its check, then one follow-up for the event the check's read raised");
        Assert.That(Reloads, Is.Zero);

        provider.User = Users.WithRoles("B");
        provider.Raise();
        await SettleRechecksAsync();

        Assert.That(provider.Reads, Is.EqualTo(7), "the check, the reload's guard, one follow-up");
        Assert.That(Reloads, Is.EqualTo(1));
        Assert.That(_store.Current!.Error!.Kind, Is.EqualTo(RouteErrorKind.NotAuthorized));
    }

    [Test]
    public async Task Signing_out_on_a_protected_page_reloads_into_the_not_authorized_outcome()
    {
        await StartAsync("http://localhost/secure");
        Assert.That(_store.Current!.Error, Is.Null);
        Assert.That((SessionAdminGuard.Runs, OrdinaryGuard.Runs, CountingLoader.Runs), Is.EqualTo((1, 1, 1)));

        _session.Admin = false;
        await ChangeAuthenticationAsync();

        Assert.That(_store.Current!.Error!.Kind, Is.EqualTo(RouteErrorKind.NotAuthorized));
        Assert.That(_store.Current.Error.Level, Is.Zero);
        Assert.That(_store.Current.Error.GuardType, Is.EqualTo(typeof(SessionAdminGuard)));
        Assert.That(_store.Current.Trigger, Is.EqualTo(NavigationTrigger.Reload));
        Assert.That(Reloads, Is.EqualTo(1));
        Assert.That(SessionAdminGuard.Runs, Is.EqualTo(3), "once by the re-check, once by the reload's guard stage");
        Assert.That(OrdinaryGuard.Runs, Is.EqualTo(2), "the ordinary guard runs in the reload only, never in the re-check");
        Assert.That(CountingLoader.Runs, Is.EqualTo(1), "the reload stops at the guard; no loader runs");
    }

    [Test]
    public async Task Signing_in_on_a_not_authorized_page_shows_the_page_with_its_data()
    {
        _session.Admin = false;
        await StartAsync("http://localhost/secure");
        Assert.That(_store.Current!.Error!.Kind, Is.EqualTo(RouteErrorKind.NotAuthorized));
        Assert.That(CountingLoader.Runs, Is.Zero);

        _session.Admin = true;
        await ChangeAuthenticationAsync();

        Assert.That(_store.Current!.Error, Is.Null);
        Assert.That(_store.Current.Chain!.Leaf.RouteName, Is.EqualTo("secure"));
        Assert.That(_store.Current.LevelData[0]!.Data, Is.EqualTo("loaded"));
        Assert.That(Reloads, Is.EqualTo(1));
    }

    [Test]
    public async Task Events_without_an_outcome_change_reload_nothing_and_run_only_the_authorization_guard()
    {
        await StartAsync("http://localhost/secure");

        await ChangeAuthenticationAsync();
        await ChangeAuthenticationAsync();

        Assert.That(Reloads, Is.Zero, "a token refresh must not reload pages");
        Assert.That(SessionAdminGuard.Runs, Is.EqualTo(3), "the authorization guard ran once per event");
        Assert.That(OrdinaryGuard.Runs, Is.EqualTo(1));
        Assert.That(CountingLoader.Runs, Is.EqualTo(1));
        Assert.That(_commits, Has.Count.EqualTo(1), "the state was not re-committed");
    }

    [Test]
    public async Task A_page_without_authorization_guards_is_never_touched()
    {
        await StartAsync("http://localhost/");

        await ChangeAuthenticationAsync();

        Assert.That(_commits, Has.Count.EqualTo(1));
        Assert.That(SessionAdminGuard.Runs, Is.Zero);
        Assert.That(OrdinaryGuard.Runs, Is.Zero);
    }

    [Test]
    public async Task Without_a_registered_provider_the_feature_is_off()
    {
        await StartAsync("http://localhost/secure", withProvider: false);

        _session.Admin = false;
        _provider.Notify();
        await _coordinator.AuthorizationRecheck;

        Assert.That(_commits, Has.Count.EqualTo(1));
        Assert.That(SessionAdminGuard.Runs, Is.EqualTo(1));
    }

    [Test]
    public async Task The_real_AuthorizeAttributeGuard_is_rechecked_like_any_authorization_guard()
    {
        await StartAsync("http://localhost/attributes");
        Assert.That(_store.Current!.Error, Is.Null, "[Authorize(Roles = A)] admits the user");

        _provider.User = Users.WithRoles("B");
        await ChangeAuthenticationAsync();

        Assert.That(_store.Current!.Error!.Kind, Is.EqualTo(RouteErrorKind.NotAuthorized));
        Assert.That(_store.Current.Error.GuardType, Is.EqualTo(typeof(AuthorizeAttributeGuard)));
        Assert.That(Reloads, Is.EqualTo(1));

        _provider.User = Users.WithRoles("A");
        await ChangeAuthenticationAsync();

        Assert.That(_store.Current!.Error, Is.Null);
        Assert.That(Reloads, Is.EqualTo(2));
    }

    [Test]
    public async Task A_nested_chain_is_rechecked_parent_first_and_stops_at_the_first_denial()
    {
        await StartAsync("http://localhost/parent/child");
        Assert.That(_store.Current!.Error, Is.Null);
        Assert.That(SessionAdminGuard.Runs, Is.EqualTo(1));

        _provider.User = Users.Anonymous;
        await ChangeAuthenticationAsync();

        Assert.That(_store.Current!.Error!.Level, Is.Zero, "the parent's [Authorize] denies the anonymous user");
        Assert.That(SessionAdminGuard.Runs, Is.EqualTo(1), "the child's guard was not run: the parent denied first, in the re-check and in the reload");
    }

    [Test]
    public async Task An_event_during_an_in_flight_navigation_is_deferred_until_that_navigation_commits()
    {
        await StartAsync("http://localhost/");
        CountingLoader.Gate = new TaskCompletionSource();
        var navigation = _adapter.NavigateAsync("secure", intercepted: true);
        await ComponentTestKit.EventuallyAsync(() => CountingLoader.Runs, Is.EqualTo(1));
        Assert.That(_store.IsNavigating, Is.True);

        _session.Admin = false;
        _provider.Notify();
        await _coordinator.AuthorizationRecheck;

        Assert.That(SessionAdminGuard.Runs, Is.EqualTo(1), "deferred: the in-flight navigation's guards already ran, and a reload now would supersede it");
        Assert.That(_commits, Has.Count.EqualTo(1));

        CountingLoader.Gate.SetResult();
        Assert.That(await navigation, Is.True);
        await _coordinator.AuthorizationRecheck;
        await _adapter.SettledAsync();
        await _coordinator.AuthorizationRecheck;

        // The navigation ran its guard while the user was an administrator and committed the page; the deferred re-check
        // then saw the change and reloaded into the not-authorized outcome at the new URL.
        Assert.That(_commits.Select(c => (c.Path, c.Trigger)), Is.EqualTo(new[] { ("/", NavigationTrigger.InitialLoad), ("/secure", NavigationTrigger.Link), ("/secure", NavigationTrigger.Reload) }));
        Assert.That(_store.Current!.Error?.Kind, Is.EqualTo(RouteErrorKind.NotAuthorized));
    }

    /// <summary>Installs a hook (the compatibility Router's OnNavigateAsync) that holds every navigation until <paramref name="gate"/> completes.</summary>
    void HoldNavigationsIn(TaskCompletionSource gate) => _coordinator.OnNavigate = (_, token) => gate.Task.WaitAsync(token);

    [Test]
    public async Task An_event_while_a_navigation_waits_on_its_hook_from_a_page_without_a_chain_is_deferred_to_its_commit()
    {
        await StartAsync("http://localhost/nowhere");
        Assert.That(_store.Current!.Chain, Is.Null, "not found: the committed state has no chain");
        var hook = new TaskCompletionSource();
        HoldNavigationsIn(hook);

        Assert.That(await _adapter.NavigateAsync("secure", intercepted: true), Is.True);
        Assert.That(SessionAdminGuard.Runs, Is.EqualTo(1), "the guard allowed the administrator");

        _session.Admin = false;
        _provider.Notify();
        await _coordinator.AuthorizationRecheck;

        hook.SetResult();
        await ComponentTestKit.EventuallyAsync(() => _commits.Count(c => c.Path == "/secure"), Is.GreaterThanOrEqualTo(1));
        await SettleRechecksAsync();

        Assert.That(_commits.Select(c => (c.Path, c.Trigger)), Is.EqualTo(new[] { ("/nowhere", NavigationTrigger.InitialLoad), ("/secure", NavigationTrigger.Link), ("/secure", NavigationTrigger.Reload) }));
        Assert.That(_store.Current!.Error?.Kind, Is.EqualTo(RouteErrorKind.NotAuthorized), "the sign-out during the hook is not lost");
    }

    [Test]
    public async Task Going_back_to_the_committed_page_while_a_hooked_navigation_waits_runs_the_deferred_check()
    {
        await StartAsync("http://localhost/secure");
        HoldNavigationsIn(new TaskCompletionSource());
        Assert.That(await _adapter.NavigateAsync("/", intercepted: true), Is.True);

        _session.Admin = false;
        _provider.Notify();
        await _coordinator.AuthorizationRecheck;
        Assert.That(SessionAdminGuard.Runs, Is.EqualTo(1), "deferred behind the navigation waiting on its hook");

        // Back to the page still on screen: no navigation runs, and the one that waited is released.
        Assert.That(await _adapter.NavigateAsync("secure", historyTraversal: true), Is.True);
        await SettleRechecksAsync();

        Assert.That(_commits.Select(c => (c.Path, c.Trigger)), Is.EqualTo(new[] { ("/secure", NavigationTrigger.InitialLoad), ("/secure", NavigationTrigger.Reload) }));
        Assert.That(_store.Current!.Error?.Kind, Is.EqualTo(RouteErrorKind.NotAuthorized), "the signed-out user no longer sees the page");
    }

    [Test]
    public async Task Retry_on_the_old_pages_error_while_a_hooked_navigation_waits_reloads_the_new_location()
    {
        _session.Admin = false;
        await StartAsync("http://localhost/secure");
        var error = _store.Current!.Error!;
        Assert.That(error.Kind, Is.EqualTo(RouteErrorKind.NotAuthorized));
        HoldNavigationsIn(new TaskCompletionSource());
        Assert.That(await _adapter.NavigateAsync("login", intercepted: true), Is.True);

        // The old page's error (still on screen while the hook runs) offers Retry.
        await error.Retry();

        Assert.That(_adapter.Uri, Is.EqualTo("http://localhost/login"));
        Assert.That(_commits.Select(c => (c.Path, c.Trigger)), Is.EqualTo(new[] { ("/secure", NavigationTrigger.InitialLoad), ("/login", NavigationTrigger.Link) }), "the reload replaced the waiting navigation: one commit, as that navigation");
        Assert.That(_store.Current!.Error, Is.Null);
    }

    [Test]
    public async Task Events_during_a_running_check_run_exactly_one_follow_up()
    {
        await StartAsync("http://localhost/secure");
        SessionAdminGuard.Gate = new TaskCompletionSource();

        _provider.Notify();
        var first = _coordinator.AuthorizationRecheck;
        await ComponentTestKit.EventuallyAsync(() => SessionAdminGuard.Runs, Is.EqualTo(2));
        _provider.Notify();
        _provider.Notify();
        _provider.Notify();
        Assert.That(SessionAdminGuard.Runs, Is.EqualTo(2), "serialized: nothing runs while a check is in progress");

        SessionAdminGuard.Gate.SetResult();
        await first;
        await _coordinator.AuthorizationRecheck;

        Assert.That(SessionAdminGuard.Runs, Is.EqualTo(3), "the three events during the check collapsed into one follow-up");
        Assert.That(Reloads, Is.Zero);
    }

    [Test]
    public async Task A_navigation_the_user_starts_while_a_check_is_running_is_never_canceled_by_it()
    {
        await StartAsync("http://localhost/secure");
        SessionAdminGuard.Gate = new TaskCompletionSource();
        _session.Admin = false;
        _provider.Notify();
        var recheck = _coordinator.AuthorizationRecheck;
        await ComponentTestKit.EventuallyAsync(() => SessionAdminGuard.Runs, Is.EqualTo(2));

        CountingLoader.Gate = new TaskCompletionSource();
        var navigation = _adapter.NavigateAsync("attributes", intercepted: true);
        await ComponentTestKit.EventuallyAsync(() => CountingLoader.Runs, Is.EqualTo(2));

        // The check's guard now denies while the user's navigation is still loading.
        SessionAdminGuard.Gate.SetResult();
        await recheck;
        CountingLoader.Gate.SetResult();

        Assert.That(await navigation, Is.True, "the user's navigation was not superseded by a reload of the old URL");
        await _coordinator.AuthorizationRecheck;
        await _adapter.SettledAsync();
        await _coordinator.AuthorizationRecheck;

        // The deferred check ran against /attributes, whose [Authorize(Roles = A)] still admits the user: nothing to reload.
        Assert.That(_commits.Select(c => (c.Path, c.Trigger)), Is.EqualTo(new[] { ("/secure", NavigationTrigger.InitialLoad), ("/attributes", NavigationTrigger.Link) }));
        Assert.That(_store.Current!.Error, Is.Null);
        Assert.That(SessionAdminGuard.Runs, Is.EqualTo(2), "the guard of the page the user left is not run again");
    }

    [Test]
    public async Task An_event_while_a_redirect_is_in_flight_is_deferred_until_the_redirect_arrives()
    {
        await StartAsync("http://localhost/secure", stage: new RedirectingStage("?go", "/login"));
        _adapter.ExecuteNavigateTo = false;
        Assert.That(await _adapter.NavigateAsync("/?go", intercepted: true), Is.False, "the stage redirected: the platform does not move to the redirecting URL");
        Assert.That(_adapter.NavigateCalls.Select(c => c.Uri), Is.EqualTo(new[] { "/login" }), "the redirect is on its way, not yet arrived");

        _session.Admin = false;
        _provider.Notify();
        await _coordinator.AuthorizationRecheck;

        Assert.That(SessionAdminGuard.Runs, Is.EqualTo(1), "deferred: a reload of /secure now would race the redirect, and the page it leaves is not the one that matters");
        Assert.That(_commits, Has.Count.EqualTo(1));

        Assert.That(await _adapter.NavigateAsync("/login"), Is.True, "the redirect arrives");
        await SettleRechecksAsync();

        Assert.That(_commits.Select(c => (c.Path, c.Trigger)), Is.EqualTo(new[] { ("/secure", NavigationTrigger.InitialLoad), ("/login", NavigationTrigger.Redirect) }));
        Assert.That(SessionAdminGuard.Runs, Is.EqualTo(1), "the check that ran after the redirect committed found no authorization guard on /login");
    }

    [Test]
    public async Task A_check_after_a_redirect_to_the_current_location_runs_at_once_because_no_redirect_is_on_its_way()
    {
        await StartAsync("http://localhost/secure", stage: new RedirectingStage("?go", "/secure"));
        _adapter.ExecuteNavigateTo = false;
        Assert.That(await _adapter.NavigateAsync("/?go", intercepted: true), Is.False);
        Assert.That(_adapter.NavigateCalls, Is.Empty, "a redirect to the page on screen goes nowhere");

        _session.Admin = false;
        _provider.Notify();
        await SettleRechecksAsync();

        Assert.That(Reloads, Is.EqualTo(1), "nothing deferred it: it ran and reloaded into the new outcome");
        Assert.That(_store.Current!.Error?.Kind, Is.EqualTo(RouteErrorKind.NotAuthorized));
    }

    [Test]
    public async Task A_check_deferred_behind_a_redirect_runs_when_the_redirect_ends_on_the_current_page_and_the_platform_does_not_move()
    {
        // The target differs from the current location only by its fragment, so the redirect still navigates.
        await StartAsync("http://localhost/secure", stage: new RedirectingStage("?go", "/secure#details"));
        _adapter.ExecuteNavigateTo = false;
        Assert.That(await _adapter.NavigateAsync("/?go", intercepted: true), Is.False);
        Assert.That(_adapter.NavigateCalls.Select(c => c.Uri), Is.EqualTo(new[] { "/secure#details" }));
        _session.Admin = false;
        _provider.Notify();
        await _coordinator.AuthorizationRecheck;
        Assert.That(SessionAdminGuard.Runs, Is.EqualTo(1), "deferred behind the redirect");

        // The redirect arrives on the page on screen (the handler lets it through without a pipeline) and a NavigationLock
        // after the router's handler stops the platform: no location change follows, so nothing but the handler can
        // release the deferred check.
        using var lockHandler = _adapter.RegisterLocationChanging(_ => ValueTask.FromResult(LocationChangingDecision.Prevent));
        Assert.That(await _adapter.NavigateAsync("/secure#details"), Is.False);
        await SettleRechecksAsync();

        Assert.That(Reloads, Is.EqualTo(1), "the deferred check ran and reloaded into the new outcome");
        Assert.That(_store.Current!.Error?.Kind, Is.EqualTo(RouteErrorKind.NotAuthorized));
    }

    [Test]
    public async Task A_check_deferred_behind_a_redirect_to_another_page_that_a_lock_prevents_runs_at_the_next_navigation()
    {
        await StartAsync("http://localhost/secure", stage: new RedirectingStage("?go", "/login"));
        _adapter.ExecuteNavigateTo = false;
        Assert.That(await _adapter.NavigateAsync("/?go", intercepted: true), Is.False);
        _session.Admin = false;
        _provider.Notify();
        await _coordinator.AuthorizationRecheck;
        Assert.That(SessionAdminGuard.Runs, Is.EqualTo(1), "deferred behind the redirect");

        // The router prepares /login and allows it; a NavigationLock prevents it. The platform tells the router nothing,
        // so the prepared state waits (docs/known-limitations.md) and keeps the check deferred.
        var lockHandler = _adapter.RegisterLocationChanging(_ => ValueTask.FromResult(LocationChangingDecision.Prevent));
        Assert.That(await _adapter.NavigateAsync("/login"), Is.False);
        await SettleRechecksAsync();
        Assert.That(SessionAdminGuard.Runs, Is.EqualTo(1), "still deferred: nothing told the router the hop was prevented");

        // The next navigation ends the wait; the check then runs against the page it commits, not the one it left.
        lockHandler.Dispose();
        Assert.That(await _adapter.NavigateAsync("parent/child", intercepted: true), Is.True);
        await SettleRechecksAsync();

        Assert.That(_commits.Select(c => (c.Path, c.Trigger)), Is.EqualTo(new[] { ("/secure", NavigationTrigger.InitialLoad), ("/parent/child", NavigationTrigger.Link) }));
        Assert.That(SessionAdminGuard.Runs, Is.EqualTo(3), "the navigation ran the child's guard, then the deferred check ran it again");
        Assert.That(Reloads, Is.Zero, "the outcome the navigation committed (not authorized) did not change");
    }

    [Test]
    public async Task A_check_that_finishes_after_a_navigation_arrived_waits_for_its_hook_instead_of_replacing_it()
    {
        await StartAsync("http://localhost/secure");
        var hook = new TaskCompletionSource();
        var hookToken = CancellationToken.None;
        _coordinator.OnNavigate = (_, token) =>
        {
            hookToken = token;

            return hook.Task.WaitAsync(token);
        };
        SessionAdminGuard.Gate = new TaskCompletionSource();
        _session.Admin = false;
        _provider.Notify();
        var recheck = _coordinator.AuthorizationRecheck;
        await ComponentTestKit.EventuallyAsync(() => SessionAdminGuard.Runs, Is.EqualTo(2));

        // The location changes while the check's guards run: nothing is in flight any more, the hook is.
        Assert.That(await _adapter.NavigateAsync("login", intercepted: true), Is.True);
        SessionAdminGuard.Gate.SetResult();
        await recheck;

        Assert.That(_commits, Has.Count.EqualTo(1), "no reload: it would replace the navigation whose hook is still running");
        Assert.That(hookToken.IsCancellationRequested, Is.False);
        Assert.That(_store.IsNavigating, Is.True);

        hook.SetResult();
        await ComponentTestKit.EventuallyAsync(() => _commits.Count, Is.EqualTo(2));
        await SettleRechecksAsync();

        Assert.That(_commits.Select(c => (c.Path, c.Trigger)), Is.EqualTo(new[] { ("/secure", NavigationTrigger.InitialLoad), ("/login", NavigationTrigger.Link) }), "the deferred check ran against /login, which has no authorization guard");
        Assert.That(SessionAdminGuard.Runs, Is.EqualTo(2));
    }

    [Test]
    public async Task A_check_that_finishes_after_the_users_navigation_committed_rechecks_the_new_page_instead()
    {
        await StartAsync("http://localhost/secure");
        SessionAdminGuard.Gate = new TaskCompletionSource();
        _session.Admin = false;
        _provider.Notify();
        var recheck = _coordinator.AuthorizationRecheck;
        await ComponentTestKit.EventuallyAsync(() => SessionAdminGuard.Runs, Is.EqualTo(2));

        Assert.That(await _adapter.NavigateAsync("/", intercepted: true), Is.True);
        Assert.That(_store.Current!.Path, Is.EqualTo("/"));

        SessionAdminGuard.Gate.SetResult();
        await recheck;
        await _adapter.SettledAsync();
        await _coordinator.AuthorizationRecheck;

        // The stale verdict for /secure must not reload /, which has no authorization guards.
        Assert.That(_commits.Select(c => (c.Path, c.Trigger)), Is.EqualTo(new[] { ("/secure", NavigationTrigger.InitialLoad), ("/", NavigationTrigger.Link) }));
        Assert.That(Reloads, Is.Zero);
    }

    [Test]
    public async Task A_check_that_finishes_between_the_users_navigation_being_allowed_and_committing_waits_for_the_commit()
    {
        await StartAsync("http://localhost/secure");
        SessionAdminGuard.Gate = new TaskCompletionSource();
        _session.Admin = false;
        _provider.Notify();
        var recheck = _coordinator.AuthorizationRecheck;
        await ComponentTestKit.EventuallyAsync(() => SessionAdminGuard.Runs, Is.EqualTo(2));

        // A second location-changing handler, after the router's, holds the navigation once the router allowed it:
        // prepared and waiting for its location-changed notification, no longer in flight.
        var held = new TaskCompletionSource();
        var reached = new TaskCompletionSource();
        using var hold = _adapter.RegisterLocationChanging(async _ =>
        {
            reached.TrySetResult();
            await held.Task;

            return LocationChangingDecision.Allow;
        });
        var navigation = _adapter.NavigateAsync("attributes", intercepted: true);
        await reached.Task;

        SessionAdminGuard.Gate.SetResult();
        await recheck;
        held.SetResult();

        Assert.That(await navigation, Is.True);
        await _coordinator.AuthorizationRecheck;
        await _adapter.SettledAsync();
        await _coordinator.AuthorizationRecheck;

        Assert.That(_commits.Select(c => (c.Path, c.Trigger)), Is.EqualTo(new[] { ("/secure", NavigationTrigger.InitialLoad), ("/attributes", NavigationTrigger.Link) }));
        Assert.That(_store.Current!.Error, Is.Null);
    }

    [Test]
    public async Task Disposing_the_router_unsubscribes()
    {
        await StartAsync("http://localhost/secure");
        _lease!.Dispose();
        _lease = null;
        _session.Admin = false;

        await ChangeAuthenticationAsync();

        Assert.That(SessionAdminGuard.Runs, Is.EqualTo(1));
        Assert.That(_commits, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task An_ordinary_guards_not_authorized_causes_no_reload()
    {
        OrdinaryGuard.Verdict = () => RouteGuardResult.NotAuthorized("ordinary");
        await StartAsync("http://localhost/secure");
        Assert.That(_store.Current!.Error!.GuardType, Is.EqualTo(typeof(OrdinaryGuard)));
        Assert.That(SessionAdminGuard.Runs, Is.Zero, "declared after the ordinary guard, it never ran");

        await ChangeAuthenticationAsync();
        _session.Admin = false;
        await ChangeAuthenticationAsync();

        Assert.That(Reloads, Is.Zero, "the authorization guards cannot have changed an outcome they never produced");
        Assert.That(SessionAdminGuard.Runs, Is.Zero, "a guard that did not run at commit time is not run by the re-check either");
        Assert.That(OrdinaryGuard.Runs, Is.EqualTo(1));
    }

    [Test]
    public async Task A_not_authorized_fragment_that_navigates_to_login_does_not_loop()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton(_session);
        ctx.Services.AddSingleton<AuthenticationStateProvider>(_provider);
        ctx.Services.AddModRouter(map => map
            .Route("/login", typeof(LoginPage), name: "login")
            .Route("/secure", typeof(SecurePage), name: "secure").Guard<SessionAdminGuard>());
        var adapter = new FakeNavigationAdapter("http://localhost/", "http://localhost/secure") { ExecuteNavigateTo = true };
        ctx.Services.AddSingleton<INavigationPlatformAdapter>(adapter);
        var commits = new List<RouteState>();
        ctx.Services.GetRequiredService<RouteStateStore>().Changed += commits.Add;

        var cut = ctx.Render<Components.ModRouter>(p => p.Add(r => r.NotAuthorized, (RenderFragment<RouteError>)(_ => b =>
        {
            b.OpenComponent<RedirectToLoginProbe>(0);
            b.CloseComponent();
        })));
        Assert.That(cut.Find("#secure"), Is.Not.Null);

        _session.SignedIn = false;
        _session.Admin = false;
        _provider.Notify();
        await ctx.Services.GetRequiredService<NavigationCoordinator>().AuthorizationRecheck;
        await adapter.SettledAsync();
        await ComponentTestKit.EventuallyAsync(() => cut.Markup.Contains("id=\"login\"", StringComparison.Ordinal), Is.True);
        await Task.Delay(100);

        Assert.That(RedirectToLoginProbe.Renders, Is.EqualTo(1), "the fragment rendered once and navigated once");
        Assert.That(commits.Select(c => (c.Path, c.Trigger)), Is.EqualTo(new[] { ("/secure", NavigationTrigger.InitialLoad), ("/secure", NavigationTrigger.Reload), ("/login", NavigationTrigger.Programmatic) }));
        Assert.That(adapter.History, Is.EqualTo(new[] { "http://localhost/login" }), "replaced, like the sites' RedirectToLogin");
        Assert.That(ctx.Renderer.UnhandledException.IsCompleted, Is.False);
    }
}
