using Bunit;
using ModRouter.Authorization;
using ModRouter.Components;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Guards;
using ModRouter.Navigation;
using ModRouter.Test.AuthorizationPages;
using ModRouter.Test.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace ModRouter.Test;

/// <summary>
/// The guard reaches every route that needs it, whichever source declared the route (fluent, discovered, markup), first
/// among the level's guards; anonymous access from either source means no guard; the chain is evaluated per level,
/// parent first; <see cref="RouteError.GuardType"/> records who denied.
/// </summary>
[TestFixture]
public sealed class AuthorizationRouteTests
{
    static readonly System.Reflection.Assembly Fixtures = typeof(Pages.App.HomePage).Assembly;

    static Func<Type, bool> Set(string set) => type => type.Namespace == $"ModRouter.Test.Pages.{set}";

    static IEnumerable<Type> Guards(RouteDefinition route) => route.Metadata.GetAll<RouteGuardMetadata>().Select(g => g.GuardType);

    static RouteTableHolder Holder(Action<RouteMap> routes)
        => new ServiceCollection().AddModRouter(routes).BuildServiceProvider().GetRequiredService<RouteTableHolder>();

    [SetUp]
    public void SetUp() => RecordingGuard.Calls.Clear();

    // ---- the contributor: every source, the guard first, anonymous means none -------------------------------------

    [Test]
    public void A_fluent_route_whose_component_carries_Authorize_gets_the_guard_before_its_declared_guards()
    {
        var holder = Holder(map => map.Route("/secure", typeof(P03_RolesA), name: "secure").Guard<AuthGuard>().Guard<AuditGuard>());

        Assert.That(Guards(holder.GetRequiredRoute("secure")), Is.EqualTo(new[] { typeof(AuthorizeAttributeGuard), typeof(AuthGuard), typeof(AuditGuard) }));
    }

    [Test]
    public void Fluent_Authorize_metadata_adds_the_guard_to_a_plain_component_and_is_kept_on_the_route()
    {
        var holder = Holder(map => map
            .Route("/any", typeof(P01_NoAttrs), name: "any").Authorize()
            .Route("/admins", typeof(P01_NoAttrs), name: "admins").Authorize(roles: "admin", policy: "NeedsX"));

        Assert.That(Guards(holder.GetRequiredRoute("any")), Is.EqualTo(new[] { typeof(AuthorizeAttributeGuard) }));
        Assert.That(holder.GetRequiredRoute("admins").Metadata.Get<RouteAuthorizeMetadata>(), Is.EqualTo(new RouteAuthorizeMetadata("admin", "NeedsX")));
    }

    [Test]
    public void AllowAnonymous_from_the_class_or_the_route_metadata_means_no_guard_and_a_plain_route_gets_none()
    {
        var holder = Holder(map => map
            .Route("/open", typeof(P11_AllowAnonOnly), name: "open")
            .Route("/index", typeof(P10_AuthorizePlusAllowAnon), name: "index")
            .Route("/opted-out", typeof(P03_RolesA), name: "opted-out").AllowAnonymous()
            .Route("/both", typeof(P01_NoAttrs), name: "both").Authorize().AllowAnonymous()
            .Route("/plain", typeof(P01_NoAttrs), name: "plain").Guard<AuthGuard>()
            .Redirect("/old", "plain"));

        foreach (var name in new[] { "open", "index", "opted-out", "both", "plain" })
        {
            Assert.That(Guards(holder.GetRequiredRoute(name)), Does.Not.Contain(typeof(AuthorizeAttributeGuard)), name);
        }

        Assert.That(Guards(holder.GetRequiredRoute("plain")), Is.EqualTo(new[] { typeof(AuthGuard) }), "other guards are untouched");
        Assert.That(holder.Table.Match("/old").IsMatch, Is.True, "the redirect definition compiles (a redirect cannot carry a guard, RT0017)");
    }

