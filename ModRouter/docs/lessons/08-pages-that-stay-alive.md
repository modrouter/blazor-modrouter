# Lesson 8: Pages that stay alive

Keep a page and its state while the visitor is elsewhere, and cross-fade between pages. Back to the [guided lessons](../lessons.md).

**Live demo:** [open lesson 8 in the lab](https://modrouter.github.io/blazor-modrouter/lab/lessons/alive)

## See it

1. Open **Reactor**. It counts, and the core pulses.
2. Type a note in **Hangar**, then open **Observatory** and type a note there too.
3. Go back to the Reactor. The count carried on from where it was, and the page says it was *paused* while you were away.
4. Go back to Hangar: your note is still there. Go back to the Observatory: its note is gone, because that page is not kept alive.
5. Notice the fade as the pages change. That is a view transition, turned on once in `Program.cs`.

## Read it

Normally a page is thrown away when the visitor leaves it, and a new one is built when they return. A route that declares `KeepAlive` keeps its page instead. The component stays rendered but hidden, with all its state, and the same instance is shown again on return:

```csharp
// ModRouter.Lab/LabRoutes.cs
.Route("reactor", typeof(ReactorPage), name: "reactor", title: "Reactor").KeepAlive(3)
.Route("hangar", typeof(HangarPage), name: "hangar", title: "Hangar").KeepAlive(3)
.Route("observatory", typeof(ObservatoryPage), name: "observatory", title: "Observatory"));
```

The lab keeps two of the three on purpose. The note in Hangar survives; the note in Observatory does not. `3` is how many hidden pages of that route the outlet keeps (`/reports/1` and `/reports/2` would be two separate kept pages). Past the limit, the least recently shown is dropped and disposed.

### A hidden page keeps running

Hidden is not paused. A timer or subscription in a kept page carries on unless you stop it. A page that does work implements `IKeepAliveRoute` to learn when it is hidden and shown again:

```csharp
// ModRouter.Lab/Lessons/L8/ReactorPage.razor.cs
public ValueTask OnDeactivatedAsync()
{
    _paused = true;
    _pauses++;
    _timer?.Dispose();
    _timer = null;

    return ValueTask.CompletedTask;
}

public ValueTask OnActivatedAsync()
{
    _paused = false;
    Start();
    StateHasChanged();

    return ValueTask.CompletedTask;
}
```

`OnDeactivatedAsync` runs when the page is hidden, and `OnActivatedAsync` when it is shown again. It is not called the first time the page is shown, and not when the instance is dropped (it is disposed instead).

### Where the pages are kept

The kept pages live in the outlet that renders them, for as long as that outlet lives. In the lab that is the outlet of the lesson page, which stays while you move between the three tabs. Leaving lesson 8 drops them. So put `KeepAlive` on routes whose outlet stays: the router's own layout, or a parent route common to the pages you move between.

Returning to a kept page is a navigation like any other. Its loader runs, or is answered by the cache, and the page receives the new data while it keeps the rest of its state.

### Fading between pages

```csharp
// ModRouter.Lab/Program.cs
options.ViewTransitions.Enabled = true;                   // cross-fade between pages (lesson 8)
```

With this on, a navigation that changes the page runs inside the browser's view transition: the browser holds a picture of the old page while the new one renders, then fades to it. You can style the fade in CSS with `::view-transition-old(root)` and `::view-transition-new(root)`; the lab shortens it to a quick cross-fade. The router skips the fade for the first load, for a change of query or fragment only, for an error page, in a browser without the feature, and for visitors who ask for reduced motion.

## Your turn

1. Turn the Hangar into one page per bay: change its route to `hangar/{bay:int}`, add `[Parameter] public int Bay { get; set; }` to `HangarPage`, and use `.KeepAlive(1)`. Open bay 1 and type a note, then open bay 2, bay 3, and bay 1 again. The note is gone. Why? Each bay is its own kept page, and the route keeps only one hidden page, so the least recently shown (bay 1) was dropped and disposed.
2. Make the Observatory kept alive too, then watch its counter of instances in the dashed frames.
3. Make the reactor keep counting while it is hidden. Which of the two interface methods no longer has any work to do?
4. Try turning `ViewTransitions.Enabled` off and compare the page change.

## What you learned

- `KeepAlive(n)` keeps up to `n` hidden pages per route, with their state, in the outlet that shows them.
- A hidden page keeps running. `IKeepAliveRoute` tells it when to pause and resume.
- Kept pages live as long as their outlet, so declare `KeepAlive` where the outlet stays.
- View transitions cross-fade pages and are turned on in the options.

Next: [Lesson 9, The flight recorder](09-the-flight-recorder.md).
