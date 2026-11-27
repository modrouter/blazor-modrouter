# Lesson 2: Parameters

Turn part of an address into data, and see why `/accounts/abc` matches nothing. Back to the [guided lessons](../lessons.md).

**Live demo:** the [sample CRM](https://modrouter.github.io/blazor-modrouter/crm/), chapter 2, Parameters

## See it

1. Click **Open account 3** in the guide. The address is `/accounts/3`.
2. Click **Open contact 7**. The page says the address held `id = 7`, and that it reached the page as an `int`.
3. Type `/accounts/abc` into the address bar. Nothing matches, and the router shows the not-found page.
4. Look at the route log for each: the log lists the parameter and its value.

## Read it

A template can have a hole, written `{name}`. The router fills it from the address. A **rule** after a colon limits what the hole accepts, and converts the value:

```csharp
// ModRouter.Crm/Crm/CrmRoutes.cs
.Route("{id:int}", typeof(ContactPage), p => int.TryParse($"{p["id"]}", out var id) && CrmData.Contact(id) is { } contact ? contact.Name : $"Contact {p["id"]}", name: "contact"))
```

`{id:int}` accepts only whole numbers, so `abc` does not match this route. The third argument is a function of the values. Here it makes the title and the breadcrumb label the contact's name, not "Contact 7".

### Receiving the value

The page asks for the value with `[Parameter]`. It arrives already converted:

```csharp
// ModRouter.Crm/Crm/Pages/ContactPage.razor.cs
[Parameter]
public int Id { get; set; }
```

A page that is not found is still a page: the route matched, so the router called it, and it looks the contact up itself. The CRM shows "There is no contact 99" for that, which is different from "No route matches". The first is your data; the second is the address.

### Linking by name

A link never needs the address typed by hand. It names the route and gives the values, and the router builds the address:

```razor
@* ModRouter.Crm/Crm/Pages/ContactPage.razor *@
<RouteLink Route="account-overview" Parameters="@(new { id = Contact.AccountId })" Class="crm-link text-sm">@CrmData.Account(Contact.AccountId)?.Name</RouteLink>
```

If the address of that route changes later, this link does not. [Lesson 8](08-names-and-redirects.md) goes further with names.

### Which route wins

When more than one route fits an address, the router never guesses. It compares them one segment at a time, in this order:

| Rank | Kind of segment | Example |
|---|---|---|
| 1 | Fixed text | `new` |
| 2 | Placeholder with a rule | `{id:int}` |
| 3 | Placeholder without a rule | `{slug}` |
| 4 | Catch-all | `{*rest}` |

The order never depends on the order you declared the routes in. `contacts/new` beats `contacts/{id:int}`, which beats `contacts/{slug}`. Two routes of the same shape cannot be ranked, so the app stops at startup with `RT0012`. `Explain` evaluates an address against the table and says why each candidate won or lost, without navigating. It takes an address without the base path, query or fragment. See [Routing](../routing.md).

## Your turn

1. Open `/contacts/7`, then `/contacts/seven`. Which one gets the not-found page?
2. Change `{id:int}` to `{id}` for contacts. The page now fails to bind `seven` to an `int`. Put the rule back: a rule keeps bad addresses out of your component.
3. Add `contacts/{code:int}` next to the existing contact route. The app stops with `RT0012`, naming both templates. Remove it.

## What you learned

- `{name}` in a template becomes a value; `{name:int}` also limits and converts it.
- A page receives route values through `[Parameter]`.
- A link names a route and its values, and the router builds the address.
- The router ranks routes by how specific each segment is, never by declaration order.

Next: [Lesson 3, Nested routes](03-nested-routes.md).
