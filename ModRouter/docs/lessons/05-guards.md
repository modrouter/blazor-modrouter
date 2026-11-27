# Lesson 5: Guards

Stop, redirect or refuse a navigation before the page appears, and find out how it ended. Back to the [guided lessons](../lessons.md).

**Live demo:** the [sample CRM](https://modrouter.github.io/blazor-modrouter/crm/), chapter 5, Guards

## See it

1. Click **Sign out** in the guide. The role picker in the app bar appears.
2. Click **Open settings**. A guard sends you to the sign-in page, and remembers where you were going.
3. Sign in as a sales rep. You are taken on to Settings.
4. Open **Billing**. The address changes, but the page is replaced by "Not allowed".
5. Sign in as an admin from the app bar. Billing opens.

In the route log, a redirect shows as two navigations, and the refused page shows as shown with a not-allowed level.

## Read it

A **guard** is a small class that runs before the page appears and answers one question: may this navigation go on? It is created with the app's services, so it takes what it needs in its constructor. This one lets any signed-in visitor through and sends everyone else to the sign-in page:

```csharp
// ModRouter.Crm/Crm/Routing/RoutingClasses.cs
public sealed class SignedInGuard(Session session) : IRouteGuard
if (session.Role != Role.SignedOut)
return ValueTask.FromResult(RouteGuardResult.Allow());
var wanted = new Uri(context.To).PathAndQuery;
return ValueTask.FromResult(RouteGuardResult.RedirectToRoute("sign-in", query: new Dictionary<string, object?> { ["returnUrl"] = wanted }));
```

A guard answers with one of four results:

| Result | What happens |
|---|---|
| `Allow()` | The navigation goes on. |
| `Cancel()` | The old page and address stay. |
| `Redirect(url)`, `RedirectToRoute(name, ...)` | A new navigation starts for the target instead. |
| `NotAuthorized(message)` | The navigation commits, but the denied level shows "not authorized" content. |

The billing guard uses the last one. A signed-in rep is not sent anywhere; they are told why:

```csharp
// ModRouter.Crm/Crm/Routing/RoutingClasses.cs
public sealed class AdminOnlyGuard(Session session) : IRouteGuard
? RouteGuardResult.Allow()
: RouteGuardResult.NotAuthorized("Billing is only open to admins. Switch to the Admin role in the top bar to see it."));
```

### Attaching guards

A guard belongs to the route declared just before it. Guards run parent first, so a guard on a parent covers every child. Billing adds its own, stricter guard on top of Settings':

```csharp
// ModRouter.Crm/Crm/CrmRoutes.cs
.Route("billing", typeof(SettingsBilling), name: "settings-billing", title: "Billing").Guard<AdminOnlyGuard>())
.Guard<SignedInGuard>()
```

### The way back

The guard puts the address the visitor wanted in the query string of the sign-in page. The sign-in page reads it, and goes there after sign-in:

```csharp
// ModRouter.Crm/Crm/Pages/SignInPage.razor.cs
[Parameter, SupplyParameterFromQuery(Name = "returnUrl")]
public string? ReturnUrl { get; set; }
```

It only follows a path inside the app, so a crafted link cannot send a visitor to another site.

### "Not allowed" is content, not an error page

`NotAuthorized` does not stop the navigation. The address changes, the levels above the denied one stay on screen, and the denied level shows what you declared in the root component:

```razor
@* ModRouter.Crm/App.razor *@
<NotAuthorized Context="error">
```

### Finding out how a navigation ended

`NavigateToAsync` and `NavigateToRouteAsync` navigate like `NavigateTo` and say what happened, so code that navigates after a save can tell whether it landed:

| Status | What happened |
|---|---|
| `Succeeded` | The page is shown. |
| `Redirected` | A guard or rule sent the visitor elsewhere, and `RedirectedTo` says where. |
| `NotAuthorized` | A guard denied a level. The address changed. |
| `Cancelled` | A guard said no. Nothing changed. |
| `NotFound`, `Failed`, `Superseded` | No route matched; a guard or loader threw; a newer navigation replaced this one. |

See [Navigation](../navigation.md) and [Authorization](../authorization.md) for the full list.

## Your turn

1. Let reps open Billing too, but only to read: change `AdminOnlyGuard` to allow `Role.Rep` as well, and sign in as a rep.
2. Put `.Guard<SignedInGuard>()` on the `accounts` route. Sign out and open an account: the guard now covers every child. Where does the visitor land after signing in?
3. Make a guard that cancels: return `RouteGuardResult.Cancel()` for one address, and watch the route log say canceled.

## What you learned

- A guard runs before the page appears and decides: allow, cancel, redirect or not authorized.
- A guard on a parent protects its children, and a child can add a stricter guard.
- A redirect carries a return address; "not authorized" commits and shows content in place of the page.
- `NavigateToAsync` reports how the navigation ended.

Next: [Lesson 6, Loaders](06-loaders.md).
