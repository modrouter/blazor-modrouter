using ModRouter.Authorization;
using ModRouter.Core.Definitions;
using ModRouter.Core.Guards;
using ModRouter.Test.AuthorizationPages;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace ModRouter.Test;

/// <summary>
/// <see cref="AuthorizeAttributeGuard"/> on its own: every attribute form, inheritance, the precedence of
/// <c>[AllowAnonymous]</c>, the fluent metadata forms, the built-in's exceptions, the null resource, the cancellation
/// token and the missing-services message. The 650-cell comparison with the built-in is <c>AuthorizationDifferentialTests</c>.
/// </summary>
[TestFixture]
public sealed class AuthorizeAttributeGuardTests
{
    FakeAuthenticationStateProvider _provider = default!;
    ServiceProvider _services = default!;

    [SetUp]
    public void SetUp()
    {
        _provider = new FakeAuthenticationStateProvider();
        _services = new ServiceCollection().AddTestAuthorization(_provider).BuildServiceProvider();
    }

    [TearDown]
    public void TearDown() => _services.Dispose();

    async Task<RouteGuardOutcome> DecideAsync(Type page, RouteMetadataCollection? metadata = null, CancellationToken cancellationToken = default)
        => (await new AuthorizeAttributeGuard().CanActivateAsync(AuthorizationKit.GuardContext(page, _services, metadata, cancellationToken))).Outcome;

    [Test]
    public void It_is_an_authorization_route_guard_so_the_re_check_on_authentication_change_runs_it()
        => Assert.That(typeof(IAuthorizationRouteGuard).IsAssignableFrom(typeof(AuthorizeAttributeGuard)), Is.True);

    [Test]
    public async Task A_component_without_authorization_data_is_allowed_without_reading_the_authentication_state()
    {
        Assert.That(await DecideAsync(typeof(P01_NoAttrs)), Is.EqualTo(RouteGuardOutcome.Allow));
        Assert.That(_provider.Reads, Is.Zero, "the built-in blocks on the state even then; the guard does not need to");
    }

    [Test]
    public async Task A_plain_Authorize_admits_authenticated_users_and_denies_anonymous_ones()
    {
        Assert.That(await DecideAsync(typeof(P02_Authorize)), Is.EqualTo(RouteGuardOutcome.NotAuthorized));

        _provider.User = Users.Authenticated();
        Assert.That(await DecideAsync(typeof(P02_Authorize)), Is.EqualTo(RouteGuardOutcome.Allow));

        _provider.User = Users.All.Single(u => u.Name == "UnauthRoleA").User;
        Assert.That(await DecideAsync(typeof(P02_Authorize)), Is.EqualTo(RouteGuardOutcome.NotAuthorized), "a role claim without authentication is not an authenticated user");
    }

    [Test]
    public async Task Roles_admit_any_listed_role_trimmed_and_case_sensitive()
    {
        _provider.User = Users.WithRoles("Capital Markets");
        Assert.That(await DecideAsync(typeof(P06_RolesSpaces)), Is.EqualTo(RouteGuardOutcome.Allow), "entries of a comma list are trimmed and OR-ed");

        _provider.User = Users.WithRoles("A");
        Assert.That(await DecideAsync(typeof(P22_RolesLowercase)), Is.EqualTo(RouteGuardOutcome.NotAuthorized), "roles are case-sensitive");
        Assert.That(await DecideAsync(typeof(P21_RolesEmptyEntry)), Is.EqualTo(RouteGuardOutcome.Allow), "an empty entry in the list is ignored");

        _provider.User = Users.All.Single(u => u.Name == "UnauthRoleA").User;
        Assert.That(await DecideAsync(typeof(P03_RolesA)), Is.EqualTo(RouteGuardOutcome.Allow), "a roles-only [Authorize] checks the role claim alone, as the built-in does");
    }

    [Test]
    public async Task Policy_and_roles_on_one_attribute_must_both_hold()
    {
        _provider.User = Users.Authenticated(Users.Role("A"));
        Assert.That(await DecideAsync(typeof(P08_RolesAPlusPolicyX)), Is.EqualTo(RouteGuardOutcome.NotAuthorized));

        _provider.User = Users.Authenticated(Users.Role("A"), new System.Security.Claims.Claim("X", "1"));
        Assert.That(await DecideAsync(typeof(P08_RolesAPlusPolicyX)), Is.EqualTo(RouteGuardOutcome.Allow));
    }

