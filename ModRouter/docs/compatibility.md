# Compatibility

The drop-in layer over the built-in Router and the compatibility matrix. Back to the [README](../README.md).
This page moves from the original README unchanged apart from slice labels.

## Compatibility with the built-in Router

`ModRouter.Compatibility` holds drop-in replacements for the built-in `Router`, `RouteView` and
`AuthorizeRouteView` with the built-in's parameter names, built on `ModRouter`. The migration of a site is a mechanical edit
of `App.razor`: the two opening and the two closing tag names, **fully qualified**. Razor ignores a `@using X = ...` alias for
a tag name (a plain `<Router>` keeps binding to the built-in, silently) and importing the namespace next to the framework's
is RZ9985, so neither is documented. Parameters, the `Found`/`NotAuthorized`/`NotFound` fragments, `context`, `routeData` and
`App.razor.cs` stay as they are.

Before (a typical `App.razor`):

```razor
<CascadingAuthenticationState>
    <Router AppAssembly="@typeof(Program).Assembly" AdditionalAssemblies="@lazyLoadedAssemblies" OnNavigateAsync="@OnNavigate">
        <Found Context="routeData">
            <AuthorizeRouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)">
                <NotAuthorized>
                    @if (!context.User.Identity.IsAuthenticated)
                    {
                        <RedirectToLogin />
                    }
                    else
                    {
                        @RedirectToNotAuthorized();
                    }
                </NotAuthorized>
            </AuthorizeRouteView>
        </Found>
        <NotFound>
            <LayoutView Layout="@typeof(MainLayout)">
                @RedirectToDashboard();
            </LayoutView>
        </NotFound>
    </Router>
</CascadingAuthenticationState>
```

After (the four tag names are the whole change):

```razor
<CascadingAuthenticationState>
    <ModRouter.Compatibility.Router AppAssembly="@typeof(Program).Assembly" AdditionalAssemblies="@lazyLoadedAssemblies" OnNavigateAsync="@OnNavigate">
        <Found Context="routeData">
            <ModRouter.Compatibility.AuthorizeRouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)">
                <NotAuthorized>
                    @if (!context.User.Identity.IsAuthenticated)
                    {
                        <RedirectToLogin />
                    }
                    else
                    {
                        @RedirectToNotAuthorized();
                    }
                </NotAuthorized>
            </ModRouter.Compatibility.AuthorizeRouteView>
        </Found>
        <NotFound>
            <LayoutView Layout="@typeof(MainLayout)">
                @RedirectToDashboard();
            </LayoutView>
        </NotFound>
    </ModRouter.Compatibility.Router>
</CascadingAuthenticationState>
```

`Program.cs` adds `builder.Services.AddModRouter()` (with fluent routes only for what has no page form). The pages are
discovered by the compat `Router` itself at its first navigation, so a startup `Redirect(...)` or guard redirect that targets a
*discovered* page by name fails at `AddModRouter` with RT0017: declare such a route with `AddPages` at startup, in markup, or in
the fluent map. The sequence: the first render shows `Navigating` bare; `OnNavigateAsync` runs for the first navigation (add
the "lazy" assemblies there); `AppAssembly` and `AdditionalAssemblies` are read **once**; the pages are discovered, compiled with
the startup routes and frozen; the first navigation starts. Every later navigation runs `OnNavigateAsync` after its location
changed (`NavigationManager.Uri` is the new URI) and before its page renders, with the old page kept and the navigation
indicator on; its `NavigationContext` is the framework type (`Path` base-relative with query and fragment, as the built-in
gives it) and its token is canceled only while the callback is still running, when the next location change, a reload that replaces the
navigation or the router's removal supersedes it (a navigation that is then canceled or prevented supersedes nothing); once the callback
has returned its token is never canceled.
Unlike the built-in, it runs only for a navigation to another path or query (not for a fragment-only or same-URL one), and
only once the route was recognized and its guards and loaders ran, while the location was changing; the built-in runs it for
every location change, before route matching, so per-route lazy loading or state a guard reads cannot rely on it here. A reload while the
callback runs (a retry, a sign-in or sign-out) replaces the waiting navigation: it renders the location the platform shows and commits
once, as that navigation (its trigger, so the new page's title, announcement, focus and scroll run, not a reload's none). `RouteView` renders the chain inside the depth-0
page's `[Layout]` else `DefaultLayout`; `AuthorizeRouteView` has no authorization logic of its own (the guard decided before the
commit) and maps a `NotAuthorized` error at any level to its `NotAuthorized` fragment with the `AuthenticationState` from
`CascadingAuthenticationState` or the provider; that fragment, like every other error content of the router, is wrapped in
`<div data-mod-route-error-host style="display:contents">` (the built-in renders it bare), which keeps the layout but may matter
to a CSS child combinator on that content. Either view outside the compat `Router` throws an `InvalidOperationException`
that says so. An intercepted link click to a URL no route matches renders `NotFound` here; the built-in treats such a URL as
possibly non-Blazor content and does a full page load, so there only a navigation that is not intercepted (`NavigateTo`, a typed
URL) shows `NotFound`.

