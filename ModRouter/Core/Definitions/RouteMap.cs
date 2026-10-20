using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using ModRouter.Core.Guards;
using ModRouter.Core.Loading;
using ModRouter.Core.Matching;

namespace ModRouter.Core.Definitions;

/// <summary>
/// Fluent builder for <see cref="RouteDefinition"/>s. Overloads take either a static title or a title factory.
/// Child routes are declared inside the <c>children</c> callback with relative templates.
/// </summary>
public sealed class RouteMap
{
    readonly List<RouteDefinition> _routes = [];

    RouteMap()
    {
    }

    /// <summary>Starts an empty map. The constructor is private so a map is always created here.</summary>
    public static RouteMap Create() => new();

    /// <summary>
    /// Declares a route with a fixed title. Children are declared inside the <paramref name="children"/> callback with
    /// templates relative to this route's. <paramref name="name"/> registers the route for URL generation and
    /// redirects; <paramref name="aliases"/> are alternative templates that also match. <paramref name="sourceFile"/>
    /// and <paramref name="sourceLine"/> are filled in by the compiler from the call site and only appear in error
    /// messages.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    ///   <paramref name="template"/> or <paramref name="component"/> is null.
    /// </exception>
    public RouteMap Route(
        string template,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type component,
        string? name = null,
        string? title = null,
        IEnumerable<string>? aliases = null,
        RouteMetadataCollection? metadata = null,
        RouteComponentReuse reuse = RouteComponentReuse.ReuseWhenComponentTypeMatches,
        Action<RouteMap>? children = null,
        [CallerFilePath] string? sourceFile = null,
        [CallerLineNumber] int sourceLine = 0)
        => Add(template, component, name, title, null, aliases, metadata, reuse, children, false, sourceFile, sourceLine);

    /// <summary>
    /// Declares a route whose title is computed from the level's inherited route parameters; otherwise identical to the
    /// overload with a fixed title.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    ///   <paramref name="template"/> or <paramref name="component"/> is null.
    /// </exception>
    public RouteMap Route(
        string template,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type component,
        Func<RouteParameters, string> title,
        string? name = null,
        IEnumerable<string>? aliases = null,
        RouteMetadataCollection? metadata = null,
        RouteComponentReuse reuse = RouteComponentReuse.ReuseWhenComponentTypeMatches,
        Action<RouteMap>? children = null,
        [CallerFilePath] string? sourceFile = null,
        [CallerLineNumber] int sourceLine = 0)
        => Add(template, component, name, null, title, aliases, metadata, reuse, children, false, sourceFile, sourceLine);

    /// <summary>Declares the default child, rendered at the parent's own path.</summary>
    public RouteMap Index(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type component,
        string? name = null,
        string? title = null,
        RouteMetadataCollection? metadata = null,
        RouteComponentReuse reuse = RouteComponentReuse.ReuseWhenComponentTypeMatches,
        [CallerFilePath] string? sourceFile = null,
        [CallerLineNumber] int sourceLine = 0)
        => Add(string.Empty, component, name, title, null, null, metadata, reuse, null, true, sourceFile, sourceLine);

    /// <summary>
    /// Declares the default child with a title computed from the level's inherited route parameters; otherwise
    /// identical to the overload with a fixed title.
    /// </summary>
    public RouteMap Index(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type component,
        Func<RouteParameters, string> title,
        string? name = null,
        RouteMetadataCollection? metadata = null,
        RouteComponentReuse reuse = RouteComponentReuse.ReuseWhenComponentTypeMatches,
        [CallerFilePath] string? sourceFile = null,
        [CallerLineNumber] int sourceLine = 0)
        => Add(string.Empty, component, name, null, title, null, metadata, reuse, null, true, sourceFile, sourceLine);

    /// <summary>
    /// Declares a pathless group: <paramref name="layout"/> (a component with a <c>ModRouterOutlet</c>) wraps the routes declared in
    /// <paramref name="children"/> without adding a segment to their URLs. Use it to share a layout, a guard
    /// (<c>.Guard&lt;T&gt;()</c>, <c>.Authorize()</c>) or a loader between routes that do not share a path prefix. The children's
    /// templates are relative (no leading '/'); declare the group's own default page with an index route.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> or <paramref name="children"/> is null.</exception>
    public RouteMap Group(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type layout,
        Action<RouteMap> children,
        RouteComponentReuse reuse = RouteComponentReuse.ReuseWhenComponentTypeMatches,
        [CallerFilePath] string? sourceFile = null,
        [CallerLineNumber] int sourceLine = 0)
    {
        ArgumentNullException.ThrowIfNull(children);

        return Add(string.Empty, layout, null, null, null, null, null, reuse, children, false, sourceFile, sourceLine, isGroup: true);
    }

    /// <summary>
    /// Declares a URL that forwards to the named route instead of rendering. Path parameters of the same name are
    /// forwarded; the query and fragment are kept by default.
    /// </summary>
    public RouteMap Redirect(
        string from,
        string toRoute,
        bool preserveQuery = true,
        bool preserveFragment = true,
        bool permanent = false,
        [CallerFilePath] string? sourceFile = null,
        [CallerLineNumber] int sourceLine = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toRoute);