    [Test]
    public async Task Attributes_of_a_base_class_count_and_several_attributes_combine_with_AND()
    {
        _provider.User = Users.WithRoles("B");
        Assert.That(await DecideAsync(typeof(P12_InheritsBaseA)), Is.EqualTo(RouteGuardOutcome.NotAuthorized), "the base class requires A");
        Assert.That(await DecideAsync(typeof(P13_DerivedB_BaseA)), Is.EqualTo(RouteGuardOutcome.NotAuthorized), "base A and derived B: both are required");
        Assert.That(await DecideAsync(typeof(P09_TwoAttrsAandB)), Is.EqualTo(RouteGuardOutcome.NotAuthorized));

        _provider.User = Users.WithRoles("A", "B");
        Assert.That(await DecideAsync(typeof(P12_InheritsBaseA)), Is.EqualTo(RouteGuardOutcome.Allow));
        Assert.That(await DecideAsync(typeof(P13_DerivedB_BaseA)), Is.EqualTo(RouteGuardOutcome.Allow));
        Assert.That(await DecideAsync(typeof(P09_TwoAttrsAandB)), Is.EqualTo(RouteGuardOutcome.Allow));
    }

    [Test]
    public async Task AllowAnonymous_wins_from_the_class_a_base_class_a_derived_class_and_the_route_metadata()
    {
        Assert.That(await DecideAsync(typeof(P10_AuthorizePlusAllowAnon)), Is.EqualTo(RouteGuardOutcome.Allow), "[Authorize] + [AllowAnonymous] on the class: the real apps' Index, Authentication and NotAuthorized pages");
        Assert.That(await DecideAsync(typeof(P15_BaseAuth_DerivedAllowAnon)), Is.EqualTo(RouteGuardOutcome.Allow), "a derived [AllowAnonymous] beats the base's [Authorize]");
        Assert.That(await DecideAsync(typeof(P16_BaseAnon_DerivedAuth)), Is.EqualTo(RouteGuardOutcome.Allow), "a base [AllowAnonymous] beats the derived [Authorize], as inherit: true finds it");
        Assert.That(await DecideAsync(typeof(P03_RolesA), RouteMetadataCollection.Create(new RouteAllowAnonymousMetadata())), Is.EqualTo(RouteGuardOutcome.Allow), ".AllowAnonymous() beats the component's [Authorize(Roles)]");
        Assert.That(await DecideAsync(typeof(P11_AllowAnonOnly), RouteMetadataCollection.Create(new RouteAuthorizeMetadata("A"))), Is.EqualTo(RouteGuardOutcome.Allow), "[AllowAnonymous] beats .Authorize()");
        Assert.That(_provider.Reads, Is.Zero, "an anonymous level never reads the authentication state");
    }

    [Test]
    public async Task Fluent_Authorize_metadata_is_enforced_like_the_attribute_and_combines_with_it()
    {
        var roleA = RouteMetadataCollection.Create(new RouteAuthorizeMetadata("A"));
        Assert.That(await DecideAsync(typeof(P01_NoAttrs), roleA), Is.EqualTo(RouteGuardOutcome.NotAuthorized), ".Authorize(roles: \"A\") on a plain component");

        _provider.User = Users.WithRoles("A");
        Assert.That(await DecideAsync(typeof(P01_NoAttrs), roleA), Is.EqualTo(RouteGuardOutcome.Allow));
        Assert.That(await DecideAsync(typeof(P01_NoAttrs), RouteMetadataCollection.Create(new RouteAuthorizeMetadata())), Is.EqualTo(RouteGuardOutcome.Allow), ".Authorize() alone wants an authenticated user");
        Assert.That(await DecideAsync(typeof(P03_RolesA), RouteMetadataCollection.Create(new RouteAuthorizeMetadata("B"))), Is.EqualTo(RouteGuardOutcome.NotAuthorized), "[Authorize(Roles = A)] AND .Authorize(roles: B)");
        Assert.That(await DecideAsync(typeof(P01_NoAttrs), RouteMetadataCollection.Create(new RouteAuthorizeMetadata("A"), new RouteAuthorizeMetadata("B"))), Is.EqualTo(RouteGuardOutcome.NotAuthorized), "two .Authorize() calls AND like two attributes");

        _provider.User = Users.Authenticated(Users.Role("A"), new System.Security.Claims.Claim("X", "1"));
        Assert.That(await DecideAsync(typeof(P01_NoAttrs), RouteMetadataCollection.Create(new RouteAuthorizeMetadata(null, "NeedsX"))), Is.EqualTo(RouteGuardOutcome.Allow), ".Authorize(policy: ...)");
    }

