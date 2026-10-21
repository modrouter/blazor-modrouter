# Lesson 1: Pages inside pages

Nest routes so a ship page can live inside a fleet page, and see which parts of the screen survive a click. Back to the [guided lessons](../lessons.md).

**Live demo:** [open lesson 1 in the lab](https://modrouter.github.io/blazor-modrouter/lab/lessons/routes)

## See it

1. Open **Fleet**, then a ship. Watch the strip at the top: the chain gains one node for each level of the route.
2. Click **Crew** and **Log** on the ship. Only the part inside the ship changes. The dashed frames around it keep the same *instance*, and their *updated* counters do not move.
3. Switch to another ship. The ship page is not rebuilt. It is updated in place with the new `id`.
4. Press the browser's Back button. Every step you took is in the history.

## Read it

All the routes of the lab live in one file. Lesson 1 declares a route with children, and those children have children:

```csharp
// ModRouter.Lab/LabRoutes.cs
routes.Route("/lessons/routes", typeof(Lesson1Page), name: "l1", title: "Pages inside pages", children: lesson => lesson
    .Index(typeof(L1Welcome), name: "hangar-start")
    .Route("fleet", typeof(FleetLayout), name: "fleet", title: "Fleet", children: fleet => fleet
        .Index(typeof(FleetList), name: "fleet-list")
        .Route("{id:int}", typeof(ShipPage), p => $"Ship {p["id"]}", name: "ship", children: ship => ship
            .Index(typeof(ShipOverview), name: "ship-overview")
            .Route("crew", typeof(ShipCrew), name: "ship-crew", title: "Crew")
            .Route("log", typeof(ShipLog), name: "ship-log", title: "Log"))
        .Breadcrumb("Fleet")));
```

Three things to notice:

- **A child's template is relative.** `"fleet"` under `/lessons/routes` means `/lessons/routes/fleet`. A leading `/` on a child is an error.
- **`Index` is the default child.** It shows at the parent's own address. `ShipOverview` shows at `/lessons/routes/fleet/2`, the ship page with nothing chosen yet.
- **`{id:int}` is a placeholder with a rule.** It accepts only whole numbers. The title is a function of the values: `p => $"Ship {p["id"]}"`.

The address `/lessons/routes/fleet/2/crew` matches four routes at once. That list is the **chain**, and each match is a **level**:

| Level | Route name | Component | Value |
|---|---|---|---|
| 0 | `l1` | `Lesson1Page` | |
| 1 | `fleet` | `FleetLayout` | |
| 2 | `ship` | `ShipPage` | `id = 2` |
| 3 | `ship-crew` | `ShipCrew` | |

Every level is a component. A parent decides where its child appears by placing an outlet:

```razor
@* ModRouter.Lab/Lessons/L1/ShipPage.razor *@
<nav class="tabs" aria-label="Ship sections">
    <RouteLink Route="ship-overview" Parameters="@(new { id = Id })" Match="RouteLinkMatch.Exact" Class="tab">Overview</RouteLink>
    <RouteLink Route="ship-crew" Parameters="@(new { id = Id })" Class="tab">Crew</RouteLink>
    <RouteLink Route="ship-log" Parameters="@(new { id = Id })" Class="tab">Log</RouteLink>
</nav>
<div class="tab-body">
    <ModRouterOutlet />
</div>
```

The tabs are links to the three child routes, built from the route's name and a value, not typed by hand (lesson 2 goes deeper). The `ModRouterOutlet` is where whichever child matched appears.

A component asks for the route values it wants with `[Parameter]`:

```csharp
// ModRouter.Lab/Lessons/L1/ShipPage.razor.cs
[Parameter]
public int Id { get; set; }
```

`{id:int}` arrives as an `int`. A child, such as the crew tab, can declare `[Parameter] int Id` too: it receives the values of its ancestors.

### Why the parent survives

When you click from **Crew** to **Log**, only the deepest level changes. ModRouter keeps the `ShipPage` and the `FleetLayout` it already has and swaps just the child. This is what the *instance* and *updated* counters on the frames show. It also means a parent can hold state, such as a selected tab or a scroll position, without losing it when a child changes. When you switch from ship 2 to ship 3 the ship page is updated in place with the new `id` (`SetParametersAsync`); it is not recreated.

## Your turn

Add a **Cargo** tab to the ship.

1. Create `ModRouter.Lab/Lessons/L1/ShipCargo.razor` with a heading and a short list. Copy `ShipLog.razor` as a start.
2. Add one line next to the other ship children in `LabRoutes.cs`:

   ```csharp
   .Route("cargo", typeof(ShipCargo), name: "ship-cargo", title: "Cargo")
   ```

3. Add a `RouteLink` for `ship-cargo` to the tabs in `ShipPage.razor`.
4. Run the lab, open a ship and click **Cargo**. The chain now has a node named `ship-cargo`, and the ship page's counters still did not move.

## What you learned

- A route can have children, and a child renders in its parent's outlet.
- The address, not the component tree, decides the chain. Each level is one route.
- A parent stays on screen when only a child changes.
- `[Parameter]` receives route values, converted to the type the rule names.

Next: [Lesson 2, Names instead of URLs](02-names-instead-of-urls.md).
