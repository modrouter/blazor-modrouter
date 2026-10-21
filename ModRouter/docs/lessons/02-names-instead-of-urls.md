# Lesson 2: Names instead of URLs

Give routes names, build their addresses from values, and let the compiler check the call. Back to the [guided lessons](../lessons.md).

**Live demo:** [open lesson 2 in the lab](https://modrouter.github.io/blazor-modrouter/lab/lessons/names)

## See it

1. Pick a route name in the **URL forge** and fill in its values. The address is built from the name, never typed by hand.
2. Type `abc` where an `int` is expected. The router refuses and says why, before anything navigates.
3. Try **beacon**. Its second value is optional, so the address is shorter when you leave it empty.
4. Use the two launch buttons. They reach the same page. One passes a name and values; the other calls a method the compiler checks.

## Read it

Hand-typed addresses break quietly: a renamed path, a typo, a value in the wrong place. A **named route** lets you say where you want to go, and ModRouter builds the address.

Lesson 1 declared routes in code. Lesson 2 declares them on the pages themselves. A page states its address and its name with attributes on its class:

```csharp
// ModRouter.Lab/Lessons/L2/Lesson2Page.razor.cs
[Route("/lessons/names")]
[RouteName("l2")]
[RouteTitle("Names instead of URLs")]
```

`[Route]` is what `@page` compiles to. The lab writes it in the code-behind, not as `@page` in the `.razor` file, because the typed helpers further down are generated from these attributes, and the generator cannot see attributes that Razor itself generates (see [Typed routes](../typed-routes.md)). A child page nests under its parent with `[RouteParent]`, and its address carries a typed value:

```csharp
// ModRouter.Lab/Lessons/L2/L2Dock.razor.cs
[Route("/lessons/names/dock/{Id:int}")]
[RouteParent(typeof(Lesson2Page))]
[RouteName("dock")]
```

The router finds these pages when you ask it to, in `LabRoutes.cs`, next to the routes declared in code:

```csharp
// ModRouter.Lab/LabRoutes.cs
routes.AddPages(typeof(Program).Assembly);
```

### Building an address from a name

`TryGetRouteUrl` builds the address, or says why it cannot. It never throws and never navigates. This is what the forge calls on every keystroke:

```csharp
// ModRouter.Lab/Lessons/L2/Lesson2Page.razor.cs
if (Navigation.TryGetRouteUrl(Selected, values, out var url, out var error))
{
    Url = url;
    Error = null;
}
```

When it cannot, the error names the route, the value and the rule that failed, for example: *The value 'abc' for parameter 'Id' does not satisfy the constraint 'int' of route 'dock'.* `NavigateToRoute("dock", new { id = 3 })` builds the same address and goes there; unlike `TryGetRouteUrl`, it throws if the address cannot be built, so a mistake is loud, not silent.

The address also includes the app's base path. The lab is served from `/blazor-modrouter/lab/` on the live site and from `/` on your machine, and the same code works in both places.

### Optional values

A trailing `?` makes a value optional. The component must bind it to a nullable property:

```csharp
// ModRouter.Lab/Lessons/L2/L2Beacon.razor.cs
[Route("/lessons/names/beacon/{Code}/{Channel:int?}")]
public int? Channel { get; set; }
```

When the optional value is left out, it is left out of the address too: `/beacon/alpha`, or `/beacon/alpha/7`.

### Typed helpers

For a page that has both a route and a name, a source generator writes a method per route into your project. The second launch button uses one:

```csharp
// ModRouter.Lab/Lessons/L2/Lesson2Page.razor.cs
async Task LaunchTyped() => await Navigation.NavigateToDockAsync(4);
```

`NavigateToDockAsync` takes an `int` because the template says `{Id:int}`. Pass a string and the code does not compile. The generator also writes `GetDockUrl(int id)` and a constant, `RouteNames.Dock`. The generator only sees attribute pages, which is why this lesson uses them. See [Typed routes](../typed-routes.md).

## Your turn

1. Add a page: `Lessons/L2/L2Hangar.razor` for the markup and `L2Hangar.razor.cs` with a `partial class L2Hangar` carrying `[Route("/lessons/names/hangar/{Bay:int}")]`, `[RouteParent(typeof(Lesson2Page))]` and `[RouteName("hangar-bay")]`. Copy `L2Dock.razor.cs` as a start.
2. Build the project. A method `NavigateToHangarBayAsync(int bay)` now exists. Call it from a new button in `Lesson2Page.razor.cs`.
3. Give the new page the same name as an existing route, such as `"dock"`, and start the lab. It stops at startup with an `RT0010` error that names the duplicate. Fix it before moving on. This is the router refusing a mistake early.

## What you learned

- A named route turns "go to this address" into "go to this route".
- `TryGetRouteUrl` checks values against the route's rules and explains a refusal.
- Optional values are left out of the address when empty.
- Attribute pages get typed helpers the compiler checks.

Next: [Lesson 3, Which route wins?](03-which-route-wins.md).
