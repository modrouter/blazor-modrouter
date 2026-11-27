# Lesson 1: A URL is a screen

Declare a route table so that every screen of an app has an address. Back to the [guided lessons](../lessons.md).

**Live demo:** the [sample CRM](https://modrouter.github.io/blazor-modrouter/crm/), chapter 1, A URL is a screen

## See it

1. Open the CRM. The guide beside the browser frame is on chapter 1.
2. Click **Dashboard**, **Accounts** and **Contacts**. The address bar changes with each click, and so does the screen.
3. Look at the **route log** under the guide. Each click is one navigation: the stages it passed, and the route it ended on.
4. Type `/contacts` into the address bar and press Enter. Typing an address is the same as clicking a link.

## Read it

ModRouter replaces Blazor's built-in `Router`. Instead of a `@page` line on every component, you declare the whole **route table** in one place. A route is a template (the address), the component it shows, and a name:

```csharp
// ModRouter.Crm/Crm/CrmRoutes.cs
routes.Route("/sign-in", typeof(SignInPage), name: "sign-in", title: "Sign in");
```

`title` becomes the browser tab's title. The table is compiled once at startup, and a mistake in it, such as two routes that could never be told apart, stops the app there with a code and a message, not when a visitor opens the address.

### A group adds a layout, not an address

Most of the CRM sits inside one frame: the app bar, the navigation and the page area. A **group** gives a set of routes a shared layout without adding anything to their addresses:

```csharp
// ModRouter.Crm/Crm/CrmRoutes.cs
routes.Group(typeof(CrmLayout), crm => crm
.Index(typeof(DashboardPage), name: "home")
```

`Index` is the route for the group's own address, so the dashboard is `/`. The sign-in page is declared outside the group on purpose: it shows none of the CRM's navigation.

The layout is a component. It decides where the page appears by placing an outlet:

```razor
@* ModRouter.Crm/Crm/CrmLayout.razor *@
<ModRouterOutlet />
```

### Starting the router

The table is handed to `AddModRouter` when the app starts. The options are the second argument:

```csharp
// ModRouter.Crm/Program.cs
builder.Services.AddModRouter(
routes => CrmRoutes.Declare(routes),
options =>
options.Title.Format = "{0} - Northwind CRM";
```

The root component swaps the built-in router for `ModRouter`. What the router shows when no route matches is declared in it too:

```razor
@* ModRouter.Crm/App.razor *@
<ModRouter DefaultLayout="typeof(ShellLayout)">
<NotFound>
```

[Getting started](../getting-started.md) walks through a fresh app. [Routing](../routing.md) has every rule of the table.

## Your turn

1. Add a route for a new page: a component `HelpPage` and the line `.Route("help", typeof(HelpPage), name: "help", title: "Help")` inside the group in `CrmRoutes.cs`.
2. Run the CRM and open `/help`. Look at the title of the browser tab, and at the route log.
3. Declare `help` twice. The app stops at startup instead of picking one. Remove the extra line.

## What you learned

- A route is a template, a component and a name, declared in one table.
- A group shares a layout between routes without changing their addresses.
- The root `ModRouter` component declares what shows for no match, no permission and errors.
- Mistakes in the table stop the app at startup.

Next: [Lesson 2, Parameters](02-parameters.md).
