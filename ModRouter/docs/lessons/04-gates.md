# Lesson 4: Gates

Stop, redirect or refuse a navigation before the page appears, and find out how it ended. Back to the [guided lessons](../lessons.md).

**Live demo:** [open lesson 4 in the lab](https://modrouter.github.io/blazor-modrouter/lab/lessons/guards)

## See it

1. You start as a **visitor**. Open the **Bridge**. A guard sends you to the dock and remembers where you were going.
2. On the dock, become **crew**. You are taken on to the bridge you asked for.
3. Open the **Vault**. The navigation goes through, but the page is replaced by "gate closed", and the address changes.
4. Press the **Airlock**. It always says no: nothing moves, not even the address.
5. Open **Old bridge**. It is a redirect rule, not a page. It forwards to the bridge.

The line under the gates shows what `NavigateToAsync` returned. Look at the pipeline lights at the top as well: a redirect turns the **Guard** light amber, and a refusal turns it rose.

## Read it

A **guard** is a small class that runs before the page appears and answers one question: may this navigation go on? It is created from the navigation's services, so it can take what it needs in its constructor. This one lets crew and captains through and sends everyone else to the dock:

```csharp
// ModRouter.Lab/Lessons/L4/Gates.cs
public sealed class CrewOnlyGuard(Clearance clearance) : IRouteGuard
{
    public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context)
    {
        if (clearance.Current >= Level.Crew)
        {
            return ValueTask.FromResult(RouteGuardResult.Allow());
        }

        // Redirect: the navigation never commits here. The router starts a new one for the dock.
        var wanted = new Uri(context.To).PathAndQuery;

        return ValueTask.FromResult(RouteGuardResult.RedirectToRoute("gate-dock", query: new Dictionary<string, object?> { ["returnUrl"] = wanted }));
    }
}
```

A guard answers with one of four results:

| Result | What happens |
|---|---|
| `Allow()` | The navigation goes on. |
| `Cancel()` | The old page and address stay, for a link, `NavigateTo`, and Back and Forward alike. |
| `Redirect(url)`, `RedirectToRoute(name, ...)` | A new navigation starts for the target instead. |
| `NotAuthorized(message)` | The navigation commits, but the denied level shows "not authorized" content. |

The other two guards of the lesson use the other results:

```csharp
// ModRouter.Lab/Lessons/L4/Gates.cs
? RouteGuardResult.Allow()
: RouteGuardResult.NotAuthorized("The vault only opens for a captain."));
```

```csharp
// ModRouter.Lab/Lessons/L4/Gates.cs
=> ValueTask.FromResult(RouteGuardResult.Cancel());
```

### Attaching guards and redirect rules

A guard belongs to the route declared just before it. A redirect rule needs no component: it forwards an address to a named route.

```csharp
// ModRouter.Lab/LabRoutes.cs
.Route("dock", typeof(DockPage), name: "gate-dock", title: "Dock")
.Route("bridge", typeof(BridgePage), name: "gate-bridge", title: "Bridge").Guard<CrewOnlyGuard>()
.Route("vault", typeof(VaultPage), name: "gate-vault", title: "Vault").Guard<CaptainOnlyGuard>()
.Route("airlock", typeof(AirlockPage), name: "gate-airlock", title: "Airlock").Guard<SealedGuard>()
.Redirect("old-bridge", "gate-bridge"));
```

Guards run parent first, one level at a time, so a guard on a parent protects every child. A guard is also where a "return to where you were going" flow starts: the dock page reads `returnUrl` from the query and navigates there after sign-in.

### "Gate closed" is content, not an error page

`NotAuthorized` does not stop the navigation. The address changes, the levels above the denied one stay on screen, and the denied level shows content you supply. That content is a fragment of the router in `App.razor`:

```razor
@* ModRouter.Lab/App.razor *@
<NotAuthorized Context="error">
    <section class="notice notice-deny">
        <h1>Gate closed</h1>
        <p>@error.Message</p>
        <button class="btn" @onclick="error.Retry">Check clearance again</button>
    </section>
</NotAuthorized>
```

### Finding out how a navigation ended

`NavigateToAsync` navigates like `NavigateTo` and tells you what happened, so code that navigates after a save can tell whether it landed:

```csharp
// ModRouter.Lab/Lessons/L4/Lesson4Page.razor.cs
=> Last = await Navigation.NavigateToRouteAsync(routeName);
```

| Status | What happened |
|---|---|
| `Succeeded` | The page is shown. |
| `Redirected` | A guard or rule sent the visitor elsewhere, and `RedirectedTo` says where. |
| `NotAuthorized` | A guard denied a level. The address changed. |
| `Cancelled` | A guard said no. Nothing changed. |
| `NotFound`, `Failed`, `Superseded` | No route matched; a guard or loader threw; a newer navigation replaced this one. |

See [Navigation](../navigation.md) for the full list.

## Your turn

1. Make the vault accept crew too. Change `CaptainOnlyGuard` so `clearance.Current >= Level.Crew` passes, and try it as a visitor, then as crew.
2. Protect all three pages of lesson 7's console at once by putting `.Guard<CrewOnlyGuard>()` after the group in `LabRoutes.cs`. One guard now covers every route in the group. Remember that guards run for every route the group holds, so the guard's redirect target must not be inside the group itself.
3. Add a redirect rule `.Redirect("sealed", "gate-airlock")` and open `/lessons/guards/sealed`. What does `NavigateToAsync` return now?

## What you learned

- A guard runs before the page appears and decides: allow, cancel, redirect or not authorized.
- A guard on a parent protects its children, and a redirect rule forwards an address without a component.
- "Not authorized" commits the navigation and shows content in place of the page.
- `NavigateToAsync` reports how the navigation ended.

Next: [Lesson 5, Data in flight](05-data-in-flight.md).
