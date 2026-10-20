using ModRouter.Core.Matching;

namespace ModRouter.Core.Definitions;

/// <summary>
/// Overrides how a route level appears in a breadcrumb trail (added by <c>RouteMap.Breadcrumb(...)</c>): a fixed
/// <see cref="Label"/>, a <see cref="Factory"/> over the level's inherited parameters, or <see cref="Hidden"/> to leave
/// the level out. Without it a level shows its route title.
/// </summary>
public sealed record RouteBreadcrumbMetadata(string? Label, Func<RouteParameters, string>? Factory, bool Hidden);

/// <summary>The element to focus after a navigation to this route, instead of the router's default (added by <c>RouteMap.Focus(...)</c>).</summary>
public sealed record RouteFocusMetadata(string Selector);

/// <summary>Switches the screen-reader announcement off for this route (added by <c>RouteMap.NoAnnounce()</c>).</summary>
public sealed record RouteAnnounceMetadata(bool Enabled);
