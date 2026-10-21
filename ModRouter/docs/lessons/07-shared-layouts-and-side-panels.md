# Lesson 7: Shared layouts and side panels

Share one layout between routes with different paths, and fill a side panel from the route. Back to the [guided lessons](../lessons.md).

**Live demo:** [open lesson 7 in the lab](https://modrouter.github.io/blazor-modrouter/lab/lessons/layout)

## See it

1. Open **Radar**, **Comms** and **Power**. They share one console, and the frame labelled `ConsoleLayout` never changes: it keeps the same instance and its counter stays at 1.
2. Watch the right-hand panel. Radar and Comms each fill it with their own inspector. Power declares none, so the panel shows its empty state.
3. Look at the chain above. The console is a level of its own, but it adds nothing to the address.

## Read it

Lesson 1 shared a layout by nesting: a child's address starts with its parent's. Sometimes pages that should share a layout have no common start. A **group** shares a layout (or a guard, or a loader) between routes without adding anything to their addresses:

```csharp
// ModRouter.Lab/LabRoutes.cs
.Group(typeof(ConsoleLayout), console => console
    .Route("radar", typeof(RadarPage), name: "radar", title: "Radar").Slot("side", typeof(RadarInspector))
    .Route("comms", typeof(CommsPage), name: "comms", title: "Comms").Slot("side", typeof(CommsInspector))
    .Route("power", typeof(PowerPage), name: "power", title: "Power")));
```

A group is a level of the chain (look at the strip: `l7`, `group`, `radar`), so its component is a layout with its own outlet. It is never matched by itself, and it has no name, no aliases and no title.

### A layout with two outlets

The group's component places the page's outlet. It can also place a **named outlet**, a second place a route can render into:

```razor
@* ModRouter.Lab/Lessons/L7/ConsoleLayout.razor *@
<section class="console-main" aria-label="Page">
    <ModRouterOutlet />
</section>
<aside class="console-side" aria-label="Inspector">
    <ModRouterOutlet Name="side">
```

### Filling the side panel

A route fills a named outlet with a **slot**: the component to show there for that route.

```csharp
// ModRouter.Lab/LabRoutes.cs
.Route("radar", typeof(RadarPage), name: "radar", title: "Radar").Slot("side", typeof(RadarInspector))
```

The slot component is rendered inside the route's loaded data, and its route values are bound like the page's, so an inspector can show the details of the item in the address. When no route in the chain declares a slot of that name, the outlet shows its `EmptyContent`:

```razor
@* ModRouter.Lab/Lessons/L7/ConsoleLayout.razor *@
<p class="dim">No inspector for this page. Nothing declared a <code>side</code> slot here.</p>
```

A slot is part of the page's route, not the address. A link cannot pick "this inspector with that page" on its own, and two slots do not navigate separately. If you need addresses that open several panels independently, ModRouter does not support that (see [known limitations](../known-limitations.md)).

### Why the layout survives

Opening Comms after Radar changes only the deepest level, so `ConsoleLayout` is updated, not rebuilt, just like the ship page in lesson 1. A group is the way to get that behavior across unrelated addresses.

## Your turn

1. Add a fourth console page, **Cargo**, with its own inspector. It needs a component, a `.Route(...)` line with a `.Slot("side", ...)`, and a tab in `ConsoleLayout.razor`.
2. Put a guard on the whole group: add `.Guard<CrewOnlyGuard>()` right after the group's closing parenthesis. Visit the console as a visitor (lesson 4 sets your clearance) and watch the redirect.
3. Move the default page into the group. Delete the lesson's `.Index(typeof(L7Intro), name: "console-start")` line and add it inside the group instead. The intro now appears inside the console, with its tabs and side panel around it, because an index route in a group is the default page of the group's parent address.

## What you learned

- A group shares a layout, guard or loader between routes without changing their addresses.
- A layout can have named outlets next to the page's own outlet.
- A route fills a named outlet with `.Slot(name, component)`, and the outlet shows empty content when none does.
- A shared layout survives while only its child changes.

Next: [Lesson 8, Pages that stay alive](08-pages-that-stay-alive.md).
