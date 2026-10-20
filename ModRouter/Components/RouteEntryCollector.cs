using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using ModRouter.Authorization;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Guards;
using ModRouter.Core.Loading;
using ModRouter.Core.Matching;
using ModRouter.Discovery;

namespace ModRouter.Components;

/// <summary>
/// One <see cref="ModRouteEntry"/> as registered: a snapshot of its parameters and its child nodes. Entries register in
/// level order (Blazor initializes all siblings before any child), so a node cannot build its definition when it
/// registers; the collector materializes the definitions when it closes.
/// </summary>
internal sealed class RouteEntryNode
{
    public required string Path { get; init; }

    public Type? Component { get; init; }

    public string? Name { get; init; }

    public string? Title { get; init; }

    public Func<RouteParameters, string>? TitleFunc { get; init; }

    public IReadOnlyList<string> Aliases { get; init; } = [];

    public bool IsIndex { get; init; }

    public bool IsGroup { get; init; }

    public int? KeepAlive { get; init; }

    public IReadOnlyDictionary<string, Type> Slots { get; init; } = new Dictionary<string, Type>();

    public string? Breadcrumb { get; init; }

    public bool BreadcrumbHidden { get; init; }

    public Type? Guard { get; init; }

    public Type? LeaveGuard { get; init; }

    public TimeSpan? CacheFreshFor { get; init; }

    public TimeSpan? CacheStaleFor { get; init; }

    public Type? Loader { get; init; }

    public RouteReloadPolicy ReloadPolicy { get; init; }

    public string? Focus { get; init; }

    public bool NoAnnounce { get; init; }

    public bool CanonicalAlias { get; init; }

    public RouteComponentReuse Reuse { get; init; }

    public bool Authorize { get; init; }

    public string? Roles { get; init; }

    public string? Policy { get; init; }

    public bool AllowAnonymous { get; init; }

    /// <summary>True when the entry requires authorization: <see cref="Authorize"/>, or a <see cref="Roles"/> or <see cref="Policy"/> that implies it.</summary>
    public bool RequiresAuthorization => Authorize || Roles is not null || Policy is not null;

    public List<RouteEntryNode> Children { get; } = [];

    /// <summary>The entry's tag reconstructed from its parameters, used as the definition's source and in errors.</summary>
    public string Describe()
    {
        var text = new StringBuilder("<ModRouteEntry Path=\"").Append(Path).Append('"');
        text.Append(" Component=\"").Append(Component?.Name ?? "(none)").Append('"');
        Append(text, "Name", Name);
        Append(text, "Title", Title);

        if (TitleFunc is not null)
        {
            text.Append(" TitleFunc=\"...\"");
        }

        if (Aliases.Count > 0)
        {
            Append(text, "Aliases", string.Join(",", Aliases));
        }

        Flag(text, "Index", IsIndex);
        Flag(text, "Group", IsGroup);
        Append(text, "KeepAlive", KeepAlive?.ToString(System.Globalization.CultureInfo.InvariantCulture));

        if (Slots.Count > 0)
        {
            Append(text, "Slots", string.Join(",", Slots.Select(slot => $"{slot.Key}={slot.Value.Name}")));
        }

        Append(text, "Breadcrumb", Breadcrumb);
        Flag(text, "BreadcrumbHidden", BreadcrumbHidden);
        Append(text, "Guard", Guard?.Name);
        Append(text, "LeaveGuard", LeaveGuard?.Name);
        Append(text, "CacheFreshFor", CacheFreshFor?.ToString());
        Append(text, "CacheStaleFor", CacheStaleFor?.ToString());
        Append(text, "Loader", Loader?.Name);

        if (Loader is not null && ReloadPolicy != RouteReloadPolicy.WhenParametersChange)
        {
            Append(text, "ReloadPolicy", ReloadPolicy.ToString());
        }

        Append(text, "Focus", Focus);
        Flag(text, "NoAnnounce", NoAnnounce);
        Flag(text, "CanonicalAlias", CanonicalAlias);

        if (Reuse != RouteComponentReuse.ReuseWhenComponentTypeMatches)
        {
            Append(text, "Reuse", Reuse.ToString());
        }

        Flag(text, "Authorize", Authorize);
        Append(text, "Roles", Roles);
        Append(text, "Policy", Policy);
        Flag(text, "AllowAnonymous", AllowAnonymous);

        return text.Append('>').ToString();
    }

