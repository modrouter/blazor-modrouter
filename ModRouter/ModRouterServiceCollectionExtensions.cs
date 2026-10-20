using System.Diagnostics.CodeAnalysis;
using ModRouter.Authorization;
using ModRouter.Components;
using ModRouter.Core.Compilation;
using ModRouter.Core.Constraints;
using ModRouter.Core.Definitions;
using ModRouter.Core.Generation;
using ModRouter.Core.Titles;
using ModRouter.Diagnostics;
using ModRouter.Discovery;
using ModRouter.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ModRouter;

/// <summary>Options for <c>AddModRouter</c>.</summary>
public sealed class ModRouterOptions
{
    /// <summary>Constraint registry used to compile routes; null uses the built-in constraints.</summary>
    public IConstraintRegistry? ConstraintRegistry { get; set; }

    /// <summary>The loader data cache of routes that declare <c>CacheFor</c>.</summary>
    public LoaderCacheOptions LoaderCache { get; } = new();

    /// <summary>When true (the default), route parameters are checked against their components' parameter types at startup.</summary>
    public bool ValidateComponentBindings { get; set; } = true;

    /// <summary>
    /// What an alias URL does by default: render in place (the default) or redirect to the canonical URL. A route can
    /// override it with <c>CanonicalAlias()</c>.
    /// </summary>
    public AliasBehavior AliasBehavior { get; set; } = AliasBehavior.Render;

    /// <summary>The browser title: format, default and not-found text.</summary>
    public TitleOptions Title { get; } = new();

    /// <summary>The screen-reader announcement of a new page.</summary>
    public AnnounceOptions Announce { get; } = new();

    /// <summary>Moving focus to the new page's heading.</summary>
    public FocusOptions Focus { get; } = new();

    /// <summary>Scroll to the top, restore on back/forward, fragments.</summary>
    public ScrollOptions Scroll { get; } = new();

    /// <summary>Animate the change between pages with the browser's View Transitions API. Off by default.</summary>
    public ViewTransitionOptions ViewTransitions { get; } = new();

    /// <summary>The assemblies whose <c>@page</c> components are discovered at startup, after the fluent routes (see <c>RouteMap.AddPages</c>).</summary>
    public PageAssemblyCollection Pages { get; } = new();

    /// <summary>The in-memory navigation history (<see cref="DiagnosticsOptions.HistorySize"/>, 0 = off), read by the debug panel's timeline.</summary>
    public DiagnosticsOptions Diagnostics { get; } = new();
}

/// <summary>Options of the loader data cache (<c>CacheFor</c>).</summary>
public sealed class LoaderCacheOptions
{
    /// <summary>The most entries one router scope keeps (one per cached level, parameter set and query); the oldest are evicted first. Default 50.</summary>
    public int MaxEntries { get; set; } = 50;
}

/// <summary>Registers the router with dependency injection: <c>AddModRouter</c>.</summary>
public static class ModRouterServiceCollectionExtensions
{
    /// <summary>
    /// Registers the router. The startup route table (the <paramref name="routes"/> delegate, which an app whose routes
    /// are all declared in <c>&lt;ModRouter&gt;&lt;Routes&gt;</c> markup leaves out) is compiled, and by default its component
    /// bindings validated, right here, so configuration mistakes surface at startup in <c>Program.cs</c>, not on first
    /// navigation. The table is served through a <see cref="RouteTableHolder"/> that <c>ModRouter</c> freezes before its first
    /// navigation.
    /// </summary>
    /// <exception cref="RouteCompilationException">A route definition is invalid.</exception>
    /// <exception cref="RouteBindingValidationException">A route parameter cannot bind to its component.</exception>
    public static IServiceCollection AddModRouter(this IServiceCollection services, Action<RouteMap>? routes = null, Action<ModRouterOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new ModRouterOptions();
        configure?.Invoke(options);

        RouteTitleFormatter.Validate(options.Title.Format);

        if (options.ViewTransitions.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("ModRouterOptions.ViewTransitions.Timeout must be positive.", nameof(configure));
        }

        if (options.Focus.Timeout < TimeSpan.Zero)
        {
            throw new ArgumentException("ModRouterOptions.Focus.Timeout cannot be negative.", nameof(configure));
        }

        options.Diagnostics.Validate();

        if (options.LoaderCache.MaxEntries < 1)
        {
            throw new ArgumentException("ModRouterOptions.LoaderCache.MaxEntries must be at least 1.", nameof(configure));
        }

        var map = RouteMap.Create();
        routes?.Invoke(map);
        AddPages(map, options.Pages);

        // Routes that need authorization ([Authorize] on the component, .Authorize() on the route) get the guard here, so the
        // compiled table enforces it whichever source declared them.
        var definitions = AuthorizationRouteContributor.Apply(map.Build());
        var table = RouteCompiler.Compile(definitions, options.ConstraintRegistry);
        var holder = new RouteTableHolder(table, definitions);
        var binder = new RouteComponentBinder();

        if (options.ValidateComponentBindings)
        {
            var errors = binder.Validate(table);

            if (errors.Count > 0)
            {
                throw new RouteBindingValidationException(errors);
            }
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddSingleton(holder);
        services.AddSingleton<INamedRouteRegistry>(holder);
        services.AddSingleton(binder);
        services.AddScoped<INavigationPlatformAdapter, BlazorNavigationAdapter>();
        services.AddScoped<RouteStateStore>();
        services.AddScoped<RouteComponentRegistry>();
        services.AddScoped<LoaderCache>();
        services.AddScoped<RouterDiagnostics>();
        services.AddScoped<NavigationCoordinator>();
        services.AddScoped<INavigationService, NavigationService>();

        return services;
    }

    // The trimming requirement of AddPages (root a trimmable assembly) is the app's to meet; see RouteMapPagesExtensions.
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = RouteMapPagesExtensions.TrimmingMessage)]
    static void AddPages(RouteMap map, PageAssemblyCollection pages)
    {
        foreach (var (assembly, filter) in pages)
        {
            map.AddPages(assembly, filter);
        }
    }
}
