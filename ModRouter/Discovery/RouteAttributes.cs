using System.Diagnostics.CodeAnalysis;
using ModRouter.Core.Definitions;
using ModRouter.Core.Guards;
using ModRouter.Core.Loading;
using ModRouter.Navigation;

namespace ModRouter.Discovery;

/// <summary>The route name of a discovered page (<c>name:</c> in the fluent map): what <c>NavigateToRoute</c>, <c>GetRouteUrl</c> and <c>RouteLink Route=</c> refer to.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RouteNameAttribute(string name) : Attribute
{
    /// <summary>The route name; page discovery fails (RT0022) when it is null, empty or white space.</summary>
    public string Name { get; } = name;
}

/// <summary>The static title of a discovered page (<c>title:</c> in the fluent map). A computed title stays in the fluent map or markup.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RouteTitleAttribute(string title) : Attribute
{
    /// <summary>The title; page discovery fails (RT0022) when it is null, empty or white space.</summary>
    public string Title { get; } = title;
}

/// <summary>
/// The breadcrumb of a discovered page: a fixed <see cref="Label"/> (<c>.Breadcrumb(label)</c>) or
/// <c>[RouteBreadcrumb(Hidden = true)]</c> to leave the level out (<c>.Breadcrumb(hidden: true)</c>). One of the two, never both.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RouteBreadcrumbAttribute : Attribute
{
    /// <summary>For <c>Hidden = true</c>, which leaves the level out of breadcrumb trails.</summary>
    public RouteBreadcrumbAttribute()
    {
    }

    /// <summary>Gives the level a fixed breadcrumb <paramref name="label"/>.</summary>
    public RouteBreadcrumbAttribute(string label) => Label = label;

    /// <summary>The fixed breadcrumb label; null when the attribute only hides the level.</summary>
    public string? Label { get; }

    /// <summary>
    /// True leaves the level out of breadcrumb trails. Page discovery fails (RT0022) when the attribute has both a label
    /// and <c>Hidden</c>, or neither.
    /// </summary>
    public bool Hidden { get; set; }
}

/// <summary>A guard of a discovered page (<c>.Guard&lt;T&gt;()</c>); repeat it for several guards, which run in attribute order.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class RouteGuardAttribute([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type guardType) : Attribute
{
    /// <summary>
    /// The guard class; page discovery fails (RT0022) unless it is a concrete class implementing
    /// <see cref="IRouteGuard"/>. Resolved from the navigation scope's services, or created with them.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type GuardType { get; } = guardType;
}

/// <summary>The generic form of <see cref="RouteGuardAttribute"/>: <c>[RouteGuard&lt;SignedInGuard&gt;]</c>.</summary>
public sealed class RouteGuardAttribute<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>() : RouteGuardAttribute(typeof(T))
    where T : class, IRouteGuard;

/// <summary>A leave guard of a discovered page (<c>.LeaveGuard&lt;T&gt;()</c>); repeat it for several guards, which run in attribute order.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class RouteLeaveGuardAttribute([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type guardType) : Attribute
{
    /// <summary>
    /// The leave guard class; page discovery fails (RT0022) unless it is a concrete class implementing
    /// <see cref="IRouteLeaveGuard"/>. Resolved from the navigation scope's services, or created with them.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type GuardType { get; } = guardType;
}

/// <summary>The generic form of <see cref="RouteLeaveGuardAttribute"/>: <c>[RouteLeaveGuard&lt;UnsavedChangesGuard&gt;]</c>.</summary>
public sealed class RouteLeaveGuardAttribute<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>() : RouteLeaveGuardAttribute(typeof(T))
    where T : class, IRouteLeaveGuard;

/// <summary>
/// Fills a named outlet while the discovered page is on screen (<c>.Slot(name, component)</c>); repeat it for several
/// outlets. The component must implement <c>IComponent</c> (RT0022 otherwise).
/// </summary>
/// <param name="name">The outlet's name.</param>
/// <param name="component">The component rendered in it.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RouteSlotAttribute(string name, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type component) : Attribute
{
    /// <summary>The name of the outlet this component fills.</summary>
    public string Name { get; } = name;

    /// <summary>The component rendered in the outlet.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type Component { get; } = component;
}

/// <summary>
/// Keeps the discovered page alive when it is left (<c>.KeepAlive(max)</c>): hidden, with its state, until the same route comes back.
/// </summary>
/// <param name="max">How many hidden instances of the route one outlet keeps; at least 1 (RT0022 otherwise).</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RouteKeepAliveAttribute(int max = 3) : Attribute
{
    /// <summary>How many hidden instances of the route one outlet keeps.</summary>
    public int Max { get; } = max;
}