    /// <summary>True when <paramref name="other"/> is the same declaration (every parameter equal; delegates by reference).</summary>
    public bool SameAs(RouteEntryNode other)
        => string.Equals(Describe(), other.Describe(), StringComparison.Ordinal)
            && ReferenceEquals(TitleFunc, other.TitleFunc)
            && Component == other.Component
            && Guard == other.Guard
            && LeaveGuard == other.LeaveGuard
            && CacheFreshFor == other.CacheFreshFor
            && CacheStaleFor == other.CacheStaleFor
            && Loader == other.Loader
            && ReloadPolicy == other.ReloadPolicy;

    /// <summary>The tags of this node and its descendants, one per line, indented by level.</summary>
    public void Describe(StringBuilder text, int depth)
    {
        text.Append(' ', depth * 2).AppendLine(Describe());

        foreach (var child in Children)
        {
            child.Describe(text, depth + 1);
        }
    }

    public RouteDefinition ToDefinition(List<RouteCompilationError> errors)
    {
        var source = "markup: " + Describe();

        if (Component is null)
        {
            errors.Add(Error("needs a Component.", source));
        }

        if (IsIndex && Path.Length > 0)
        {
            errors.Add(Error("is an Index entry, which renders at its parent's path, so it cannot have a Path.", source));
        }

        if (IsGroup && (Path.Length > 0 || IsIndex))
        {
            errors.Add(Error("is a Group entry, which adds no path segment, so it cannot have a Path and cannot also be an Index entry.", source));
        }

        if (Guard is not null && !IsUsable(Guard, typeof(IRouteGuard)))
        {
            errors.Add(Error($"names the guard {Guard.FullName}, which is not a concrete class implementing IRouteGuard.", source));
        }

        if (CacheFreshFor is { } fresh && fresh <= TimeSpan.Zero || CacheStaleFor is { } stale && stale < TimeSpan.Zero)
        {
            errors.Add(Error("has a CacheFreshFor that is not positive or a CacheStaleFor that is negative.", source));
        }

        if (CacheStaleFor is not null && CacheFreshFor is null)
        {
            errors.Add(Error("sets CacheStaleFor without CacheFreshFor.", source));
        }

        if (LeaveGuard is not null && !IsUsable(LeaveGuard, typeof(IRouteLeaveGuard)))
        {
            errors.Add(Error($"names the leave guard {LeaveGuard.FullName}, which is not a concrete class implementing IRouteLeaveGuard.", source));
        }

        if (Loader is not null && !IsUsable(Loader, typeof(IRouteLoader)))
        {
            errors.Add(Error($"names the loader {Loader.FullName}, which is not a concrete class implementing IRouteLoader.", source));
        }

        if (Breadcrumb is not null && BreadcrumbHidden)
        {
            errors.Add(Error("has both a Breadcrumb label and BreadcrumbHidden; use one of them.", source));
        }

        if (Title is not null && TitleFunc is not null)
        {
            errors.Add(Error("has both a Title and a TitleFunc; use one of them.", source));
        }

        if (AllowAnonymous && RequiresAuthorization)
        {
            errors.Add(Error("declares AllowAnonymous together with Authorize, Roles or Policy; use one of them.", source));
        }

        var metadata = new List<object>();

        if (RequiresAuthorization)
        {
            metadata.Add(new RouteAuthorizeMetadata(Roles, Policy));
        }

        if (AllowAnonymous)
        {
            metadata.Add(new RouteAllowAnonymousMetadata());
        }

        if (Guard is not null)
        {
            metadata.Add(new RouteGuardMetadata(Guard));
        }

        if (LeaveGuard is not null)
        {
            metadata.Add(new RouteLeaveGuardMetadata(LeaveGuard));
        }

        if (CacheFreshFor is { } freshFor)
        {
            metadata.Add(new RouteLoaderCacheMetadata(freshFor, CacheStaleFor ?? TimeSpan.Zero));
        }

        if (Loader is not null)
        {
            metadata.Add(new RouteLoaderMetadata(Loader, ReloadPolicy));
        }

        if (Breadcrumb is not null || BreadcrumbHidden)
        {
            metadata.Add(new RouteBreadcrumbMetadata(Breadcrumb, null, BreadcrumbHidden));
        }

        if (Focus is not null)
        {
            metadata.Add(new RouteFocusMetadata(Focus));
        }

        if (NoAnnounce)
        {
            metadata.Add(new RouteAnnounceMetadata(false));
        }

        if (CanonicalAlias)
        {
            metadata.Add(new RouteAliasMetadata(AliasBehavior.RedirectToCanonical));
        }

        foreach (var slot in Slots)
        {
            metadata.Add(new RouteSlotMetadata(slot.Key, slot.Value));
        }

        if (KeepAlive is { } keepAlive)
        {
            metadata.Add(new RouteKeepAliveMetadata(keepAlive));
        }

        return new RouteDefinition
        {
            Template = IsIndex || IsGroup ? string.Empty : Path,
            Name = Name,
            Component = Component ?? typeof(RedirectOnly),
            StaticTitle = Title,
            Title = TitleFunc,
            Children = [.. Children.Select(child => child.ToDefinition(errors))],
            Metadata = RouteMetadataCollection.Create([.. metadata]),
            Aliases = Aliases,
            IsIndex = IsIndex,
            IsGroup = IsGroup,
            ReusePolicy = Reuse,
            Source = source,
        };
    }

