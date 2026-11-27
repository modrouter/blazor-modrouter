# Lesson 3: Nested routes

Put a page inside a page, and see which parts of the screen survive a click. Back to the [guided lessons](../lessons.md).

**Live demo:** the [sample CRM](https://modrouter.github.io/blazor-modrouter/crm/), chapter 3, Nested routes

## See it

1. Open **Account 3, overview**. Tabs appear: Overview, Contacts, Activity.
2. Click **Contacts**, then **Activity**. The account's header stays put, and only the part under the tabs changes.
3. Switch to another account from the list. The account page is updated in place with the new `id`; it is not rebuilt.
4. In the route log, read the levels: one for each route that matched.

## Read it

The tabs of an account are **child routes** of the account. Their templates are relative to the parent, so `contacts` under `accounts/{id}` means `/accounts/3/contacts`:

```csharp
// ModRouter.Crm/Crm/CrmRoutes.cs
.Index(typeof(AccountOverview), name: "account-overview")
.Route("contacts", typeof(AccountContactsTab), name: "account-contacts", title: "Contacts")
.Route("activity", typeof(AccountActivityTab), name: "account-activity", title: "Activity").Loader<ActivityLoader>()
```

(The loader on the last line is [lesson 6](06-loaders.md).)

`Index` is the default child. It shows at the parent's own address, `/accounts/3`, when nothing more is in the address.

The lists nest too. `accounts` is a route of its own, with the list as its index and each account as a child. It renders a component that does nothing except place an outlet:

```razor
@* ModRouter.Crm/Crm/Pages/Section.razor *@
<ModRouterOutlet />
```

That is why `/accounts/3` and `/accounts` are different levels of one chain, and why the breadcrumb can read Dashboard, Accounts, then the account's name.

### The chain

The address `/accounts/3/contacts` matches four routes at once. That list is the **chain**, and each match is a **level**:

| Level | Route name | Component | Value |
|---|---|---|---|
| 0 | group | `CrmLayout` | |
| 1 | `accounts` | `Section` | |
| 2 | `account` | `AccountPage` | `id = 3` |
| 3 | `account-contacts` | `AccountContactsTab` | |

Every level is a component. A parent decides where its child appears by placing an outlet. The account page places the tabs and then the outlet:

```razor
@* ModRouter.Crm/Crm/Pages/AccountPage.razor *@
<RouteLink Route="account-overview" Parameters="@(new { id = View.Account.Id })" Match="RouteLinkMatch.Exact" Class="crm-tab">Overview</RouteLink>
<ModRouterOutlet />
```

`Match="RouteLinkMatch.Exact"` keeps the Overview tab from lighting up on every tab, because its address is the start of the others.

### Why the parent survives

When you click from Contacts to Activity, only the deepest level changes. ModRouter keeps the account page it already has and swaps the child. So a parent can hold state, such as a loaded account or a scroll position, without losing it. A child also receives what its parent shares. The account page cascades what its loader returned, and every tab reads it:

```csharp
// ModRouter.Crm/Crm/Pages/AccountEdit.razor.cs
[CascadingParameter]
AccountView View { get; set; } = default!;
```

## Your turn

1. Add a **Notes** tab: a component `AccountNotesTab`, one `.Route("notes", ...)` line next to the other children, and a `RouteLink` for it in `AccountPage.razor`.
2. Open an account, click **Notes**, and look at the route log. The chain has one more node, and the account header did not flicker.
3. Move the `Notes` line outside `children:` so it sits under `accounts` instead. What changes in the chain, and in the breadcrumb?

## What you learned

- A route can have children, and a child renders in its parent's outlet.
- The address decides the chain; each level is one route.
- A parent stays on screen when only a child changes.
- Nesting a list and its detail pages gives the breadcrumb its trail.

Next: [Lesson 4, The query string](04-the-query-string.md).
