# Authorization

Authorization as a route guard. Back to the [README](../README.md).

## Authorization

Authorization is a route guard, so every route source gets the same enforcement and the same error state:

```razor
@page "/reports"
@attribute [Authorize(Roles = "admin")]      @* the standard attributes; nothing router-specific *@
```

```csharp
routes.Route("/reports", typeof(ReportsPage), name: "reports").Authorize(roles: "admin")   // fluent: Authorize(roles, policy), AllowAnonymous()
```

```razor
<ModRouteEntry Path="/reports" Component="typeof(ReportsPage)" Roles="admin" />               @* markup: Authorize, Roles, Policy, AllowAnonymous *@
```

- **The guard.** When a table is compiled (`AddModRouter`, and the markup compile) `AuthorizeAttributeGuard` is attached
  **first** on every route whose component carries `IAuthorizeData` (`[Authorize]`, with `Roles` and `Policy`, found with
  `inherit: true`, so a base class and an `_Imports.razor` count) or whose route declares `.Authorize(...)` / `Authorize`,
  `Roles`, `Policy`; `[AllowAnonymous]` on the component, `.AllowAnonymous()` or `AllowAnonymous` means **no guard**, whichever
  source said so. Several declarations (attributes, metadata, both) combine with AND, like several `[Authorize]` attributes;
  an entry with `AllowAnonymous` and one of the others is RT0023.
- **The decision** is the built-in `AuthorizeRouteView`'s (`AuthorizationDifferentialTests`: 25 page classes by 13 users, with
  and without a fallback policy, 650 cells, every cell equal, exceptions included): the user from
  `AuthenticationStateProvider.GetAuthenticationStateAsync()` (awaited only when authorization applies, honoring the
  navigation's cancellation token), the policy from `AuthorizationPolicy.CombineAsync`, the verdict from
  `IAuthorizationService.AuthorizeAsync(user, null, policy)` (a **null** resource, as the built-in passes). A comma list of
  roles is OR with trimmed entries, roles are case-sensitive, `Roles` plus `Policy` on one attribute is AND, a roles-only
  `[Authorize]` admits an identity that has the role claim even when it is not authenticated, no fallback policy applies.
  `AuthenticationSchemes` throws the built-in's `NotSupportedException`; a missing policy or an empty `Roles` throw as the
  built-in does (a `GuardFailed` error at that level).
- **The outcome** of a denial is the existing `NotAuthorized` error at that level: the navigation commits, the levels above
  render, the level shows the not-authorized content (`ModRouter.NotAuthorized`, the outlet's, or the compatibility
  `AuthorizeRouteView.NotAuthorized`). The router never redirects on a denial; the application's `RedirectToLogin` stays
  in its fragment. `RouteError.GuardType` names the guard that denied or threw; for an `[Authorize]` denial it is the
  router's own guard, which is internal, so a fragment tells its own guards apart by comparing `GuardType`
  with their types and treats anything else as the built-in denial.
- **A denial ends the guard chain.** The pipeline records the first denial and stops running guards (`GuardStage` returns
  there), and `AuthorizeAttributeGuard` is attached **first**. An application `[RouteGuard<...>]` that redirects an
  anonymous user to the login page therefore never runs on a route that carries `[Authorize]` (nor on one that inherits it from
  an `_Imports.razor`): the anonymous user is denied before it, and the redirect belongs in the `NotAuthorized` fragment
  (`RedirectToLogin`, as an app usually has it). Such a guard still runs on a route without authorization.
- **Per chain level, parent first.** A nested route is checked at every level; a parent that denies stops the chain before
  the child's guard runs. The built-in checks the page only. Child components opened through an outlet of their own are **not routes** (the route table does not match them), so their `[Authorize]` attributes are not enforced by the router, exactly as
  `AuthorizeRouteView` never enforced them; making them routes later would newly enforce them.
- **Re-check on authentication change.** With an `AuthenticationStateProvider` registered, the router subscribes to
  `AuthenticationStateChanged` while mounted. On an event it re-runs only the `IAuthorizationRouteGuard`s of the committed
  chain (parent first; `AuthorizeAttributeGuard` is one, an application guard that reads the authentication state should
  implement the marker too) and reloads the current URL (trigger `Reload`, loaders by their policy) only when a level's
  outcome changed: a sign-out on a protected page commits `NotAuthorized` (the fragment renders, a `RedirectToLogin` in it
  navigates once), a sign-in on a not-authorized page shows the page with its data, a token refresh reloads nothing. An event
  during a navigation is deferred until it commits or ends; checks are serialized with at most one follow-up; an ordinary
  guard's denial never causes a reload. Without a provider the feature is off.
- **Missing services.** A route that needs authorization fails its first evaluation with an `InvalidOperationException`
  naming what to register (`services.AddAuthorizationCore()` and the application's `AuthenticationStateProvider`, as every
  Blazor WebAssembly app with authentication already does); an application without authorized routes needs neither.
- The library references `Microsoft.AspNetCore.Components.Authorization` (shared framework); only `Authorization/` and
  `Compatibility/` use it (`ArchitectureTests`). `Core/` knows nothing of it.