    RouteCompilationError Error(string what, string source)
        => new("RT0023", $"The markup route entry {what} Route: {Name ?? "(unnamed)"} [template '{Path}', source {source}].", Name, Path, source, Component);

    static bool IsUsable(Type type, Type contract) => contract.IsAssignableFrom(type) && type is { IsAbstract: false, IsGenericTypeDefinition: false };

    static void Append(StringBuilder text, string name, string? value)
    {
        if (value is not null)
        {
            text.Append(' ').Append(name).Append("=\"").Append(value).Append('"');
        }
    }

    static void Flag(StringBuilder text, string name, bool value)
    {
        if (value)
        {
            text.Append(' ').Append(name);
        }
    }
}

/// <summary>
/// Cascaded by <see cref="ModRouter"/> to its <c>Routes</c> fragment. <see cref="ModRouteEntry"/> and
/// <see cref="ModRoutePages"/> register here while the first pass renders; the router closes the collector in its first
/// after-render callback, which materializes the definitions (markup entries, then discovered pages) after the startup
/// ones. An entry that registers after the close is a programming error: <c>&lt;Routes&gt;</c> content must be static.
/// </summary>
internal sealed class RouteEntryCollector
{
    readonly List<RouteEntryNode> _roots = [];
    readonly List<(Assembly? Assembly, Func<Type, bool>? Filter)> _pages = [];

    public bool IsClosed { get; private set; }

    public void Register(RouteEntryNode node, RouteEntryNode? parent)
    {
        ThrowIfClosed(node.Describe());
        (parent?.Children ?? _roots).Add(node);
    }

    public void RegisterPages(Assembly? assembly, Func<Type, bool>? filter)
    {
        ThrowIfClosed($"<ModRoutePages Assembly=\"{assembly?.GetName().Name ?? "(none)"}\">");
        _pages.Add((assembly, filter));
    }