        return Add(
            from,
            typeof(RedirectOnly),
            null,
            null,
            null,
            null,
            RouteMetadataCollection.Create(new RouteRedirectMetadata(toRoute, preserveQuery, preserveFragment, permanent)),
            RouteComponentReuse.ReuseWhenComponentTypeMatches,
            null,
            false,
            sourceFile,
            sourceLine);
    }

    /// <summary>Attaches a guard to the route declared last in this map. Guards run parent-first, in declaration order.</summary>
    public RouteMap Guard<T>()
        where T : class, IRouteGuard
        => WithLast(new RouteGuardMetadata(typeof(T)), nameof(Guard));

    /// <summary>
    /// Attaches a leave guard to the route declared last in this map: asked before the level is left (an unsaved-changes
    /// check). Leave guards run before any enter guard, deepest level first, in declaration order; see
    /// <see cref="IRouteLeaveGuard"/> for when a level counts as left.
    /// </summary>
    public RouteMap LeaveGuard<T>()
        where T : class, IRouteLeaveGuard
        => WithLast(new RouteLeaveGuardMetadata(typeof(T)), nameof(LeaveGuard));

    /// <summary>Attaches a loader to the route declared last in this map (at most one per level).</summary>
    public RouteMap Loader<T>(RouteReloadPolicy policy = RouteReloadPolicy.WhenParametersChange)
        where T : class, IRouteLoader
        => WithLast(new RouteLoaderMetadata(typeof(T), policy), nameof(Loader));

    /// <summary>
    /// Caches the critical data of the loader of the route declared last: for <paramref name="freshFor"/> a loaded value is
    /// used without loading again (a link back to a page is instant); for <paramref name="staleFor"/> more it is used while a
    /// background refresh replaces it (the page then updates by itself); after that the loader runs. The cache holds one value
    /// per level, its inherited route parameters and the query, for the life of the router's scope. Only plain critical
    /// data is cached: a result with deferred values or streams is not. Needs a loader on the same route (RT0026).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="freshFor"/> is not positive, or <paramref name="staleFor"/> is negative.</exception>
    public RouteMap CacheFor(TimeSpan freshFor, TimeSpan? staleFor = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(freshFor, TimeSpan.Zero);

        if (staleFor is { } stale)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(stale, TimeSpan.Zero);
        }

        return WithLast(new RouteLoaderCacheMetadata(freshFor, staleFor ?? TimeSpan.Zero), nameof(CacheFor));
    }

    /// <summary>
    /// Fills the named outlet <paramref name="name"/> (<c>&lt;ModRouterOutlet Name="..."&gt;</c>, placed anywhere in a layout or page) with
    /// <paramref name="component"/> while the route declared last is on screen, for a contextual sidebar, toolbar or header. The
    /// outlet shows the slot of the deepest matched level that declares one of that name. The component receives the
    /// level's route parameters and loaded data like the page does. A slot is not part of the URL.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="component"/> is null.</exception>
    public RouteMap Slot(string name, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type component)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(component);

        return WithLast(new RouteSlotMetadata(name, component), nameof(Slot));
    }

    /// <summary>
    /// Keeps the page of the route declared last alive when the user leaves it: the component stays in the page, hidden and inert, with
    /// its state (form input, scroll position inside it, a running timer), and is shown again when the same route comes back. Each
    /// distinct set of route values is its own entry (<c>/customers/1</c> and <c>/customers/2</c> are two), up to
    /// <paramref name="max"/> hidden ones per outlet, the least recently shown dropped first. The cache lives as long as the
    /// outlet that renders the page: leaving the section that holds the outlet drops it. A page can implement
    /// <see cref="ModRouter.Components.IKeepAliveRoute"/> to stop and resume work while hidden.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="max"/> is less than 1.</exception>
    public RouteMap KeepAlive(int max = 3)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);

        return WithLast(new RouteKeepAliveMetadata(max), nameof(KeepAlive));
    }

    /// <summary>Gives the route declared last a fixed breadcrumb label.</summary>
    public RouteMap Breadcrumb(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        return WithLast(new RouteBreadcrumbMetadata(label, null, false), nameof(Breadcrumb));
    }

    /// <summary>Gives the route declared last a breadcrumb label computed from the level's inherited route parameters.</summary>
    public RouteMap Breadcrumb(Func<RouteParameters, string> label)
    {
        ArgumentNullException.ThrowIfNull(label);

        return WithLast(new RouteBreadcrumbMetadata(null, label, false), nameof(Breadcrumb));
    }

    /// <summary>Leaves the route declared last out of breadcrumb trails (<c>Breadcrumb(hidden: true)</c>).</summary>
    public RouteMap Breadcrumb(bool hidden)
        => WithLast(new RouteBreadcrumbMetadata(null, null, hidden), nameof(Breadcrumb));

    /// <summary>The element to focus (a CSS selector) after a navigation to the route declared last, instead of the router's default.</summary>
    public RouteMap Focus(string selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);

        return WithLast(new RouteFocusMetadata(selector), nameof(Focus));
    }

    /// <summary>Switches the screen-reader announcement off for the route declared last.</summary>
    public RouteMap NoAnnounce()
        => WithLast(new RouteAnnounceMetadata(false), nameof(NoAnnounce));

    /// <summary>
    /// Makes the route declared last redirect its alias URLs to the canonical URL instead of rendering at them.
    /// </summary>
    public RouteMap CanonicalAlias()
        => WithLast(new RouteAliasMetadata(AliasBehavior.RedirectToCanonical), nameof(CanonicalAlias));

    /// <summary>Returns a snapshot of the definitions declared so far.</summary>
    public IReadOnlyList<RouteDefinition> Build() => _routes.ToArray();

    /// <summary>Appends a definition built elsewhere (page discovery).</summary>
    internal void Add(RouteDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        _routes.Add(definition);
    }

    /// <summary>The full canonical template of every route declared so far (at any depth) that renders <paramref name="component"/>, in declaration order: empty when none does, more than one when the component is declared at several routes.</summary>
    internal IReadOnlyList<string> FullTemplatesOf(Type component) => FullTemplatesOf(_routes, component);

    /// <summary>The full canonical template of every definition in <paramref name="routes"/> (at any depth) that renders <paramref name="component"/>, in declaration order.</summary>
    internal static IReadOnlyList<string> FullTemplatesOf(IReadOnlyList<RouteDefinition> routes, Type component)
    {
        var found = new List<string>();

        CollectFullTemplates(routes, component, null, found);

        return found;
    }

    /// <summary>Appends <paramref name="child"/> to the children of the route (at any depth) whose component is <paramref name="parentComponent"/>.</summary>
    internal void NestUnder(Type parentComponent, RouteDefinition child)
    {
        if (!TryNest(_routes, parentComponent, child, out var replaced))
        {
            throw new InvalidOperationException($"No route declared so far renders {parentComponent.FullName}.");
        }

        _routes.Clear();
        _routes.AddRange(replaced);
    }

    static void CollectFullTemplates(IEnumerable<RouteDefinition> routes, Type component, string? parentTemplate, List<string> found)
    {
        foreach (var route in routes)
        {
            var full = parentTemplate is null
                ? route.Template
                : route.Template.Length == 0 ? parentTemplate : parentTemplate.TrimEnd('/') + "/" + route.Template;

            if (route.Component == component)
            {
                found.Add(full);
            }

            CollectFullTemplates(route.Children, component, full, found);
        }
    }

    /// <summary>Rebuilds the (immutable) definitions on the path to the parent with the child appended; false when no definition renders the parent.</summary>
    internal static bool TryNest(IReadOnlyList<RouteDefinition> routes, Type parentComponent, RouteDefinition child, out List<RouteDefinition> replaced)
    {
        replaced = [.. routes];

        for (var i = 0; i < replaced.Count; i++)
        {
            var route = replaced[i];

            if (route.Component == parentComponent)
            {
                replaced[i] = route.WithChildren([.. route.Children, child]);

                return true;
            }

            if (TryNest(route.Children, parentComponent, child, out var children))
            {
                replaced[i] = route.WithChildren(children);

                return true;
            }
        }

        return false;
    }

    /// <summary>Appends <paramref name="item"/> to the metadata of the route declared last; <paramref name="method"/> names the caller in the error when there is none.</summary>
    internal RouteMap WithLast(object item, string method)
    {
        if (_routes.Count == 0)
        {
            throw new InvalidOperationException($"RouteMap.{method}() applies to the route declared just before it, but no route has been declared yet.");
        }

        _routes[^1] = _routes[^1].WithMetadata(item);

        return this;
    }

    RouteMap Add(
        string template,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type component,
        string? name,
        string? staticTitle,
        Func<RouteParameters, string>? title,
        IEnumerable<string>? aliases,
        RouteMetadataCollection? metadata,
        RouteComponentReuse reuse,
        Action<RouteMap>? children,
        bool isIndex,
        string? sourceFile,
        int sourceLine,
        bool isGroup = false)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(component);

        IReadOnlyList<RouteDefinition> childDefinitions = [];

        if (children is not null)
        {
            var nested = new RouteMap();
            children(nested);
            childDefinitions = nested.Build();
        }

        _routes.Add(new RouteDefinition
        {
            Template = template,
            Name = name,
            Component = component,
            StaticTitle = staticTitle,
            Title = title,
            Children = childDefinitions,
            Metadata = metadata ?? RouteMetadataCollection.Empty,
            Aliases = aliases?.ToArray() ?? Array.Empty<string>(),
            IsIndex = isIndex,
            IsGroup = isGroup,
            ReusePolicy = reuse,
            Source = sourceFile is null ? null : $"fluent: {Path.GetFileName(sourceFile)}:{sourceLine}",
        });

        return this;
    }
}
