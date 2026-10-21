# ModRouter.Lab

A Blazor WebAssembly app that teaches ModRouter by showing it work. It has nine lessons, from nested routes to the router's own flight recorder, and a strip across the top of every page that shows what the router sees: the address, the levels that matched, and a light for each stage of the navigation.

- **Run it online:** https://modrouter.github.io/blazor-modrouter/lab/
- **Follow it in writing:** the [guided lessons](../ModRouter/docs/lessons.md). Each lesson quotes code from this project, and a test keeps the quotes in step with the source.

## Run it locally

```bash
dotnet run --project ModRouter.Lab --urls http://localhost:5301
```

## Where things are

| Path | What it holds |
|---|---|
| `LabRoutes.cs` | The whole route table. Start here. |
| `Program.cs` | Registration and the router options (title, focus, view transitions, history) |
| `App.razor` | The router component and its fragments: loading, not found, not authorized, error |
| `Lessons/L1` to `Lessons/L9` | One folder per lesson: its pages, guards and loaders |
| `Hud/` | The signal strip drawn from the route state and the navigation history |
| `Layout/` | The shell, the lesson line and the frame every lesson wears |
| `Services/` | The small stateful services the lessons share (clearance, loader dials, draft, dialog) |
| `wwwroot/css/` | The look of the lab: `lab.css` for the shell, `lessons.css` for the stages |

## Change it

Every lesson page of the guide ends with a "Your turn" exercise. The lab is meant to be edited: add a route to `LabRoutes.cs`, a component for it, and watch the strip react.
