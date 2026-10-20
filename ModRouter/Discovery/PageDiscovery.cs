using System.Reflection;
using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Guards;
using ModRouter.Core.Loading;
using Microsoft.AspNetCore.Components;

namespace ModRouter.Discovery;

/// <summary>
/// Turns the routable components of an assembly into <see cref="RouteDefinition"/>s. The only code in the library that
/// enumerates an assembly's types.
/// </summary>
internal static class PageDiscovery
{
    /// <summary>One routable component with its attributes, before it becomes a definition.</summary>
    sealed class Page(Type type, string source, IReadOnlyList<string> templates, Type? parent)
    {
        public Type Type { get; } = type;

        public string Source { get; } = source;

        /// <summary>The templates as written, <c>@page</c> order: the first is the route's own.</summary>
        public IReadOnlyList<string> Templates { get; } = templates;

        public Type? Parent { get; } = parent;

        public bool IsIndex { get; } = type.IsDefined(typeof(RouteIndexAttribute), inherit: false);

        public List<Page> Children { get; } = [];
    }

    /// <summary>
    /// Discovers the pages of <paramref name="assembly"/>. Returns the root definitions (parent null) and the definitions to
    /// nest under a component the map already declares (<paramref name="mapTemplates"/> gives the full template of every route
    /// that renders it: none when the map declares no such component, more than one is RT0024).
    /// </summary>
    /// <exception cref="RouteCompilationException">An attribute is invalid; every problem is reported at once.</exception>
    public static IReadOnlyList<(Type? Parent, RouteDefinition Definition)> Discover(Assembly assembly, Func<Type, bool>? filter, Func<Type, IReadOnlyList<string>> mapTemplates)
    {
        var errors = new List<RouteCompilationError>();
        var pages = Collect(assembly, filter, errors);
        var byType = pages.ToDictionary(p => p.Type);
        var result = new List<(Type?, RouteDefinition)>();
        var mapParents = new Dictionary<Page, string>();

        foreach (var page in pages.Where(p => p.Parent is not null))
        {
            if (byType.TryGetValue(page.Parent!, out var parent))
            {
                parent.Children.Add(page);
            }
            else if (mapTemplates(page.Parent!) is var templates && templates.Count == 1)
            {
                mapParents[page] = templates[0];
            }
            else if (templates.Count > 1)
            {
                errors.Add(Error("RT0024", $"[RouteParent] names {page.Parent!.FullName}, which the map declares at {templates.Count} routes ({string.Join(", ", templates.Select(t => $"'{t}'"))}); a page can nest under only one. Give the parent a route of its own, or name as the parent a component that is declared once, or declare the child in the fluent map.", page));
            }
            else
            {
                errors.Add(Error("RT0019", $"[RouteParent] names {page.Parent!.FullName}, which is not a page of this AddPages call and not a route of the map. Discover the parent in the same AddPages call, declare it in the fluent map before AddPages, or remove the attribute.", page));
            }
        }

        foreach (var page in pages.Where(p => p.Parent is not null && IsInCycle(p, byType)))
        {
            errors.Add(Error("RT0020", $"[RouteParent] forms a cycle: {Describe(ParentChain(page, byType))}. A page cannot be its own ancestor.", page));
        }

        foreach (var page in pages.Where(p => p.IsIndex && p.Parent is null))
        {
            errors.Add(Error("RT0021", "[RouteIndex] needs a [RouteParent]: an index page renders at its parent's path, so it must be nested.", page));
        }

        if (errors.Count > 0)
        {
            throw new RouteCompilationException(errors);
        }

        foreach (var page in pages.Where(p => p.Parent is null || mapParents.ContainsKey(p)))
        {
            result.Add((page.Parent, Build(page, mapParents.GetValueOrDefault(page), errors)));
        }

        if (errors.Count > 0)
        {
            throw new RouteCompilationException(errors);
        }

        return result;
    }

    static List<Page> Collect(Assembly assembly, Func<Type, bool>? filter, List<RouteCompilationError> errors)
    {
        var pages = new List<Page>();

        foreach (var type in assembly.GetExportedTypes())
        {
            if (!typeof(IComponent).IsAssignableFrom(type)
                || type.IsAbstract
                || type.IsGenericTypeDefinition
                || !type.IsDefined(typeof(RouteAttribute), inherit: false)
#if NET9_0_OR_GREATER
                || type.IsDefined(typeof(ExcludeFromInteractiveRoutingAttribute), inherit: false)
#endif
                || filter?.Invoke(type) == false)
            {
                continue;
            }

            var templates = type.GetCustomAttributes<RouteAttribute>(inherit: false).Select(r => r.Template).ToList();
            var page = new Page(type, $"page: {type.FullName} ({assembly.GetName().Name})", templates, type.GetCustomAttribute<RouteParentAttribute>(inherit: false)?.Parent);
            pages.Add(page);
            ValidateAttributes(page, errors);
        }

        return pages;
    }

