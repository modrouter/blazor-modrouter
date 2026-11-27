# Lesson 9: The route log

Read back every navigation: which stage ran, how long it took, how it ended. Back to the [guided lessons](../lessons.md).

**Live demo:** the route log under the guide in the [sample CRM](https://modrouter.github.io/blazor-modrouter/crm/), which has been on screen in every chapter

## See it

1. Click through the chapters. After every click the log shows the last navigation: a row for each stage, and a line at the top that says how it ended and how long it took.
2. Try the cases that end differently: an account (shown), `/clients/3` (redirect), `/accounts/abc` (no route), Settings as a visitor (redirect), Billing as a rep (not allowed), and Keep editing on the edit page (canceled).
3. Read the colors. A stage that sent you elsewhere takes the brand color, one that stopped the navigation is amber, and a failure is red.
4. Open **Before that**. The log keeps the last forty navigations.

A stage that did not run, because an earlier one stopped the navigation, reads "Not reached". That is the log being honest about the order.

## Read it

The router keeps a short history of the navigations that ended. It is off by default, and one option turns it on:

```csharp
// ModRouter.Crm/Program.cs
options.Diagnostics.HistorySize = 40;          // the guide's route log reads this history
```

The history lives in a service, `RouterDiagnostics`. Its `History` is a snapshot, oldest first, and `Changed` fires after each record. The route log reads it and redraws:

```csharp
// ModRouter.Crm/Guide/RouterLog.razor.cs
var history = Diagnostics.History;
Last = history.Count > 0 ? history[^1] : null;
Diagnostics.Changed += OnChanged;
```

A record is plain data (text, numbers and enums), so keeping many of them holds nothing alive:

| Field | What it tells you |
|---|---|
| `NavigationId`, `Target` | Which navigation, and where it was going |
| `Trigger`, `CommittedAs` | How it began (link, programmatic, back/forward, redirect, first load, reload) |
| `Outcome`, `OutcomeDetail` | `Committed`, `Redirected`, `Canceled`, `Superseded` or `Failed`, with detail such as `NotFound` |
| `Duration` | The time from the start to the end |
| `Stages` | One entry per stage that ran: its name, its duration and whether it continued, redirected or canceled |
| `RedirectCount` | How many hops a redirect chain took |

### The stages, in order

The list of rows is the router's pipeline, which this whole course has walked through:

```csharp
// ModRouter.Crm/Guide/RouterLog.razor.cs
("Recognize", "Find the matching route."),
("LeaveGuard", "May the current page be left?"),
("Guard", "May this navigation go on?"),
("Loader", "Fetch the page data."),
("Commit", "Show the new page."),
```

Recognize is [lesson 1](01-a-url-is-a-screen.md) to [4](04-the-query-string.md), LeaveGuard is [lesson 7](07-leave-guards.md), Redirect is [lesson 8](08-names-and-redirects.md), Guard is [lesson 5](05-guards.md) and Loader is [lesson 6](06-loaders.md). The commit is two-phase: the prepared state shows only when the browser reports the location change, and a newer navigation replaces an unfinished one.

The log draws a row for each stage in that list. A stage that is not in the record did not run, so the log marks it "Not reached". The wording of the outcome is a small mapping:

```csharp
// ModRouter.Crm/Guide/RouterLog.razor.cs
NavigationRecordOutcome.Committed => "shown",
```

### Beyond the history

The history is for people and for small in-app tools. For production, the router also writes structured log events through `ILogger` (every event has a stable id) and emits a trace span per navigation through an `ActivitySource`, so your logging and tracing tools see the same story. See [Observability](../observability.md).

## Your turn

1. Show only the records whose outcome is not `Committed` in the **Before that** list. Use the `Outcome` field.
2. Add the `RedirectCount` to the summary line, and open `/clients/3` as a visitor to see a chain with more than one hop.
3. Raise `HistorySize` to 5 and navigate around. What happens to the oldest rows?
4. Slow the network with the guide's control, open an account, and find the stage that took longest.

## What you learned

- Turn on the history with `Diagnostics.HistorySize`, then read `RouterDiagnostics.History`.
- A record says how a navigation began, how it ended, and how long each stage took.
- The CRM's route log is drawn from those records, and so can your own tools.
- Logs and traces carry the same information for production tools.

## You finished the lessons

Go deeper in the reference pages: [Routing](../routing.md), [Navigation](../navigation.md), [Route sources](../route-sources.md), [Authorization](../authorization.md) and [Hosting](../hosting.md). Back to the [guided lessons](../lessons.md).
