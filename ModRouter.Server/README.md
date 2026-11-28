# ModRouter.Server

**Install this in the server project of a Blazor Web App** (interactive Server, WebAssembly or Auto rendering, with or without prerendering). It includes
[ModRouter](https://www.nuget.org/packages/ModRouter), so it is the only package that project needs. A Blazor WebAssembly app, and the `.Client` project of a
Web App, install `ModRouter` alone.

```bash
dotnet add package ModRouter.Server --prerelease
```

`AddModRouterHttpStatus()` sets the HTTP status of a server-rendered page: 404 for an unmatched URL, 401 or 403 for a guard that denied, 500 for a failed
pipeline and 302 for a guard redirect. See the hosting page of the ModRouter documentation for the full setup (the endpoint requirement, prerendering and
carrying loader data to the interactive pass).

```csharp
builder.Services.AddModRouter(routes => ...);
builder.Services.AddModRouterHttpStatus();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
```
