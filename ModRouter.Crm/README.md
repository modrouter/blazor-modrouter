# ModRouter.Crm

A sample CRM that demonstrates ModRouter, live at https://modrouter.github.io/blazor-modrouter/crm/. It runs inside a browser-style frame with an editable address bar, and a guide beside it turns on one router feature at a time across eight chapters: routes, parameters, nested routes, the query string, guards, loaders, leave guards, and names and redirects. A live log under the guide shows the route that matched and the stages each navigation passed.

```bash
dotnet run --project ModRouter.Crm --urls http://localhost:5401
```

It has no UI library. The components (button, avatar, badge, dialog and so on) are in `Ui/`, and the look is Tailwind plus the colour tokens in `wwwroot/css/theme.css`, in the spirit of Fluent 2, with light and dark following the system. The stylesheet `wwwroot/css/output.css` is checked in, so running the CRM needs only the .NET SDK. To change the markup's classes, run `npm install` once in `ModRouter.Crm`; the build then regenerates the stylesheet (or run `npm run css`).