### Compatibility matrix

A row is *supported* only when the named tests prove it; *difference* rows are deliberate and listed as such.

| Behavior | Status | Test |
|---|---|---|
| `Router.AppAssembly`: its `@page` components are routes | supported | `CompatibilityRouterTests.A_deep_link_renders_the_nested_chain_inside_the_layout_through_the_apps_Found_fragment` |
| `Router.AdditionalAssemblies`, including an assembly added in `OnNavigateAsync` before the first navigation | supported | `CompatibilityRouterTests.OnNavigate_adds_an_assembly_before_the_first_compile_so_its_page_is_routable_on_the_first_navigation` |
| A change to `AppAssembly` / `AdditionalAssemblies` after the first navigation | difference: read once, a later change is logged once and ignored (the built-in re-reads them per render) | `CompatibilityRouterTests.An_assembly_added_after_the_first_navigation_is_ignored_with_one_warning` |
| `Router.Found` with a `RouteData` (`PageType` = the depth-0 component, its own `RouteValues`, `Template`) | supported | `CompatibilityRouterTests.A_deep_link_renders_the_nested_chain_inside_the_layout_through_the_apps_Found_fragment` |
| `Router.NotFound` (bare, the fragment brings its own `LayoutView`) | supported | `CompatibilityRouterTests.NotFound_renders_the_apps_fragment_with_its_own_LayoutView` |
| `Router.NotFoundPage` (rendered through `Found` with an empty `RouteData`) | supported | `CompatibilityRouterTests.NotFoundPage_renders_through_Found_with_an_empty_RouteData_inside_the_layout` |
| `NotFound` and `NotFoundPage` together | supported: throws the built-in's message | `CompatibilityRouterTests.Setting_NotFound_and_NotFoundPage_together_throws_the_built_ins_message` |
| An intercepted link click to a URL no route matches | difference: renders `NotFound`; the built-in does a full page load (`NavigateTo(forceLoad: true)`) because the URL may be non-Blazor content on the same host, so only a navigation that is not intercepted (`NavigateTo`, a typed URL) shows its `NotFound` | `CompatibilityRouterTests.Moving_between_a_page_and_not_found_recreates_the_layout_like_the_built_in_Router` |
| `Router.Navigating` | difference: shown bare until the first navigation commits; a later slow `OnNavigateAsync` keeps the old page (`ModNavigationIndicator` is the analogue; the built-in swaps `Navigating` in whenever it re-renders during the callback). The old page includes the `NotFound` content: while a navigation is preparing or waiting for its callback, a re-render of the router (the callback's own, or the screen-reader announcement) keeps the `NotFound` output already on screen and does not run the fragment again for that state, so a fragment that redirects (`@RedirectToDashboard();`) navigates once, not in a loop that cancels every callback | `CompatibilityRouterTests.The_first_pass_shows_Navigating_bare_while_the_first_OnNavigateAsync_is_slow`, `CompatibilityRouterTests.A_slow_later_OnNavigateAsync_keeps_the_old_page_and_shows_no_Navigating`, `CompatibilityRouterTests.A_NotFound_fragment_that_navigates_is_not_run_again_by_the_re_render_an_async_OnNavigateAsync_causes`, `CompatibilityRouterTests.A_NotFound_fragment_that_navigates_runs_once_per_committed_state_not_again_for_the_announcement` |
| `Router.OnNavigateAsync` on later navigations | difference: runs after the location changed and before the page renders, but only for a navigation to another path or query (the built-in also runs it for fragment-only and same-URL navigations), and after the route was recognized and its guards and loaders ran, while the location was changing (the built-in runs it before route matching): per-route lazy loading, or state a guard or loader reads, cannot rely on it | `CompatibilityRouterTests.Navigating_between_pages_keeps_the_layout_and_the_parent_instance_while_Found_re_renders_per_navigation`, `OnNavigateHookTests.The_hook_runs_after_the_location_changed_and_before_the_commit_then_the_state_commits` |
| `NavigationContext.Path` (base-relative, query and fragment kept) and `CancellationToken` | supported | `OnNavigateHookTests.The_factory_creates_the_framework_NavigationContext_through_its_internal_constructor`, `OnNavigateHookTests.The_path_is_base_relative_without_a_leading_slash_with_query_and_fragment_like_NavigationManager` |
| A superseded navigation's `OnNavigateAsync` token is canceled and it never renders (the token is canceled only while the callback is running, when a location change, a replacing reload or the router's removal supersedes it; a callback that has returned keeps an uncanceled token) | supported | `CompatibilityRouterTests.A_superseded_navigation_has_its_OnNavigateAsync_token_canceled_and_never_commits` |
| An `OnNavigateAsync` that throws | difference: on the first navigation it is unhandled, as in the built-in (the router stays on `Navigating`); on later ones it is logged and the navigation commits anyway (the built-in surfaces it as an unhandled exception) | `OnNavigateHookTests.A_throwing_hook_is_logged_and_the_navigation_commits_anyway` |
| `Router.PreferExactMatches` | supported: accepted and ignored, as the built-in ignores it | `CompatibilityRouterTests.The_plain_RouteView_renders_the_chain_in_the_layout_and_PreferExactMatches_is_accepted` |
| `NavigationManager.NotFound()` / `OnNotFound` (.NET 10) | not handled: the router does not subscribe to it | — |
| `RouteView.RouteData`, `RouteView.DefaultLayout` | supported | `CompatibilityRouterTests.The_plain_RouteView_renders_the_chain_in_the_layout_and_PreferExactMatches_is_accepted` |
| `[Authorize]` pages under the plain `RouteView` | difference: enforced anyway, because the router attaches its guard to the route whatever view renders it (the built-in `RouteView` ignores `[Authorize]`); without the authorization services such an app gets a `GuardFailed` error on those pages | `CompatibilityRouterTests.The_plain_RouteView_still_enforces_Authorize_for_an_anonymous_user`, `CompatibilityRouterTests.The_plain_RouteView_leaves_a_denial_to_the_outlets_default_content`, `AuthorizeAttributeGuardTests.Missing_services_fail_with_a_message_naming_what_to_register` |
| `[Layout]` / `@layout` on the page wins over `DefaultLayout` | supported | `CompatibilityRouterTests.A_page_with_its_own_Layout_renders_in_it_and_the_others_in_DefaultLayout` |
| Layout and page instances survive navigations; a child updates in place | supported | `CompatibilityRouterTests.Navigating_between_pages_keeps_the_layout_and_the_parent_instance_while_Found_re_renders_per_navigation` |
| Page to `NotFound` recreates the layout (separate `LayoutView`s, as in the built-in) | supported | `CompatibilityRouterTests.Moving_between_a_page_and_not_found_recreates_the_layout_like_the_built_in_Router` |
| Back and forward | supported | `CompatibilityRouterTests.Back_and_forward_render_the_right_page_and_are_recognized_as_history_traversals` |
| `AuthorizeRouteView.NotAuthorized` with the `AuthenticationState`, inside the layout | supported | `CompatibilityRouterTests.A_not_authorized_outcome_renders_the_apps_NotAuthorized_fragment_with_the_AuthenticationState_inside_the_layout` |
| Error content (the `NotAuthorized` fragment, the default denial of the plain `RouteView`) | difference: wrapped in `<div data-mod-route-error-host style="display:contents">`, which the built-in does not render; it scopes the focus fallback to the router's own error content and leaves the layout alone | `CompatibilityRouterTests.The_NotAuthorized_fragment_of_AuthorizeRouteView_is_inside_the_error_host`, `CompatibilityRouterTests.The_denial_of_a_plain_RouteView_is_inside_the_error_host` |
| The `AuthenticationState` from `CascadingAuthenticationState`, else from the provider | supported | `CompatibilityRouterTests.Without_CascadingAuthenticationState_the_state_comes_from_the_provider` |
| `AuthorizeRouteView.Authorizing` | difference: accepted, never shown (authorization completes before the commit) | `CompatibilityRouterTests.A_not_authorized_outcome_renders_the_apps_NotAuthorized_fragment_with_the_AuthenticationState_inside_the_layout` |
| `AuthorizeRouteView.Resource` | difference: accepted and ignored; the guard always passes a null resource (the built-in passes `Resource` to the authorization service when it is set, null otherwise) | `AuthorizeAttributeGuardTests.The_authorization_service_receives_a_null_resource_like_the_built_in` |
| `[Authorize]`, `Roles`, `Policy`, several attributes, inheritance, `[AllowAnonymous]`, custom `IAuthorizeData`, a fallback policy | supported: 650 cells equal to the built-in | `AuthorizationDifferentialTests.The_guard_decides_every_cell_like_the_built_in_AuthorizeRouteView` |
| `[Authorize(AuthenticationSchemes = ...)]` | supported: throws the built-in's `NotSupportedException` | `AuthorizeAttributeGuardTests.An_authentication_scheme_throws_the_built_ins_NotSupportedException` |
| When authorization is decided | difference: before the navigation commits, with the old page kept (the built-in decides while rendering and shows `Authorizing`) | `AuthorizeAttributeGuardTests.A_pending_authentication_state_is_awaited_and_honors_the_navigation_token` |
| Re-decision when the authentication state changes | supported: a reload when a level's outcome changed | `AuthorizationRecheckTests.Signing_out_on_a_protected_page_reloads_into_the_not_authorized_outcome`, `AuthorizationRecheckTests.Signing_in_on_a_not_authorized_page_shows_the_page_with_its_data` |
| Authorization of nested routes | difference: every chain level is checked, parent first (the built-in checks the page only) | `AuthorizationRouteTests.A_parent_that_denies_stops_the_chain_before_the_child_is_evaluated` |
| A `RedirectToLogin` in the `NotAuthorized` fragment | supported: navigates once, no loop | `CompatibilityRouterTests.For_an_anonymous_user_the_fragment_can_navigate_to_the_login_page_without_looping` |
| Route template syntax: `@page`, `[Route]`, several templates per page, parameters, optional and catch-all segments | supported | `PageDiscoveryTests.The_first_Route_is_the_template_and_the_others_are_aliases`, `PageDiscoveryTests.Discovered_pages_compile_match_and_generate_like_fluent_routes` |
| A top-level `[Route("tool")]` without a leading slash | supported | `PageDiscoveryTests.A_top_level_template_or_alias_without_a_leading_slash_is_normalized_to_a_rooted_one` |
| Route constraints (`int`, `long`, `bool`, `decimal`, `double`, `float`, `guid`, `datetime`, `alpha`, `min`, `max`, `range`, `length`, `minlength`, `maxlength`, `file`, `nonfile`, `regex`) | supported: the built-in's list and its decisions on a value corpus (three listed exceptions: `alpha` on an empty value, thousands separators in `double`/`float`) | `BuiltInConstraintParityTests.Every_constraint_of_the_built_in_router_is_registered_here`, `BuiltInConstraintParityTests.Matches_the_same_values_as_the_built_in_router` |
| `regex` in the browser | difference: always available (the built-in needs the `Microsoft.AspNetCore.Components.Routing.RegexConstraintSupport` switch) | `BuiltInConstraintParityTests.Every_constraint_of_the_built_in_router_is_registered_here` |
| Query binding with `[SupplyParameterFromQuery]` | supported: Blazor's own provider, untouched by the router | `RouteComponentBinderTests.Query_attributed_cascading_and_unmatched_value_properties_are_never_bound_from_the_path`, `RouterComponentTests.A_query_only_navigation_does_not_touch_components_that_do_not_use_the_query` |
| A startup redirect or guard redirect to a discovered page by name | difference: RT0017 at `AddModRouter` (declare the target at startup instead) | `CompatibilityRouterTests.A_startup_redirect_to_a_discovered_page_fails_at_AddModRouter_with_RT0017` |
| The tag names in `App.razor` | difference: fully qualified (a `@using` alias is ignored for tag names; importing the namespace is RZ9985) | — |
| Blazor Hybrid | not handled | — |
| Blazor Server and Blazor Web Apps, with or without prerendering | difference: prerendering needs `@page` or `[Route]` on every routed component and fluent or `@page` routes (no `<Routes>` markup), and the HTTP status needs `AddModRouterHttpStatus`; see [Hosting](hosting.md) | `LoaderStatePersistenceTests`, `RouteStatusTests` |

