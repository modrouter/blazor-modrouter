# Lesson 8: Names and redirects

Link by name so an address can change without breaking anything, and forward old addresses. Back to the [guided lessons](../lessons.md).

**Live demo:** the [sample CRM](https://modrouter.github.io/blazor-modrouter/crm/), chapter 8, Names and redirects

## See it

1. Type the old address `/clients/3` into the address bar. It ends up at `/accounts/3`, and the route log says it was a redirect, with where it came from.
2. Open **Account 3, activity**. The breadcrumb above the page reads Dashboard, Accounts, the account's name, Activity.
3. Open **a page that does not exist**. No route matches, and the not-found page shows.

## Read it

Every route in the CRM has a name. Links ask for a name and the router builds the address, so the nav links never contain a path:

```razor
@* ModRouter.Crm/Crm/CrmLayout.razor *@
<li><RouteLink Route="accounts" Class="crm-nav-link"><UiIcon Icon="@Icons.Outlined.Building" />Accounts</RouteLink></li>
```

Rename `accounts` to `customers` in the route table and no link breaks, because no link says `/accounts`. The router also marks the link of the current page.

The same is true from code. `NavigateToRouteAsync("account", new { id = 3 })` builds the address from the name and the values and goes there. Unlike `TryGetRouteUrl`, which returns false and an explanation, it throws when the address cannot be built, so a mistake is loud:

```csharp
Navigation.NavigateToRouteAsync("account", new { id = 3 });
Navigation.TryGetRouteUrl("account", new { id = "abc" }, out var url, out var error);   // false; error names the rule that failed
```

The address includes the app's base path. The CRM is served from `/blazor-modrouter/crm/` on the live site and from `/` on your machine, and the same code works in both places, because the router adds the base path when it builds the address. That is the reason a hand-typed `"/accounts"` is a bug: the leading slash leaves the base path.

### Redirects

A redirect rule needs no component. It forwards an old address to a named route, and its values follow:

```csharp
// ModRouter.Crm/Crm/CrmRoutes.cs
.Redirect("clients/{id:int}", "account"));
```

`/clients/3` becomes `/accounts/3`. The visitor's bookmark still works, and the route log records the redirect with where it came from. `/clients/abc` does not match the rule, so it falls to not-found.

### The breadcrumb reads the chain

The breadcrumb is not a list you maintain. It reads the matched chain of levels, and takes each label from the route's title, which is why [lesson 2](02-parameters.md) made the title a function of the value:

```razor
@* ModRouter.Crm/Crm/CrmLayout.razor *@
<RouteBreadcrumb Home="Dashboard" Class="crm-crumbs mb-3" />
```

Move a page to another place in the table and its breadcrumb follows.

### Typed names

For pages declared with `@page` and attributes, a source generator writes a method per route into your project, `NavigateToAccountAsync(int id)` and `GetAccountUrl(int id)`, so a wrong value type does not compile. See [Typed routes](../typed-routes.md) and [Route sources](../route-sources.md).

## Your turn

1. Add `.Redirect("customers/{id:int}", "account")` and open `/customers/5`.
2. Rename the route `account` to `account-detail` in `CrmRoutes.cs` and fix the compiler or startup errors you get. Notice which files needed the name: only the ones that asked for it.
3. Add a link to the dashboard that names `account-activity` with `id = 2`, and check the address it builds.

## What you learned

- A named route turns "go to this address" into "go to this route", and links built from names never go stale.
- The router adds the base path when it builds an address.
- A redirect rule forwards an old address to a named route, and its values follow.
- The breadcrumb reads the route chain and each route's title.

Next: [Lesson 9, The route log](09-the-route-log.md).