    [Test]
    public void Discovered_pages_get_the_guard_too_and_a_nested_child_is_contributed_at_its_depth()
    {
        var holder = Holder(map => map.AddPages(Fixtures, Set("Authorized")));

        Assert.That(Guards(holder.GetRequiredRoute("secure")), Is.EqualTo(new[] { typeof(AuthorizeAttributeGuard), typeof(RecordingGuard) }), "[Authorize(Roles)] + [RouteGuard]: the authorization guard runs first");
        Assert.That(Guards(holder.GetRequiredRoute("open")), Is.Empty, "[Authorize] + [AllowAnonymous], the real apps' Index page");
        Assert.That(Guards(holder.GetRequiredRoute("secure-child")), Is.EqualTo(new[] { typeof(AuthorizeAttributeGuard) }), "a child page under [RouteParent] is contributed in place");
    }

    [Test]
    public void Applying_the_contributor_twice_adds_the_guard_once()
    {
        var definitions = RouteMap.Create().Route("/secure", typeof(P02_Authorize), name: "secure").Guard<AuthGuard>().Build();

        var once = AuthorizationRouteContributor.Apply(definitions);
        var twice = AuthorizationRouteContributor.Apply(once);

        Assert.That(Guards(twice[0]), Is.EqualTo(new[] { typeof(AuthorizeAttributeGuard), typeof(AuthGuard) }));
        Assert.That(twice[0], Is.SameAs(once[0]), "nothing to add: the definition is returned as is");
        Assert.That(definitions[0].Metadata.GetAll<RouteGuardMetadata>().Count(), Is.EqualTo(1), "the input definitions are immutable and untouched");
    }

    // ---- per level, parent first; the guard type on the error --------------------------------------------------------

    static NavigationCoordinator Coordinator(IServiceProvider services, FakeNavigationAdapter adapter, RouteStateStore store, Action<RouteMap> routes)
    {
        var map = RouteMap.Create();
        routes(map);
        var table = RouteCompiler.Compile(AuthorizationRouteContributor.Apply(map.Build()));

        return new NavigationCoordinator(adapter, store, RouteTestKit.Frozen(table), new RouteComponentBinder(), services, new ModRouterOptions(), [], NullLoggerFactory.Instance);
    }

    static void NestedRoutes(RouteMap map) => map
        .Route("/parent", typeof(P03_RolesA), name: "parent", children: parent => parent
            .Route("{id:int}", typeof(P02_Authorize), name: "child"));

    [Test]
    public async Task A_parent_that_denies_stops_the_chain_before_the_child_is_evaluated()
    {
        var provider = new FakeAuthenticationStateProvider(Users.WithRoles("B"));
        await using var services = new ServiceCollection().AddTestAuthorization(provider).BuildServiceProvider();
        var store = new RouteStateStore();
        var coordinator = Coordinator(services, new FakeNavigationAdapter("http://localhost/", "http://localhost/parent/1"), store, NestedRoutes);
        using var lease = coordinator.Attach();

        await coordinator.NavigateInitialAsync();

        Assert.That(store.Current!.Error!.Kind, Is.EqualTo(RouteErrorKind.NotAuthorized));
        Assert.That(store.Current.Error.Level, Is.Zero, "the parent (depth 0) denied");
        Assert.That(store.Current.Error.GuardType, Is.EqualTo(typeof(AuthorizeAttributeGuard)));
        Assert.That(store.Current.Error.Message, Is.EqualTo(AuthorizeAttributeGuard.DeniedMessage));
        Assert.That(store.Current.Chain!.Matches, Has.Count.EqualTo(2), "the chain is kept: levels above the failed one render");
        Assert.That(provider.Reads, Is.EqualTo(1), "the child's guard never ran");
    }

