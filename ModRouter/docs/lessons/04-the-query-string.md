# Lesson 4: The query string

Keep state in the address so a link restores it and Back undoes it. Back to the [guided lessons](../lessons.md).

**Live demo:** the [sample CRM](https://modrouter.github.io/blazor-modrouter/crm/), chapter 4, The query string

## See it

1. Open **Accounts**. Filter chips (stage), a sort and a search box appear.
2. Click **Won**, then **Revenue**. The address gains `?stage=Won&sort=revenue`.
3. Type `wind` in the search box. The address gains `q=wind`, and the other two values stay.
4. Press the browser's **Back** button. The last filter is undone.
5. Reload the frame. The list is exactly as you left it, because the address holds everything.

## Read it

A filter that lives only in a component is lost on reload and cannot be shared. A filter in the address can be bookmarked, sent in a message, and stepped back through. The accounts list reads three values from the query string:

```csharp
// ModRouter.Crm/Crm/Pages/AccountsPage.razor.cs
[Parameter, SupplyParameterFromQuery(Name = "q")]
public string? Q { get; set; }
```

`Stage` and `Sort` are declared the same way, with the names `stage` and `sort`. The list is then a function of the address: the rows are computed from these three properties and nothing else.

The query is not part of the route. `/accounts?stage=Won` and `/accounts` are the same route with different values, so the chain does not change, and the page is updated in place, not rebuilt.

### Links that carry a query

A link names the route and gives the query. Each chip changes one value and keeps the others, so a link is built from the current values with one replaced. A null value drops the key from the address:

```csharp
// ModRouter.Crm/Crm/Pages/AccountsPage.razor.cs
Dictionary<string, object?> WithStage(string? stage) => new() { ["q"] = Q, ["stage"] = stage, ["sort"] = Sort };
```

```razor
@* ModRouter.Crm/Crm/Pages/AccountsPage.razor *@
<RouteLink Route="accounts" Query="@WithStage(null)" Class="@Chip(Stage is null)">All</RouteLink>
```

### Navigating from code

The search box is not a link, so it navigates from code. It builds the address from the three values and escapes the text:

```csharp
// ModRouter.Crm/Crm/Pages/AccountsPage.razor.cs
Navigation.NavigateTo("accounts" + (parts.Count > 0 ? "?" + string.Join('&', parts) : string.Empty));
```

The address has no leading slash. A leading slash means "from the host's root", which would leave the app's base path when the app is hosted in a folder, as the CRM is on its live site. An address without one is relative to the base path. Links built from route names never have this problem, because the router adds the base path itself.

Each change is a navigation of its own, so each one is in the history and in the route log. See [Navigation](../navigation.md).

## Your turn

1. Add a second sort, by city: handle `sort=city` in `AccountsPage.razor.cs` and add a `RouteLink` chip with `WithSort("city")`.
2. Add `industry` as a fourth query value. Update `WithStage` and `WithSort` so a click on one chip keeps it.
3. Open `/accounts?stage=Won` in a new tab. The page needs no click to show the filter.

## What you learned

- `[SupplyParameterFromQuery]` reads a value from the query string into a property.
- The query is not part of the route: changing it updates the page in place.
- A link names a route and a query; a null value drops the key.
- Shared state in the address is bookmarkable, shareable and undoable.

Next: [Lesson 5, Guards](05-guards.md).
