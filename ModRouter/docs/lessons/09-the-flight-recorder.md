# Lesson 9: The flight recorder

Read back every navigation: which stage ran, how long it took, how it ended. Back to the [guided lessons](../lessons.md).

**Live demo:** [open lesson 9 in the lab](https://modrouter.github.io/blazor-modrouter/lab/lessons/recorder)

## See it

1. Press the four buttons. Each one provokes a different ending: a page, a redirect, a missing route and a refusal.
2. Read the recorder. Every navigation is one row, with the stages it ran and how long each took.
3. Compare the colors. A stage that sent you elsewhere is amber, and one that stopped the navigation is rose.
4. Click around the other lessons and come back: the recorder keeps the last forty navigations.

You have been watching the same data all along. The row of lights at the top of every page is drawn from the latest record.

## Read it

The router can keep a short history of the navigations that ended. It is off by default, and one option turns it on:

```csharp
// ModRouter.Lab/Program.cs
options.Diagnostics.HistorySize = 40;                     // keep the last 40 navigations for the flight recorder (lesson 9)
```

The history lives in a service, `RouterDiagnostics`. Its `History` is a snapshot, oldest first, and `Changed` fires after each record. The recorder reads and redraws:

```csharp
// ModRouter.Lab/Lessons/L9/Lesson9Page.razor.cs
// History is oldest first and is a snapshot; show the newest on top.
IReadOnlyList<NavigationRecord> Records => Diagnostics.History.Reverse().Take(14).ToList();

protected override void OnInitialized() => Diagnostics.Changed += OnChanged;
```

A record is plain data (text, numbers and enums), so keeping many of them holds nothing alive:

| Field | What it tells you |
|---|---|
| `NavigationId`, `Target` | Which navigation, and where it was going |
| `Trigger`, `CommittedAs` | How it began (link, programmatic, back/forward, redirect, first load, reload) |
| `Outcome`, `OutcomeDetail` | `Committed`, `Redirected`, `Canceled`, `Superseded` or `Failed`, with detail such as `NotFound` |
| `Duration` | The time from the start to the end |
| `Stages` | One entry per stage that ran: its name, its duration and whether it continued, redirected or cancelled |
| `RedirectCount` | How many hops a redirect chain took |

### The same lights, from the same records

The lights at the top of the lab read the latest record and turn each stage into a lamp:

```csharp
// ModRouter.Lab/Hud/SignalStrip.razor.cs
var history = Diagnostics.History;
var record = history.Count > 0 ? history[^1] : null;
```

A stage that did not run, because an earlier one redirected or cancelled, simply is not in the record, so its lamp stays dark. That is how the **Guard** light goes rose and the **Loader** light stays dark when the airlock refuses you.

### Beyond the history

The history is for people and for small in-app tools. For production, the router also writes structured log events through `ILogger` (every event has a stable id) and emits a trace span per navigation through an `ActivitySource`, so your logging and tracing tools see the same story. See [Observability](../observability.md).

## Your turn

1. Add a filter above the table that shows only the records whose outcome is not `Committed`. Use the `Outcome` field.
2. Add a column that shows `RedirectCount`, and trigger the old bridge as a visitor to see a chain with more than one hop.
3. Raise `HistorySize` to 5 and navigate around. What happens to the oldest rows?
4. Pick a navigation from the table that took longest. Which stage was it? Slow the loader with lesson 5's slider and look again.

## What you learned

- Turn on the history with `Diagnostics.HistorySize`, then read `RouterDiagnostics.History`.
- A record says how a navigation began, how it ended, and how long each stage took.
- The lights at the top of the lab are drawn from the same records.
- Logs and traces carry the same information for production tools.

## You finished the lessons

Go deeper in the reference pages: [Routing](../routing.md), [Navigation](../navigation.md), [Route sources](../route-sources.md), [Authorization](../authorization.md) and [Hosting](../hosting.md). Back to the [guided lessons](../lessons.md).