    [Test]
    public async Task Every_level_is_evaluated_parent_first_when_the_parent_allows()
    {
        var provider = new FakeAuthenticationStateProvider(Users.WithRoles("A"));
        await using var services = new ServiceCollection().AddTestAuthorization(provider).BuildServiceProvider();
        var store = new RouteStateStore();
        var coordinator = Coordinator(services, new FakeNavigationAdapter("http://localhost/", "http://localhost/parent/1"), store, NestedRoutes);
        using var lease = coordinator.Attach();

        await coordinator.NavigateInitialAsync();

        Assert.That(store.Current!.Error, Is.Null);
        Assert.That(provider.Reads, Is.EqualTo(2), "parent then child");

        provider.User = Users.All.Single(u => u.Name == "UnauthRoleA").User;
        await coordinator.ReloadAsync();

        Assert.That(store.Current!.Error!.Level, Is.EqualTo(1), "the parent (roles only) admits the role claim; the child ([Authorize]) wants an authenticated user");
    }

    [Test]
    public async Task RouteError_records_the_guard_that_denied_or_threw()
    {
        using var pipeline = Pipeline.Create(Pipeline.Table(map => map
            .Route("/denied", typeof(OtherPage), name: "denied").Guard<AuthGuard>()
            .Route("/broken", typeof(OtherPage), name: "broken").Guard<AuditGuard>()));
        pipeline.Guards.Set("auth", RouteGuardResult.NotAuthorized("no"));
        pipeline.Guards.Set("audit", _ => throw new InvalidOperationException("boom"));
        await pipeline.Coordinator.NavigateInitialAsync();

        await pipeline.GoAsync("denied");
        Assert.That(pipeline.Current.Error!.Kind, Is.EqualTo(RouteErrorKind.NotAuthorized));
        Assert.That(pipeline.Current.Error.GuardType, Is.EqualTo(typeof(AuthGuard)));

        await pipeline.GoAsync("broken");
        Assert.That(pipeline.Current.Error!.Kind, Is.EqualTo(RouteErrorKind.GuardFailed));
        Assert.That(pipeline.Current.Error.GuardType, Is.EqualTo(typeof(AuditGuard)));

        Assert.That(new RouteError(RouteErrorKind.LoadFailed, "m", null, 0).GuardType, Is.Null, "only guard errors name a guard");
    }

    // ---- the markup form ------------------------------------------------------------------------------------------------

    static RouteEntryNode Node(Type component, bool authorize = false, string? roles = null, string? policy = null, bool allowAnonymous = false)
        => new() { Path = "/x", Component = component, Name = "x", Authorize = authorize, Roles = roles, Policy = policy, AllowAnonymous = allowAnonymous };

    [Test]
    public void A_markup_entry_with_Authorize_Roles_or_Policy_declares_the_metadata_and_AllowAnonymous_opts_out()
    {
        var errors = new List<RouteCompilationError>();

        Assert.That(Node(typeof(HomeProbe), authorize: true).ToDefinition(errors).Metadata.Get<RouteAuthorizeMetadata>(), Is.EqualTo(new RouteAuthorizeMetadata()));
        Assert.That(Node(typeof(HomeProbe), roles: "admin").ToDefinition(errors).Metadata.Get<RouteAuthorizeMetadata>(), Is.EqualTo(new RouteAuthorizeMetadata("admin")), "Roles implies Authorize");
        Assert.That(Node(typeof(HomeProbe), policy: "NeedsX").ToDefinition(errors).Metadata.Get<RouteAuthorizeMetadata>(), Is.EqualTo(new RouteAuthorizeMetadata(null, "NeedsX")), "Policy implies Authorize");
        Assert.That(Node(typeof(HomeProbe)).ToDefinition(errors).Metadata.Contains<RouteAuthorizeMetadata>(), Is.False);
        Assert.That(Node(typeof(P03_RolesA), allowAnonymous: true).ToDefinition(errors).Metadata.Contains<RouteAllowAnonymousMetadata>(), Is.True);
        Assert.That(errors, Is.Empty);

        var contributed = AuthorizationRouteContributor.Apply([Node(typeof(P03_RolesA), allowAnonymous: true).ToDefinition(errors), Node(typeof(HomeProbe), roles: "admin").ToDefinition(errors)]);
        Assert.That(Guards(contributed[0]), Is.Empty, "markup AllowAnonymous beats the component's [Authorize]");
        Assert.That(Guards(contributed[1]), Is.EqualTo(new[] { typeof(AuthorizeAttributeGuard) }));
        Assert.That(Node(typeof(HomeProbe), roles: "admin", allowAnonymous: false).Describe(), Does.Contain(" Roles=\"admin\""), "the parameters are part of the entry's identity");
    }

