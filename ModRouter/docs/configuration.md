# Configuration

The options AddModRouter takes. Back to the [README](../README.md).
This page moves from the original README unchanged.

## Configuration

`AddModRouter` takes `ModRouterOptions`: `ConstraintRegistry`, `ValidateComponentBindings` (default true), `AliasBehavior`
(default `Render`: an alias URL renders in place), the groups `Title` (`Format` default `"{0}"`, `Default`
default null, `NotFound` default `"Not found"`), `Announce` (`Enabled` true), `Focus` (`Enabled` true, `Selector` `"h1"`, `Timeout`
1 s) `Scroll` (`Enabled` true, `Container` null = the window, `Restore` true), `ViewTransitions` (`Enabled` false, `Timeout` 300 ms; see [Navigation](navigation.md)), `Pages` (the assemblies whose pages are
discovered at startup, after the fluent routes) and `Diagnostics` (`HistorySize`, default 0 = no navigation history; see
[Observability](observability.md)). An invalid title format, a negative focus timeout, a view-transition timeout that is not positive or a history size outside 0 to 1000 fails at startup. It registers `TimeProvider.System` if the app has not registered a `TimeProvider`. No settings are
read and no external service is called. The effects use one static web asset, `_content/ModRouter/mod-router.js`.