    static void ValidateAttributes(Page page, List<RouteCompilationError> errors)
    {
        var type = page.Type;

        foreach (var guard in type.GetCustomAttributes<RouteGuardAttribute>(inherit: false))
        {
            if (!IsUsable(guard.GuardType, typeof(IRouteGuard)))
            {
                errors.Add(Error("RT0022", $"[RouteGuard] names {guard.GuardType.FullName}, which is not a concrete class implementing IRouteGuard.", page));
            }
        }

        if (type.GetCustomAttribute<RouteLoaderCacheAttribute>(inherit: false) is { } cache && (cache.FreshSeconds <= 0 || cache.StaleSeconds < 0))
        {
            errors.Add(Error("RT0022", "[RouteLoaderCache] needs a positive FreshSeconds and a StaleSeconds of zero or more.", page));
        }

        foreach (var leaveGuard in type.GetCustomAttributes<RouteLeaveGuardAttribute>(inherit: false))
        {
            if (!IsUsable(leaveGuard.GuardType, typeof(IRouteLeaveGuard)))
            {
                errors.Add(Error("RT0022", $"[RouteLeaveGuard] names {leaveGuard.GuardType.FullName}, which is not a concrete class implementing IRouteLeaveGuard.", page));
            }
        }

        if (type.GetCustomAttribute<RouteKeepAliveAttribute>(inherit: false) is { Max: < 1 })
        {
            errors.Add(Error("RT0022", "[RouteKeepAlive] needs a Max of at least 1.", page));
        }

        foreach (var slot in type.GetCustomAttributes<RouteSlotAttribute>(inherit: false))
        {
            if (string.IsNullOrWhiteSpace(slot.Name) || !typeof(IComponent).IsAssignableFrom(slot.Component))
            {
                errors.Add(Error("RT0022", $"[RouteSlot] needs a name and a component type that implements IComponent ({slot.Component.FullName} does not).", page));
            }
        }

        if (type.GetCustomAttribute<RouteLoaderAttribute>(inherit: false) is { } loader && !IsUsable(loader.LoaderType, typeof(IRouteLoader)))
        {
            errors.Add(Error("RT0022", $"[RouteLoader] names {loader.LoaderType.FullName}, which is not a concrete class implementing IRouteLoader.", page));
        }

        if (type.GetCustomAttribute<RouteBreadcrumbAttribute>(inherit: false) is { } breadcrumb)
        {
            if (breadcrumb.Label is null ? !breadcrumb.Hidden : breadcrumb.Hidden || string.IsNullOrWhiteSpace(breadcrumb.Label))
            {
                errors.Add(Error("RT0022", "[RouteBreadcrumb] needs either a label or Hidden = true, not both and not neither.", page));
            }
        }

        if (type.GetCustomAttribute<RouteNameAttribute>(inherit: false) is { } name && string.IsNullOrWhiteSpace(name.Name))
        {
            errors.Add(Error("RT0022", "[RouteName] needs a name; leave the attribute out for an unnamed route.", page));
        }

        if (type.GetCustomAttribute<RouteTitleAttribute>(inherit: false) is { } title && string.IsNullOrWhiteSpace(title.Title))
        {
            errors.Add(Error("RT0022", "[RouteTitle] needs a title; leave the attribute out for an untitled route.", page));
        }

        if (type.GetCustomAttribute<RouteFocusAttribute>(inherit: false) is { } focus && string.IsNullOrWhiteSpace(focus.Selector))
        {
            errors.Add(Error("RT0022", "[RouteFocus] needs a CSS selector.", page));
        }
    }

    static bool IsUsable(Type type, Type contract) => contract.IsAssignableFrom(type) && type is { IsAbstract: false, IsGenericTypeDefinition: false };

    static bool IsInCycle(Page page, Dictionary<Type, Page> byType)
    {
        var seen = new HashSet<Page>();
        var current = page;

        while (current.Parent is not null && byType.TryGetValue(current.Parent, out var parent))
        {
            if (!seen.Add(current))
            {
                return true;
            }

            current = parent;
        }

        return false;
    }

    static IEnumerable<Page> ParentChain(Page page, Dictionary<Type, Page> byType)
    {
        var seen = new HashSet<Page>();
        var current = page;

        while (seen.Add(current))
        {
            yield return current;

            if (current.Parent is null || !byType.TryGetValue(current.Parent, out var parent))
            {
                yield break;
            }

            current = parent;
        }

        yield return current;
    }

    static string Describe(IEnumerable<Page> chain) => string.Join(" -> ", chain.Select(p => p.Type.Name));

