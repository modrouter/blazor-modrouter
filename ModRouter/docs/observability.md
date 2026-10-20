# Observability

Structured log events, tracing, and the navigation history. Back to the [README](../README.md).
This page moves from the original README unchanged apart from slice labels.

## Observability

What the router decided and why, at three levels that cost nothing until something asks for them: structured log events
through the app's `ILogger`, one `ActivitySource` for tracing, an opt-in in-memory history of the
last navigations. None of it changes what a navigation does (the whole test suite also runs with a listener sampling every activity and with the
history on), and a logging provider, an activity listener or a history subscriber that throws is ignored.

### Log events

Every log entry of the router is a source-generated event of `Diagnostics/RouterLog` with a stable id, a fixed level and
named placeholders; the category is the class that logs (`NavigationCoordinator`, `GuardStage`, `ModRouter`, ...). Debug
is how a navigation went, Warning is degraded but continuing, Error is a failed operation; there are no Information
events. Ids are grouped by area (1000 lifecycle, 1100 stages, 1200 guards and loaders, 1300 redirects, 1400
configuration and markup, 1500 re-check and authentication, 1600 `OnNavigateAsync` and compatibility, 1700 effects and
JavaScript, 1800 subscribers and diagnostics) and never reused. `RouterLogTests` compares this table with the code.

| Id | Name | Level | Placeholders | Area |
|---|---|---|---|---|
| 1000 | `NavigationRequested` | Debug | `NavigationId`, `Target`, `Trigger` | Lifecycle |
| 1001 | `NavigationCommitted` | Debug | `NavigationId` | Lifecycle |
| 1002 | `NavigationSuperseded` | Debug | `NavigationId` | Lifecycle |
| 1003 | `NavigationCanceled` | Debug | `NavigationId` | Lifecycle |
| 1004 | `NavigationFailed` | Error | `NavigationId`, `Target` | Lifecycle |
| 1005 | `FallbackNavigationFailed` | Error | `Location` | Lifecycle |
| 1006 | `LocationChangedWithoutPreparedNavigation` | Warning | `Location` | Lifecycle |
| 1007 | `UnparseableLocation` | Warning | `Location` | Lifecycle |
| 1100 | `NavigationCanceledByStage` | Debug | `NavigationId`, `Stage` | Stages |
| 1101 | `ParameterBindingFailed` | Warning | `NavigationId`, `Path`, `Error` | Stages |
| 1102 | `AliasRenderedInPlace` | Debug | `Path`, `Template` | Stages |
| 1200 | `GuardFailed` | Error | `NavigationId`, `Message`, `Level` | Guards and loaders |
| 1201 | `LoaderFailed` | Error | `NavigationId`, `Level` | Guards and loaders |
| 1300 | `RedirectLoop` | Error | `NavigationId`, `Path` | Redirects |
| 1400 | `MarkupRoutesCompileFailed` | Error | — | Configuration and markup |
| 1401 | `MarkupEntryParametersChanged` | Debug | `Registered`, `Current` | Configuration and markup |
| 1410 | `OutletNotRendered` | Warning | `RouteName`, `Template`, `Component`, `Depth` | Configuration and markup |
| 1500 | `AuthorizationRechecked` | Debug | `Path`, `After`, `Before` | Re-check and authentication |
| 1600 | `OnNavigateCanceled` | Debug | `NavigationId` | OnNavigateAsync and compatibility |
| 1601 | `OnNavigateThrew` | Error | `NavigationId` | OnNavigateAsync and compatibility |
| 1602 | `CompatAssembliesChanged` | Warning | `Before`, `After` | OnNavigateAsync and compatibility |
| 1700 | `EffectsFailed` | Warning | — | Effects and JavaScript |
| 1701 | `JsModuleLoadFailed` | Warning | — | Effects and JavaScript |
| 1800 | `StateSubscriberThrew` | Error | — | Subscribers and diagnostics |
| 1801 | `DiagnosticsSubscriberThrew` | Error | — | Subscribers and diagnostics |

1004, 1005, 1200 (when the guard threw), 1201, 1400, 1601, 1700, 1701, 1800 and 1801 carry the exception. `CA1848`
and `CA1873` are errors in this project (`Logging.globalconfig`) and `ArchitectureTests.Only_RouterLog_writes_log_entries`
checks the IL, so an ad hoc `logger.LogXxx` call does not build. Every logger the router uses is wrapped so that a provider
that throws is ignored (the framework's aggregate logger rethrows its providers' exceptions).

**Seeing them.** The events go to whatever `ILogger` providers the app registered: for example the console or a log aggregator, where
they appear under the category of the class that logged (filter on `ModRouter` and an event id, such as
1410 for the outlet warning). The lifecycle events are Debug, so raise the level for the router's categories while
investigating, for example in `appsettings.Development.json`:

```json
{ "Logging": { "LogLevel": { "ModRouter": "Debug" } } }
```

or `builder.Logging.AddFilter("ModRouter", LogLevel.Debug)`. The `Target` and `Location` placeholders (1000, 1004,
1005, 1006, 1007) are the URL as navigated, **including its query and fragment** (an authentication callback's `#code=…&state=…`, a
`returnUrl`, a token): unlike the activity tags and the history, which keep paths only, a log at Debug or a failure's Error
may carry them, so keep the router's Debug level to development and treat its logs accordingly. Warnings and errors (a location change the
router did not prepare, a binding failure, a failed guard or loader, a redirect loop, the outlet warning) show at the usual
levels without that.

### Tracing

