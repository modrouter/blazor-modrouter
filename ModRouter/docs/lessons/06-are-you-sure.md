# Lesson 6: Are you sure?

Ask before the visitor leaves a page with unsaved work. Back to the [guided lessons](../lessons.md).

**Live demo:** [open lesson 6 in the lab](https://modrouter.github.io/blazor-modrouter/lab/lessons/leave)

## See it

1. Type something in the log. The editor starts to glow: there is unsaved work.
2. Click another lesson on the line, or open the archive. A question appears before anything moves.
3. Answer **Stay on this page**. The page, the address and your text are exactly as they were.
4. Ask again and answer **Leave**. The navigation goes on.
5. Press **Save** first, then leave. With nothing unsaved the guard does not ask.

Look at the strip after you answer **Stay**: the **LeaveGuard** light turns rose, because a leave guard cancelled the navigation. It runs *before* the redirect rules and the guards of the page you are going to.

## Read it

A **leave guard** is the mirror image of a guard. A guard is asked about the page you are going *to*. A leave guard is asked about the page that is on screen, *before it goes away*:

```csharp
// ModRouter.Lab/Lessons/L6/UnsavedDraftGuard.cs
public sealed class UnsavedDraftGuard(Draft draft, ConfirmDialog dialog) : IRouteLeaveGuard
{
    public async ValueTask<RouteLeaveResult> CanDeactivateAsync(RouteLeaveContext context)
    {
        if (!draft.IsDirty)
        {
            return RouteLeaveResult.Allow();
        }

        // Pass the token: if the user clicks somewhere else while the question is open, that newer navigation cancels this one.
        var leave = await dialog.AskAsync("This log has changes that are not saved. Leave anyway?", context.CancellationToken);

        if (leave)
        {
            draft.Discard();
        }

        return leave ? RouteLeaveResult.Allow() : RouteLeaveResult.Cancel();
    }
}
```

Like a guard, it can await. While the question is open, the old page and address stay and the navigation counts as running. It answers `Allow()`, `Cancel()`, or `Redirect(...)`, for example to a drafts page.

You attach it the same way as a guard, to the route whose page it protects:

```csharp
// ModRouter.Lab/LabRoutes.cs
.Index(typeof(LogEditor), name: "log-editor").LeaveGuard<UnsavedDraftGuard>()
```

### How the guard sees the page's state

The guard is a separate class, so it cannot read the page's fields. The lab shares the state through a service: the editor writes the text into `Draft`, and the guard reads `draft.IsDirty`. That is the pattern the router recommends: a scoped service the page fills in and the guard reads.

### The dialog is just a service

Nothing in ModRouter draws the question. The lab's `ConfirmDialog` is an ordinary service with one method, `AskAsync`, that completes when the visitor answers. A component in the layout shows it. If a newer navigation cancels the guard (the visitor clicks elsewhere), the token is canceled and the dialog closes.

```csharp
// ModRouter.Lab/Services/ConfirmDialog.cs
public async ValueTask<bool> AskAsync(string question, CancellationToken cancellationToken)
```

### When it is asked, and when it is not

A level is *left* when the target does not show the same route at that depth: another page, a sibling, a parent, or an address that matches nothing. It is **not** left when only the values change on the same route, or when a child opens. A leave guard also does not run for the first load, for a reload, or when the visitor leaves the app (closing the tab). For that last case, use a `NavigationLock` with `ConfirmExternalNavigation` for the browser's own prompt. See [Navigation](../navigation.md).

A page can also guard itself without a service by implementing `IRouteNavigationLock`. It is asked in the same cases, and also when the same route stays but its values change.

## Your turn

1. Change the guard to redirect instead of cancel when the visitor answers **Stay**: `RouteLeaveResult.Redirect("log-archive")`. What does the strip say now?
2. Make the guard skip the question when the text is empty by adding that condition to `Draft.IsDirty`.
3. Add a second leave-guarded route, for example a settings page that reuses `ConfirmDialog`, and check that its guard and this one never ask twice.

## What you learned

- A leave guard is asked about the page on screen, before it goes away.
- It can ask the visitor, and answer allow, cancel or redirect.
- It reads the page's state through a service, and the dialog is an ordinary service too.
- It runs first, before redirect rules and the guards of the target.

Next: [Lesson 7, Shared layouts and side panels](07-shared-layouts-and-side-panels.md).
