using ModRouter.Core.Loading;

namespace ModRouter.Core.Definitions;

/// <summary>Declares a guard type on a route level (added by <c>RouteMap.Guard&lt;T&gt;()</c>).</summary>
public sealed record RouteGuardMetadata(Type GuardType);

/// <summary>Declares a leave guard type on a route level (added by <c>RouteMap.LeaveGuard&lt;T&gt;()</c>).</summary>
public sealed record RouteLeaveGuardMetadata(Type GuardType);

/// <summary>
/// Caches the critical data of a route level's loader (added by <c>RouteMap.CacheFor</c>): the data is used as it is for
/// <paramref name="FreshFor"/>, then for <paramref name="StaleFor"/> more it is used while a background refresh replaces it.
/// </summary>
/// <param name="FreshFor">How long a loaded value is used without loading again.</param>
/// <param name="StaleFor">How much longer it may be shown while it refreshes in the background; zero means never.</param>
public sealed record RouteLoaderCacheMetadata(TimeSpan FreshFor, TimeSpan StaleFor);

/// <summary>
/// Fills a named outlet (<c>&lt;ModRouterOutlet Name="..."&gt;</c>) while the route level is on screen (added by <c>RouteMap.Slot</c>):
/// <paramref name="Component"/> is rendered in the outlet <paramref name="Name"/>, wherever the layout places it.
/// </summary>
/// <param name="Name">The outlet's name (case-insensitive).</param>
/// <param name="Component">The component rendered there; it receives the level's route parameters like the page does.</param>
public sealed record RouteSlotMetadata(string Name, Type Component);

/// <summary>
/// Keeps the page of a route level alive when it is left (added by <c>RouteMap.KeepAlive</c>): its component stays rendered, hidden,
/// with its state, and is shown again when the same route (and route values) comes back.
/// </summary>
/// <param name="Max">How many hidden instances of this route one outlet keeps; the least recently shown is dropped first.</param>
public sealed record RouteKeepAliveMetadata(int Max);

/// <summary>Declares the loader type of a route level (added by <c>RouteMap.Loader&lt;T&gt;()</c>).</summary>
public sealed record RouteLoaderMetadata(Type LoaderType, RouteReloadPolicy Policy);

/// <summary>
/// Marks a definition added by <c>RouteMap.Redirect</c>: a URL that never renders, only forwards to the named route.
/// </summary>
public sealed record RouteRedirectMetadata(string ToRoute, bool PreserveQuery, bool PreserveFragment, bool Permanent);

/// <summary>What the router does when the URL matched an alias template.</summary>
public enum AliasBehavior
{
    /// <summary>Render at the alias URL (the default, and today's behavior).</summary>
    Render,

    /// <summary>Redirect to the canonical URL of the same route.</summary>
    RedirectToCanonical,
}

/// <summary>Declares the alias behavior of a route level (added by <c>RouteMap.CanonicalAlias()</c>).</summary>
public sealed record RouteAliasMetadata(AliasBehavior Behavior);

/// <summary>The component type of a redirect definition. Never rendered and never validated as a component.</summary>
internal sealed class RedirectOnly;