`RouterTelemetry.SourceName` (`"ModRouter"`) is the router's one `ActivitySource`. With no listener
nothing is created. With one, each navigation is a `Navigation` activity from its request to its end (including the wait
for the platform's location change and `OnNavigateAsync`), with a child activity per pipeline stage (`normalize`,
`recognize`, `bind`, `leaveguard`, `redirect`, `guard`, `loader` and any added stage, by its type name without "Stage", tagged
`router.stage.outcome`) and one named `OnNavigateAsync` that the hook's own code sees as `Activity.Current`. Each redirect
hop is its own navigation and activity, linked to the previous hop, and every hop shares the parent of the navigation that
started the chain (for example an app's "click" activity). Tags:

| Tag | Value |
|---|---|
| `router.navigation.id` | the navigation's id (`long`; each hop has its own) |
| `router.target` | the target's absolute path (`/myapp/customers/5`): no scheme, host, query or fragment |
| `router.trigger` | the trigger it started with (`Link`, `Programmatic`, `Redirect`, `InitialLoad`, `Reload`, ...) |
| `router.committed_as` | the trigger it committed as (a programmatic navigation recognized as back/forward commits as `HistoryRestore`; a reload that replaces a navigation waiting on `OnNavigateAsync` commits as that navigation), only when it committed |
| `router.redirect.count` | the hops before this one |
| `router.outcome` | `Committed`, `Redirected`, `Canceled`, `Superseded` or `Failed` |
| `router.outcome.detail` | for `Committed` `NotFound`, `RedirectLoop` or an error kind at its level (`LoadFailed@1`, `NotAuthorized@0`); for `Redirected` the next hop's absolute path (the target's, with no hop after it, when the target is where the user already is; `external ` and the scheme, host and path for a full page load: never its query or fragment); for `Canceled` by a stage `canceled by <stage>`, and `the router was disposed` for one the disposal ended while it waited for its location change or `OnNavigateAsync` (otherwise none); for `Superseded` `released before its location change` or `released while OnNavigateAsync ran` when it was already prepared, and `a newer navigation committed while the flag dropped` when a `NavigatingChanged` subscriber's navigation committed during its commit (none when a newer navigation canceled it while it was preparing); for `Failed` the exception type (the activity's status is then `Error` with an exception event, carrying the exception's own message, which the router does not control) |

Every activity the router starts is stopped, whatever becomes of the navigation (a navigation that a newer one supersedes
ends when its running stage returns: a stage that ignores its cancellation token keeps its activity, and the navigation's,
open until it does), also when a listener's `Sample`,
`ActivityStarted`, `ActivityStopped` or `ExceptionRecorder` throws (the navigation goes on and its record is still kept).
The router's disposal ends a navigation that was waiting for its location change or for `OnNavigateAsync` as `Canceled`
(`the router was disposed`). One limit: when the router prepared a navigation and allowed it but another location-changing
handler of the app prevents it, the platform tells the router nothing, so that navigation's activity stays open (and its record
unpublished) until the next navigation or location change, which ends it as `Superseded` (`released before its
location change`). To see them without OpenTelemetry, for
example in a test or a development build:

```csharp
using var listener = new ActivityListener
{
    ShouldListenTo = source => source.Name == RouterTelemetry.SourceName,
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => Console.WriteLine(
        $"{activity.OperationName} {activity.GetTagItem(RouterTelemetry.TargetTag)} {activity.GetTagItem(RouterTelemetry.OutcomeTag)} {activity.Duration.TotalMilliseconds:F1} ms"),
};
ActivitySource.AddActivityListener(listener);
```

An OpenTelemetry tracer subscribes with `AddSource(RouterTelemetry.SourceName)`. The authentication re-check's own guard run
is not traced; the reload it causes is an ordinary `Reload` navigation.

### Navigation history

`ModRouterOptions.Diagnostics.HistorySize` (default 0, at most `DiagnosticsOptions.MaxHistorySize` = 1000; anything else
fails at `AddModRouter`) keeps the last navigations in the scoped `RouterDiagnostics`: `History` (oldest first, a snapshot)
of immutable `NavigationRecord`s (id, target path, the trigger it started with and the one it committed as, start time,
duration, outcome and detail, the redirect chain's first path and the hop count, and a `StageRecord` per stage with its
name, duration and outcome, plus `location-change` for the wait for the platform when there was one) and `Changed`, raised
once per navigation when it ends (a subscriber that throws is logged as 1801 and skipped). A record holds only strings,
numbers and enums. With 0 nothing is recorded or allocated, and with neither a listener nor the history the
instrumentation's cost is not measurable (the spike measured 3.1–4.7 µs per navigation against a 3.3–4.1 µs baseline; a
history of 50 adds about 1 µs, a listener sampling everything 2–10 µs).

### The outlet warning

Event 1410 (`OutletNotRendered`, Warning) names a route whose component never rendered an `ModRouterOutlet` while a child
route it matched was supposed to show: the parent page rendered (its own depth was claimed by an outlet) but the next depth
never was. The router judges a state when it leaves the screen (the next navigation commits, or the router is disposed), not
when it commits, so a parent that renders its outlet after loading does not warn: a state shown for less than a second, one
that never rendered, one without a chain (not found) and anything past a failed level (its error replaces the page) are not
judged, and each route warns once per router. Its limits follow from that: the second counts from the state's first render,
so a parent that loads its own data for more than a second and renders its outlet only afterwards can warn when the user
leaves before it did (a false positive), and a state replaced within its first second is never judged (a missing outlet
there goes unreported until a state that stays longer). Typical causes: the outlet is missing, or it sits inside a condition that never
became true. It behaves the same with `Found` and the compatibility `RouteView`.