    /// <summary>Builds the definition of <paramref name="page"/> and its children; <paramref name="parentTemplate"/> is the parent's full template, null for a root.</summary>
    static RouteDefinition Build(Page page, string? parentTemplate, List<RouteCompilationError> errors)
    {
        var type = page.Type;
        var templates = page.Templates.Select(t => Relative(page, t, parentTemplate, errors)).ToList();

        if (page.IsIndex && templates[0].Length > 0)
        {
            errors.Add(Error("RT0021", $"[RouteIndex] renders at the parent's path, so the page's template must be the parent's template '{parentTemplate}', not '{page.Templates[0]}'.", page));
        }

        var fullTemplate = parentTemplate is null
            ? templates[0]
            : templates[0].Length == 0 ? parentTemplate : parentTemplate.TrimEnd('/') + "/" + templates[0];
        var children = page.Children.Select(child => Build(child, fullTemplate, errors)).ToList();

        return new RouteDefinition
        {
            Template = templates[0],
            Name = type.GetCustomAttribute<RouteNameAttribute>(inherit: false)?.Name,
            Component = type,
            StaticTitle = type.GetCustomAttribute<RouteTitleAttribute>(inherit: false)?.Title,
            Children = children,
            Metadata = Metadata(type),
            Aliases = templates.Skip(1).ToArray(),
            IsIndex = page.IsIndex,
            Source = page.Source,
        };
    }

    /// <summary>
    /// A child's template relative to its parent: a full template (Razor's <c>@page</c> always starts with '/') must
    /// extend the parent's and loses that prefix; a template without a leading '/' is relative already. A top-level
    /// template or alias without a leading '/' (<c>@attribute [Route("tool")]</c>, which the built-in router accepts)
    /// is rooted as <c>/tool</c>; only the fluent map rejects that shape (RT0014).
    /// </summary>
    static string Relative(Page page, string template, string? parentTemplate, List<RouteCompilationError> errors)
    {
        if (parentTemplate is null)
        {
            return template.StartsWith('/') ? template : "/" + template;
        }

        if (!template.StartsWith('/'))
        {
            return template;
        }

        var prefix = parentTemplate.TrimEnd('/');

        if (template.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && (template.Length == prefix.Length || template[prefix.Length] == '/'))
        {
            return template[prefix.Length..].TrimStart('/');
        }

        errors.Add(Error("RT0022", $"[RouteParent] nests the page under {page.Parent!.Name}, but its template '{template}' does not extend the parent's template '{parentTemplate}'. A nested page's template is its full URL under the parent.", page));

        return template;
    }

    static RouteMetadataCollection Metadata(Type type)
    {
        var items = new List<object>();

        foreach (var guard in type.GetCustomAttributes<RouteGuardAttribute>(inherit: false))
        {
            items.Add(new RouteGuardMetadata(guard.GuardType));
        }

        foreach (var leaveGuard in type.GetCustomAttributes<RouteLeaveGuardAttribute>(inherit: false))
        {
            items.Add(new RouteLeaveGuardMetadata(leaveGuard.GuardType));
        }

        foreach (var slot in type.GetCustomAttributes<RouteSlotAttribute>(inherit: false))
        {
            items.Add(new RouteSlotMetadata(slot.Name, slot.Component));
        }

        if (type.GetCustomAttribute<RouteKeepAliveAttribute>(inherit: false) is { } keepAlive)
        {
            items.Add(new RouteKeepAliveMetadata(keepAlive.Max));
        }

        if (type.GetCustomAttribute<RouteLoaderAttribute>(inherit: false) is { } loader)
        {
            items.Add(new RouteLoaderMetadata(loader.LoaderType, loader.Policy));
        }

        if (type.GetCustomAttribute<RouteLoaderCacheAttribute>(inherit: false) is { } cache)
        {
            items.Add(new RouteLoaderCacheMetadata(TimeSpan.FromSeconds(cache.FreshSeconds), TimeSpan.FromSeconds(cache.StaleSeconds)));
        }

        if (type.GetCustomAttribute<RoutePersistLoaderDataAttribute>(inherit: false) is { } persisted)
        {
            items.Add(persisted.CreateMetadata());
        }

        if (type.GetCustomAttribute<RouteBreadcrumbAttribute>(inherit: false) is { } breadcrumb)
        {
            items.Add(new RouteBreadcrumbMetadata(breadcrumb.Label, null, breadcrumb.Hidden));
        }

        if (type.GetCustomAttribute<RouteFocusAttribute>(inherit: false) is { } focus)
        {
            items.Add(new RouteFocusMetadata(focus.Selector));
        }

        if (type.IsDefined(typeof(RouteNoAnnounceAttribute), inherit: false))
        {
            items.Add(new RouteAnnounceMetadata(false));
        }

        if (type.IsDefined(typeof(RouteCanonicalAliasAttribute), inherit: false))
        {
            items.Add(new RouteAliasMetadata(AliasBehavior.RedirectToCanonical));
        }

        return RouteMetadataCollection.Create([.. items]);
    }

    static RouteCompilationError Error(string code, string message, Page page)
        => new(code, $"{message} Page: {page.Type.FullName} [template '{page.Templates[0]}', source {page.Source}].", null, page.Templates[0], page.Source, page.Type);
}
