# Glossary

The words ModRouter uses, in plain language, in alphabetical order. Back to the [README](../README.md). For the big picture, read [Concepts](concepts.md).

**Alias.** A second address for the same route. It can show the same page in place, or redirect to the route's main address. See [Route sources](route-sources.md).

**Breadcrumb.** The "Home › Fleet › Ship 2" trail built from the matched levels of the current address. `RouteBreadcrumb` draws it. See [Navigation](navigation.md).

**Cache (loader cache).** Plain data a loader returned, kept for a while. While it is *fresh* a visit uses it without calling the loader; while it is *stale* a visit shows it at once and refreshes it in the background. See [Navigation](navigation.md).

**Candidate.** A route that could match an address. `Explain` lists all candidates for an address and what it decided about each.

**Catch-all.** A placeholder that takes everything left in the address, however many parts: `{*rest}`. It ranks last.

**Chain.** The routes that matched one address, from the outermost to the innermost. `/customers/42/orders` has a chain of three levels.

**Commit.** The moment a navigation is done and the new page is shown. Until then the old page and address stay. See [Concepts](concepts.md).

**Constraint (rule).** A condition on a placeholder, such as `{id:int}` (a whole number) or `{name:minlength(3)}`. An address that breaks the rule does not match that route. See [Routing](routing.md).

**Deferred value.** Part of a loader's answer that the page does not wait for. The page shows first and the value fills in later.

**Explain.** A call that shows how the router would match an address, without navigating: every candidate, a verdict for each, and why a matching one lost. See [Routing](routing.md).

**Group.** A level that shares a layout, a guard or a loader between routes without adding anything to their addresses. See [Routing](routing.md).

**Guard.** A small class that answers "may this navigation go on?" before a page appears: allow, cancel, redirect, or not authorized. See [Navigation](navigation.md).

**Index route.** The default child of a route, shown at the parent's own address.

**Keep-alive.** A route setting that keeps the page in memory, hidden, with its state, when the visitor leaves, and shows the same instance on return. See [Navigation](navigation.md).

**Layout.** A component that wraps pages and places the page with an outlet. Every route level can act as one.

**Leave guard.** A guard asked before the page on screen goes away. It is how you ask "leave without saving?". See [Navigation](navigation.md).

**Level.** One route in a chain. Level 0 is the outermost.

**Loader.** A class that fetches the data a route needs before its page shows. See [Navigation](navigation.md).

**Named route.** A route with a name, so you can build its address from the name and values instead of typing the address.

**Navigation.** One trip from the current page to a new address, from the request to the commit (or to a refusal).

**Normalize.** The first stage of the pipeline: it cleans up the address and takes the base path away.

**Outlet.** The place in a component where the next level of the chain is shown (`ModRouterOutlet`). A *named outlet* is a second place a route can fill, such as a side panel.

**Pipeline.** The ordered stages every navigation passes through: normalize, recognize, bind, leave guards, redirect rules, guards, loaders, then commit.

**Precedence.** The order in which the router prefers routes that fit the same address: fixed text, then a placeholder with a rule, then a placeholder without one, then a catch-all. It never depends on declaration order.

**Preload.** Starting a loader early, for example when the pointer rests on a link, so the click finds the data ready.

**Redirect rule.** A route that only forwards one address to another named route. It shows no page of its own.

**Revalidate.** Running the loaders of the page on screen again, without leaving it, to refresh its data after a change.

**Route.** A rule that says: an address of this shape shows this component. It has a template, a component and usually a name. See [Routing](routing.md).

**Route state.** What the router publishes after each commit: the address, the chain, the title, how the navigation began and any error.

**Route table.** All the routes of an app, compiled once at startup.

**Slot.** A component a route supplies for a named outlet, for example a side panel for one page.

**Stage.** One step of the pipeline. Each stage can let the navigation go on, redirect it or cancel it.

**Stream.** Part of a loader's answer that is a sequence of items arriving over time, such as live readings.

**Template.** The shape of an address in a route: `/customers/{id:int}/orders`.

**Trigger.** How a navigation began: a link, a call from code, the Back button, a redirect, the first load or a reload.

**View transition.** A short cross-fade between the old and the new page, done by the browser. See [Navigation](navigation.md).