    /// <summary>
    /// Closes the collector and returns the startup definitions followed by the markup ones. <paramref name="signature"/>
    /// identifies the markup declarations (the holder treats a repeat freeze with the same signature as a remount).
    /// </summary>
    /// <exception cref="RouteCompilationException">An entry is invalid (RT0023) or a discovered page is (RT0019 to RT0022, RT0024).</exception>
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "ModRoutePages documents the requirement: " + RouteMapPagesExtensions.TrimmingMessage)]
    public IReadOnlyList<RouteDefinition> Close(IReadOnlyList<RouteDefinition> startup, out string signature)
    {
        IsClosed = true;

        var errors = new List<RouteCompilationError>();
        var definitions = new List<RouteDefinition>(startup);
        var text = new StringBuilder("markup:").AppendLine();

        foreach (var root in _roots)
        {
            definitions.Add(root.ToDefinition(errors));
            root.Describe(text, 0);
        }

        foreach (var (assembly, _) in _pages)
        {
            if (assembly is null)
            {
                errors.Add(new RouteCompilationError("RT0023", "The markup <ModRoutePages> entry needs an Assembly.", Source: "markup: <ModRoutePages>"));
            }

            text.Append("<ModRoutePages Assembly=\"").Append(assembly?.FullName).AppendLine("\">");
        }

        if (errors.Count > 0)
        {
            throw new RouteCompilationException(errors);
        }

        foreach (var (assembly, filter) in _pages)
        {
            foreach (var (parent, definition) in Discover(assembly!, filter, definitions))
            {
                if (parent is null)
                {
                    definitions.Add(definition);
                }
                else if (RouteMap.TryNest(definitions, parent, definition, out var replaced))
                {
                    definitions = replaced;
                }
            }
        }

        signature = text.ToString();

        return definitions;
    }

    /// <summary>
    /// Discovery that cannot even start (the assembly's types fail to load, the user's filter throws) is turned into a
    /// compilation error too: it happens in the router's after-render callback, where an escaping exception would leave
    /// the user on the bare Loading forever instead of on the rendered error.
    /// </summary>
    /// <exception cref="RouteCompilationException">A page is invalid (RT0019 to RT0022, RT0024), or discovery threw (RT0023, with the cause as the inner exception).</exception>
    static IReadOnlyList<(Type? Parent, RouteDefinition Definition)> Discover(Assembly assembly, Func<Type, bool>? filter, List<RouteDefinition> definitions)
    {
        try
        {
            return PageDiscovery.Discover(assembly, filter, component => RouteMap.FullTemplatesOf(definitions, component));
        }
        catch (Exception exception) when (exception is not RouteCompilationException)
        {
            var tag = $"<ModRoutePages Assembly=\"{assembly.GetName().Name}\">";
            var what = exception is ReflectionTypeLoadException or FileNotFoundException or FileLoadException or BadImageFormatException or TypeLoadException
                ? "the assembly's exported types could not be loaded"
                : "its Filter threw";
            var cause = exception is ReflectionTypeLoadException { LoaderExceptions: { Length: > 0 } loaderExceptions }
                ? string.Join(" ", loaderExceptions.Where(e => e is not null).Select(e => e!.Message))
                : exception.Message;

            throw new RouteCompilationException([new RouteCompilationError("RT0023", $"The markup {tag} entry could not discover its pages: {what}. {exception.GetType().Name}: {cause}", Source: "markup: " + tag)], exception);
        }
    }

    void ThrowIfClosed(string tag)
    {
        if (IsClosed)
        {
            throw new InvalidOperationException($"The route table is already compiled: {tag} rendered after ModRouter's first render. Markup route entries must all be present in the first render of <Routes>; an entry behind a condition that becomes true later, or added by a re-render, cannot be registered.");
        }
    }
}