    [Test]
    public void A_markup_entry_with_AllowAnonymous_and_an_authorization_parameter_is_RT0023()
    {
        var errors = new List<RouteCompilationError>();

        Node(typeof(HomeProbe), roles: "admin", allowAnonymous: true).ToDefinition(errors);

        Assert.That(errors.Select(e => e.Code), Is.EqualTo(new[] { "RT0023" }));
        Assert.That(errors[0].Message, Does.Contain("declares AllowAnonymous together with Authorize, Roles or Policy; use one of them."));
    }

    [Test]
    public async Task A_markup_entry_with_Roles_is_enforced_by_the_router_with_the_guard_first()
    {
        var provider = new FakeAuthenticationStateProvider(Users.WithRoles("user"));
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddTestAuthorization(provider);
        ctx.Services.AddModRouter();
        var adapter = new FakeNavigationAdapter("http://localhost/", "http://localhost/secure") { ExecuteNavigateTo = true };
        ctx.Services.AddSingleton<INavigationPlatformAdapter>(adapter);

        var cut = ctx.Render<Components.ModRouter>(p =>
        {
            p.Add(r => r.Routes, builder =>
            {
                builder.OpenComponent<ModRouteEntry>(0);
                builder.AddComponentParameter(1, nameof(ModRouteEntry.Path), "/secure");
                builder.AddComponentParameter(2, nameof(ModRouteEntry.Component), typeof(HomeProbe));
                builder.AddComponentParameter(3, nameof(ModRouteEntry.Name), "secure");
                builder.AddComponentParameter(4, nameof(ModRouteEntry.Roles), "admin");
                builder.AddComponentParameter(5, nameof(ModRouteEntry.Guard), typeof(RecordingGuard));
                builder.CloseComponent();
            });
            p.Add(r => r.NotAuthorized, error => b => b.AddMarkupContent(0, $"<p id=\"denied\">{error.Message}</p>"));
        });

        var store = ctx.Services.GetRequiredService<RouteStateStore>();
        await ComponentTestKit.EventuallyAsync(() => store.Current?.Error?.Kind, Is.EqualTo(RouteErrorKind.NotAuthorized));

        Assert.That(Guards(ctx.Services.GetRequiredService<RouteTableHolder>().GetRequiredRoute("secure")), Is.EqualTo(new[] { typeof(AuthorizeAttributeGuard), typeof(RecordingGuard) }));
        Assert.That(store.Current!.Error!.GuardType, Is.EqualTo(typeof(AuthorizeAttributeGuard)));
        Assert.That(RecordingGuard.Calls, Is.Empty, "the authorization guard denied first; the declared guard never ran");
        await ComponentTestKit.EventuallyAsync(() => cut.Markup.Contains("id=\"denied\"", StringComparison.Ordinal), Is.True);

        provider.User = Users.WithRoles("admin");
        await ctx.Services.GetRequiredService<INavigationService>().ReloadAsync();

        Assert.That(store.Current!.Error, Is.Null);
        Assert.That(RecordingGuard.Calls, Is.EqualTo(new[] { "secure" }));
        await ComponentTestKit.EventuallyAsync(() => cut.Markup.Contains("id=\"home\"", StringComparison.Ordinal), Is.True);
        Assert.That(ctx.Renderer.UnhandledException.IsCompleted, Is.False);
    }
}
