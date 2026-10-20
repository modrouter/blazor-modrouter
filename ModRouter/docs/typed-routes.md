# Typed routes

Compile-checked route names and URL helpers from the pages' own attributes. Back to the [README](../README.md).

The `ModRouter.Generators` package is a Roslyn source generator. For every page that carries both `[Route]` (what `@page` compiles to) and
[`[RouteName]`](route-sources.md), it generates, in a namespace named after the assembly:

- `RouteNames`, a class of constants (`RouteNames.CustomerDetails == "customer-details"`) for `RouteLink Route="@RouteNames.CustomerDetails"` and the
  router's named-route methods.
- On `INavigationService`, a `Get{Name}Url` and a `NavigateTo{Name}Async` extension method per route whose parameters are the template's, so a
  missing or mistyped value is a compile error instead of a `RouteUrlGenerationException` at run time.

```razor
@page "/customers/{id:int}"
@attribute [RouteName("customer-details")]
```

```csharp
string url = Navigation.GetCustomerDetailsUrl(42);                       // "/customers/42" (with the base path)
await Navigation.NavigateToCustomerDetailsAsync(42, fragment: "orders"); // NavigationResult
```

The generated code calls `GetRouteUrl` and `NavigateToRouteAsync`, so the router still builds every URL: base paths, percent-encoding, the constraint
checks and `ModNavigationOptions` behave exactly as for the string-based calls.

## Reference the generator

```xml
<ProjectReference Include="..\ModRouter.Generators\ModRouter.Generators.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## What is generated

| Template part | Helper parameter |
|---|---|
| `{id:int}`, `{n:long}`, `{ok:bool}`, `{x:double}`, `{x:float}`, `{x:decimal}` | `int`, `long`, `bool`, `double`, `float`, `decimal` |
| `{id:guid}`, `{when:datetime}` | `Guid`, `DateTime` |
| `{name}`, any other constraint (`alpha`, `regex(...)`, `minlength(3)`), `{*rest}` | `string` |
| an optional parameter (`{page:int?}`) | the same type, nullable, `= null`, and left out of the URL when null |

Every helper also takes `query`, `fragment` and, for navigation, `options` and `cancellationToken`.

- A route name becomes a PascalCase identifier (`customer-orders` is `CustomerOrders`; `404` is `Route404`). A parameter name that is a C# keyword gets an `@`
  (`{class}` is `@class`), and one that would clash with a fixed parameter (`navigation`, `query`, `fragment`, `options`, `cancellationToken`) gets a
  `Value` suffix.
- The first `[Route]` on a page is the canonical template; further ones are aliases and are ignored.
- **MRG001** (error): two route names produce the same helper name (`customer-list` and `customerList`); the second route gets no helper.
- **MRG002** (warning): a template cannot be parsed (an unclosed `{`); that route gets no helper.
- Only pages are covered. A route declared in the fluent map or in `<ModRouteEntry>` markup has no attribute to read; give its page `[RouteName]` and
  `[Route]`, or use the string-based calls. The generated names match the router's own: a route name the router rejects as a duplicate (RT0010) is still
  reported there at startup.
- The generator is incremental: a page whose route attributes did not change is not read again.
