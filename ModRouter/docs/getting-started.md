# Getting started

Put ModRouter into a new Blazor WebAssembly app in about ten minutes, with a nested page and a named link. Back to the [README](../README.md).

You need the .NET 8, 9 or 10 SDK. ModRouter is a preview, so you ask NuGet for the pre-release.

## 1. Create an app

```bash
dotnet new blazorwasm -o Hello
cd Hello
```

The template comes with the built-in `Router` in `App.razor`. You will swap it for ModRouter.

## 2. Add the package

```bash
dotnet add package ModRouter --prerelease
```

This adds the [`ModRouter`](https://www.nuget.org/packages/ModRouter) package. To use a clone of the repository instead (to try a change of your own), add
`<ProjectReference Include="..\blazor-modrouter\ModRouter\ModRouter.csproj" />` to the project file.

## 3. Swap the router

In `App.razor`, replace the whole `<Router>` block:

```razor
<ModRouter DefaultLayout="typeof(MainLayout)">
    <NotFound>
        <h1>Not found</h1>
        <p>Sorry, there is nothing at this address.</p>
    </NotFound>
</ModRouter>
```

Add two lines to `_Imports.razor` so the components and the page attributes are in scope:

```razor
@using ModRouter.Components
@using ModRouter.Discovery
```

Then register the router in `Program.cs`, before `RunAsync`:

```csharp
using ModRouter;

// Register the router. AddPages finds every @page component in this assembly, so the template's pages keep working.
builder.Services.AddModRouter(routes => routes.AddPages(typeof(Program).Assembly));
```

Run the app (`dotnet run`). Home, Counter and Weather work as before. They now go through ModRouter, which moves keyboard focus to the page heading after each navigation and sets the browser title for routes that declare one.

## 4. Add a page inside a page

Create `Pages/Team.razor`. The page declares its route with `@page` and gives it a name. It places a `ModRouterOutlet` where a child page should appear:

```razor
@page "/team"
@attribute [RouteName("team")]
@attribute [RouteTitle("Team")]

<h1>Team</h1>
<ul>
    <li><RouteLink Route="member" Parameters="@(new { id = 1 })">Ada</RouteLink></li>
    <li><RouteLink Route="member" Parameters="@(new { id = 2 })">Grace</RouteLink></li>
</ul>

@* The child page renders here. *@
<ModRouterOutlet />
```

Create `Pages/TeamMember.razor`. `[RouteParent]` nests it under `Team`, so it renders in that outlet:

```razor
@page "/team/{Id:int}"
@attribute [RouteParent(typeof(Team))]
@attribute [RouteName("member")]
@attribute [RouteTitle("Team member")]

<h2>Team member @Id</h2>

@code {
    [Parameter]
    public int Id { get; set; }
}
```

Run the app and open `/team`, then click Ada. The address becomes `/team/1`, the team page stays where it is and only the member below it changes. The links were not typed by hand: `RouteLink` built them from the route name `member` and the value `id`.

## What you have now

- A router that replaces the built-in one, with your existing pages working.
- A nested route: a parent page with an outlet and a child page.
- A named route and a link that builds its address from values.

## Where next

The [guided lessons](lessons.md) start from here and add one feature at a time (guards, loaders, caching, leave guards, layouts and more), each with a live demo you can run. If you want the big picture first, read [Concepts](concepts.md). Words you do not know are in the [glossary](glossary.md).