    [Test]
    public void An_authentication_scheme_throws_the_built_ins_NotSupportedException()
    {
        var exception = Assert.ThrowsAsync<NotSupportedException>(async () => await DecideAsync(typeof(P20_SchemesOnly)));

        Assert.That(exception!.Message, Is.EqualTo("The authorization data specifies an authentication scheme with value 'Cookies'. Authentication schemes cannot be specified for components."));
        Assert.That(_provider.Reads, Is.Zero, "the built-in checks the scheme before it reads anything");
    }

    [Test]
    public void A_missing_policy_and_an_empty_roles_string_throw_as_the_built_in_does()
    {
        _provider.User = Users.WithRoles("A");

        Assert.That(Assert.ThrowsAsync<InvalidOperationException>(async () => await DecideAsync(typeof(P14_MissingPolicy)))!.Message, Is.EqualTo("The AuthorizationPolicy named: 'Missing' was not found."));
        Assert.That(Assert.ThrowsAsync<InvalidOperationException>(async () => await DecideAsync(typeof(P23_RolesEmptyString)))!.Message, Is.EqualTo("At least one role must be specified."));
    }

    [Test]
    public async Task The_authorization_service_receives_a_null_resource_like_the_built_in()
    {
        _provider.User = Users.WithRoles("A");

        Assert.That(await DecideAsync(typeof(P18_ResourceSensitive)), Is.EqualTo(RouteGuardOutcome.NotAuthorized));
        Assert.That(_services.GetRequiredService<ResourceCapture>().Seen, Is.EqualTo(new[] { "null" }));
    }

    [Test]
    public async Task A_pending_authentication_state_is_awaited_and_honors_the_navigation_token()
    {
        var pending = new TaskCompletionSource<AuthenticationState>();
        _provider.Pending = pending.Task;
        using var cancellation = new CancellationTokenSource();

        var decision = DecideAsync(typeof(P03_RolesA), cancellationToken: cancellation.Token);
        await Task.Delay(50);
        Assert.That(decision.IsCompleted, Is.False, "the guard waits for the state when authorization applies");

        cancellation.Cancel();
        Assert.ThrowsAsync<TaskCanceledException>(async () => await decision);

        _provider.Pending = Task.FromResult(new AuthenticationState(Users.WithRoles("A")));
        Assert.That(await DecideAsync(typeof(P03_RolesA)), Is.EqualTo(RouteGuardOutcome.Allow));
    }

    [Test]
    public void Missing_services_fail_with_a_message_naming_what_to_register()
    {
        using var nothing = new ServiceCollection().BuildServiceProvider();
        var exception = Assert.ThrowsAsync<InvalidOperationException>(async () => await new AuthorizeAttributeGuard().CanActivateAsync(AuthorizationKit.GuardContext(typeof(P02_Authorize), nothing)));

        Assert.That(exception!.Message, Does.StartWith("The route '/x' requires authorization (ModRouter.Test.AuthorizationPages.P02_Authorize carries [Authorize], or the route declares Authorize), but"));
        Assert.That(exception.Message, Does.Contain("AuthenticationStateProvider").And.Contain("services.AddAuthorizationCore()"));

        using var providerOnly = new ServiceCollection().AddSingleton<AuthenticationStateProvider>(_provider).BuildServiceProvider();
        var withoutAuthorization = Assert.ThrowsAsync<InvalidOperationException>(async () => await new AuthorizeAttributeGuard().CanActivateAsync(AuthorizationKit.GuardContext(typeof(P02_Authorize), providerOnly)));

        Assert.That(withoutAuthorization!.Message, Does.Contain("services.AddAuthorizationCore()").And.Not.Contain("the application's provider"));

        Assert.DoesNotThrowAsync(async () => await new AuthorizeAttributeGuard().CanActivateAsync(AuthorizationKit.GuardContext(typeof(P01_NoAttrs), nothing)), "a route that needs no authorization needs no services either");
    }
}
