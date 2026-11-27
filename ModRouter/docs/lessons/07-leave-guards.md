# Lesson 7: Leave guards

Ask before the visitor leaves a page with unsaved work. Back to the [guided lessons](../lessons.md).

**Live demo:** the [sample CRM](https://modrouter.github.io/blazor-modrouter/crm/), chapter 7, Leave guards

## See it

1. Click **Edit account 3**. An edit form opens.
2. Change the name. Now there is unsaved work.
3. Click any link: the nav, a chapter, the browser's Back button. A question appears before anything moves.
4. Choose **Keep editing**. The page, the address and your text are exactly as they were.
5. Ask again and choose **Discard**. The navigation goes on.
6. Save first, then leave. With nothing unsaved the guard does not ask.

In the route log, a canceled navigation shows as canceled, with no new page. The leave guard stage runs first, before redirects and the guards of the page you are going to.

## Read it

A **leave guard** is the mirror image of a guard. A guard is asked about the page you are going *to*. A leave guard is asked about the page that is on screen, *before it goes away*:

```csharp
// ModRouter.Crm/Crm/Routing/RoutingClasses.cs
public sealed class UnsavedChangesGuard(EditDraft draft, Ui.DialogService dialogs) : IRouteLeaveGuard
public async ValueTask<RouteLeaveResult> CanDeactivateAsync(RouteLeaveContext context)
if (!draft.IsDirty)
return RouteLeaveResult.Allow();
var leave = await dialogs.ConfirmAsync(
return leave ? RouteLeaveResult.Allow() : RouteLeaveResult.Cancel();
```

Like a guard, it can await. While the question is open, the old page and address stay and the navigation counts as running. It answers `Allow()`, `Cancel()` or `Redirect(...)`, for example to a drafts page.

You attach it to the route whose page it protects:

```csharp
// ModRouter.Crm/Crm/CrmRoutes.cs
.Route("edit", typeof(AccountEdit), name: "account-edit", title: "Edit").LeaveGuard<UnsavedChangesGuard>())
```

### How the guard sees the page's state

The guard is a separate class, so it cannot read the page's fields. The CRM shares the state through a service: the edit page fills in `EditDraft`, and the guard reads `IsDirty`:

```csharp
// ModRouter.Crm/Data/Services.cs
public bool IsDirty => AccountId != 0 && Name != _saved;
```

### The dialog is just a service

Nothing in ModRouter draws the question. The CRM's `DialogService` is an ordinary service with one method, `ConfirmAsync`, that completes when the visitor answers. A component in the shell shows it.

### When it is asked, and when it is not

A level is *left* when the target does not show the same route at that depth: another page, a sibling, a parent, or an address that matches nothing. It is **not** left when only the values change on the same route (edit account 3, then edit account 5), or when a child opens. A leave guard also does not run for the first load, for a reload, or when the visitor leaves the app by closing the tab. For that last case, use a `NavigationLock` with `ConfirmExternalNavigation` for the browser's own prompt. See [Navigation](../navigation.md).

The edit page needs the case in the middle: moving from one account's form to another's is the same route, so the route's guard is not asked. A page can guard itself without a service by implementing `IRouteNavigationLock`. It is asked in the same cases as a leave guard, and also when the same route stays but its values change:

```csharp
// ModRouter.Crm/Crm/Pages/AccountEdit.razor.cs
public ValueTask<RouteLeaveResult> CanLeaveAsync(RouteLeaveContext context)
=> context.IsParameterChange ? new UnsavedChangesGuard(Draft, Dialogs).CanDeactivateAsync(context) : ValueTask.FromResult(RouteLeaveResult.Allow());
```

The page asks only for a parameter change and leaves a real leave to the route's guard, so the visitor sees the question once.

## Your turn

1. Change `RouteLeaveResult.Cancel()` to `RouteLeaveResult.Redirect("accounts")` for the **Keep editing** answer. What does the route log say now?
2. Make `IsDirty` ignore a name that only differs in case or spacing.
3. Put a leave guard on the Contacts tab of an account, reusing `DialogService`, and check that this guard and the edit page's never ask twice.

## What you learned

- A leave guard is asked about the page on screen, before it goes away.
- It can ask the visitor, and answer allow, cancel or redirect.
- It reads the page's state through a service, and the dialog is an ordinary service too.
- It is not asked for a same-route change of values; `IRouteNavigationLock` covers that case.

Next: [Lesson 8, Names and redirects](08-names-and-redirects.md).
