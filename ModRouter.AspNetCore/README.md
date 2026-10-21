# ModRouter.AspNetCore

The ASP.NET Core pieces of [ModRouter](https://www.nuget.org/packages/ModRouter) for server hosts. `AddModRouterHttpStatus()` tells the router how to
set the HTTP status of a server-rendered page: 404 for an unmatched URL, 401 or 403 for a guard that denied, 500 for a failed pipeline and 302 for
a guard redirect. See the hosting page of the ModRouter documentation for the full setup (the endpoint requirement, prerendering and carrying loader data to the interactive pass).

```bash
dotnet add package ModRouter.AspNetCore --prerelease
```

```csharp
builder.Services.AddModRouter(routes => ...);
builder.Services.AddModRouterHttpStatus();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
```