/// <summary>The loader of a discovered page (<c>.Loader&lt;T&gt;(policy)</c>); at most one per page.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class RouteLoaderAttribute([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type loaderType, RouteReloadPolicy policy = RouteReloadPolicy.WhenParametersChange) : Attribute
{
    /// <summary>
    /// The loader class; page discovery fails (RT0022) unless it is a concrete class implementing
    /// <see cref="IRouteLoader"/>. Resolved from the navigation scope's services, or created with them.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type LoaderType { get; } = loaderType;

    /// <summary>When the loader runs again for a navigation that keeps the page's level.</summary>
    public RouteReloadPolicy Policy { get; } = policy;
}

/// <summary>The generic form of <see cref="RouteLoaderAttribute"/>: <c>[RouteLoader&lt;CustomerLoader&gt;(RouteReloadPolicy.Never)]</c>.</summary>
public sealed class RouteLoaderAttribute<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(RouteReloadPolicy policy = RouteReloadPolicy.WhenParametersChange) : RouteLoaderAttribute(typeof(T), policy)
    where T : class, IRouteLoader;

/// <summary>
/// Caches the critical data of the page's loader (<c>.CacheFor(fresh, stale)</c>): used as it is for
/// <see cref="FreshSeconds"/>, then for <see cref="StaleSeconds"/> more while a background refresh replaces it. The page
/// needs a <see cref="RouteLoaderAttribute"/> (RT0026 otherwise).
/// </summary>
/// <param name="freshSeconds">How long a loaded value is used without loading again; must be positive.</param>
/// <param name="staleSeconds">How much longer it may be shown while it refreshes; zero (the default) means never.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RouteLoaderCacheAttribute(int freshSeconds, int staleSeconds = 0) : Attribute
{
    /// <summary>How long a loaded value is used without loading again, in seconds.</summary>
    public int FreshSeconds { get; } = freshSeconds;

    /// <summary>How much longer it may be shown while it refreshes in the background, in seconds.</summary>
    public int StaleSeconds { get; } = staleSeconds;
}

/// <summary>
/// Carries the critical data of the page's loader from the prerender to the interactive pass, so the loader does not run
/// twice (<c>.PersistLoaderData&lt;T&gt;()</c>); use <see cref="RoutePersistLoaderDataAttribute{T}"/>. The page needs a
/// <see cref="RouteLoaderAttribute"/> (RT0022 otherwise).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public abstract class RoutePersistLoaderDataAttribute : Attribute
{
    internal RoutePersistLoaderDataAttribute()
    {
    }

    internal abstract RoutePersistedDataMetadata CreateMetadata();
}

/// <summary>
/// The generic form of <see cref="RoutePersistLoaderDataAttribute"/>: <c>[RoutePersistLoaderData&lt;Customer&gt;]</c> on a page
/// whose loader returns a <typeparamref name="TData"/>. The data is stored as JSON in the persistent component state; a
/// loader that also returns deferred values or streams loads again on the interactive pass.
/// </summary>
/// <typeparam name="TData">The type of the loader's critical data.</typeparam>
public sealed class RoutePersistLoaderDataAttribute<TData> : RoutePersistLoaderDataAttribute
{
    internal override RoutePersistedDataMetadata CreateMetadata() => new(typeof(TData), new RouteDataPersistence<TData>());
}

/// <summary>The element (a CSS selector) to focus after navigating to a discovered page (<c>.Focus(selector)</c>).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RouteFocusAttribute(string selector) : Attribute
{
    /// <summary>
    /// The CSS selector of the element to focus; page discovery fails (RT0022) when it is null, empty or white space.
    /// </summary>
    public string Selector { get; } = selector;
}

/// <summary>Switches the screen-reader announcement off for a discovered page (<c>.NoAnnounce()</c>).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RouteNoAnnounceAttribute : Attribute;

/// <summary>
/// Makes a discovered page the index of its <see cref="RouteParentAttribute"/> parent (<c>.Index(...)</c>): it renders at the
/// parent's own path, so its <c>@page</c> template is the parent's template.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RouteIndexAttribute : Attribute;

/// <summary>Makes the alias URLs of a discovered page (its further <c>@page</c> templates) redirect to the canonical one (<c>.CanonicalAlias()</c>).</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RouteCanonicalAliasAttribute : Attribute;

/// <summary>
/// Nests a discovered page under another page (one discovered in the same <c>AddPages</c> call, or a route the fluent map
/// declared with the same component type). The page's <c>@page</c> template is its full URL, which must extend the
/// parent's template; the remainder is the child's relative template (a template without a leading '/' is taken as
/// relative as it is). The child renders in the parent's <c>ModRouterOutlet</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class RouteParentAttribute([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type parent) : Attribute
{
    /// <summary>
    /// The parent page's component type. Page discovery fails with RT0019 when it is neither a page of the same
    /// <c>AddPages</c> call nor a route the fluent map declared, with RT0024 when the map declares it at more than one
    /// route, with RT0020 when parents form a cycle, and with RT0022 when the page's template does not extend the parent's.
    /// </summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type Parent { get; } = parent;
}
