using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using ModRouter.Components;
using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NavigationContext = Microsoft.AspNetCore.Components.Routing.NavigationContext;

namespace ModRouter.Compatibility;

/// <summary>
/// A drop-in replacement for <c>Microsoft.AspNetCore.Components.Routing.Router</c> with the built-in's parameter names,
/// built on <see cref="ModRouter"/>. In <c>App.razor</c> the tag must be fully qualified
/// (<c>&lt;ModRouter.Compatibility.Router ...&gt;</c>, with the compatibility
/// <see cref="AuthorizeRouteView"/> or <see cref="RouteView"/> inside <c>Found</c>): Razor ignores a <c>@using</c> alias
/// for a tag name and silently binds a plain <c>&lt;Router&gt;</c> to the built-in, and importing this namespace next to
/// the framework's is RZ9985. Everything else in the file stays as it is.
/// </summary>
/// <remarks>
/// The first render shows <see cref="Navigating"/> bare; <see cref="OnNavigateAsync"/> runs for the first navigation
/// (an application adds its "lazy" assemblies there); then <see cref="AppAssembly"/> and <see cref="AdditionalAssemblies"/>
/// are read once, their pages discovered (one <see cref="ModRoutePages"/> per assembly), compiled with the startup routes of
/// <c>AddModRouter</c>, frozen, and the first navigation starts. A later change to either list is logged once as a
/// warning and ignored. Every later navigation runs <see cref="OnNavigateAsync"/> after its location changed and before
/// its page renders (the old page stays, with the navigation indicator), with a token that is canceled only while the
/// callback is still running, when the next location change (or a reload, or the router's removal) supersedes it. A
/// match renders <see cref="Found"/> with a <see cref="RouteData"/> for the depth-0 component. Blazor WebAssembly only.
/// </remarks>
public sealed class Router : ComponentBase, IDisposable
{
    static readonly IReadOnlyDictionary<string, object?> NoRouteValues = new Dictionary<string, object?>();

    CancellationTokenSource? _firstNavigation;
    Func<string, CancellationToken, Task>? _hook;
    AssemblySnapshot? _assemblies;
    bool _warnedAboutAssemblyChange;
    bool _ready;
    bool _disposed;

    /// <summary>The assembly whose <c>@page</c> components are routes.</summary>
    [Parameter]
    [EditorRequired]
    public Assembly? AppAssembly { get; set; }

    /// <summary>More assemblies with <c>@page</c> components; read once, after the first <see cref="OnNavigateAsync"/>.</summary>
    [Parameter]
    public IEnumerable<Assembly>? AdditionalAssemblies { get; set; }

    /// <summary>Rendered when no route matches (bare: the fragment brings its own layout, as in the built-in). Not with <see cref="NotFoundPage"/>.</summary>
    [Parameter]
    public RenderFragment? NotFound { get; set; }

    /// <summary>A page component rendered through <see cref="Found"/>, with an empty <see cref="RouteData"/>, when no route matches (the built-in's newer form). Not with <see cref="NotFound"/>.</summary>
    [Parameter]
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type? NotFoundPage { get; set; }

    /// <summary>Rendered with the <see cref="RouteData"/> of the depth-0 component of the matched chain.</summary>
    [Parameter]
    [EditorRequired]
    public RenderFragment<RouteData>? Found { get; set; }

    /// <summary>Rendered bare until the first navigation commits (a later navigation keeps the old page; <c>ModNavigationIndicator</c> is the analogue).</summary>
    [Parameter]
    public RenderFragment? Navigating { get; set; }

    /// <summary>
    /// Runs for the first navigation, before the pages are discovered; then for each navigation to another path or query,
    /// after its location changed and its route was prepared, before its page renders.
    /// </summary>
    [Parameter]
    public EventCallback<NavigationContext> OnNavigateAsync { get; set; }

    /// <summary>Accepted for source compatibility and ignored, as the built-in ignores it ("obsolete, configuring it has no effect").</summary>
    [Parameter]
    public bool PreferExactMatches { get; set; }

    [Inject]
    INavigationPlatformAdapter Platform { get; set; } = default!;

    [Inject]
    NavigationCoordinator Coordinator { get; set; } = default!;

    [Inject]
    IServiceProvider Services { get; set; } = default!;

    /// <summary>
    /// Validates the parameters, then runs <see cref="OnNavigateAsync"/> for the first navigation (the first render
    /// shows <see cref="Navigating"/> meanwhile), installs the hook for later navigations and reads
    /// <see cref="AppAssembly"/> and <see cref="AdditionalAssemblies"/> once.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///   <see cref="AppAssembly"/> or <see cref="Found"/> is missing, or both <see cref="NotFound"/> and
    ///   <see cref="NotFoundPage"/> are set.
    /// </exception>
    protected override async Task OnInitializedAsync()
    {
        if (AppAssembly is null)
        {
            throw new InvalidOperationException($"The {nameof(Router)} component requires a value for the parameter {nameof(AppAssembly)}.");
        }

        if (Found is null)
        {
            throw new InvalidOperationException($"The {nameof(Router)} component requires a value for the parameter {nameof(Found)}.");
        }

        if (NotFoundPage is not null && NotFound is not null)
        {
            throw new InvalidOperationException("Setting NotFound and NotFoundPage properties simultaneously is not supported. Use either NotFound or NotFoundPage.");
        }

        // The first run, before the route table exists: its token is canceled only when this router goes away. While it
        // is awaited the first render shows Navigating.
        _firstNavigation = new CancellationTokenSource();
        await InvokeOnNavigateAsync(Platform.Uri, _firstNavigation.Token);

        // Disposed while the callback ran (one that ignores its token completes normally): installing the hook now would
        // leave the coordinator calling into a dead component for whichever router mounts next.
        if (_disposed)
        {
            return;
        }

        // Every later navigation runs the callback after its location changed and before its page renders.
        _hook = InvokeOnNavigateAsync;
        Coordinator.OnNavigate = _hook;
        _assemblies = AssemblySnapshot.Take(AppAssembly, AdditionalAssemblies);
        _ready = true;
    }

    /// <summary>
    /// Warns once (a log event) when <see cref="AppAssembly"/> or <see cref="AdditionalAssemblies"/> differs from what
    /// was read at start; the routes are not rediscovered.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (_assemblies is null || _warnedAboutAssemblyChange || AppAssembly is null || _assemblies.Matches(AppAssembly, AdditionalAssemblies))
        {
            return;
        }

        _warnedAboutAssemblyChange = true;
        if (Services.GetService<ILoggerFactory>()?.CreateLogger<Router>() is { } created && Diagnostics.RouterLog.Guard(created) is var logger && logger.IsEnabled(LogLevel.Warning))
        {
            Diagnostics.RouterLog.CompatAssembliesChanged(logger, _assemblies.Describe(), AssemblySnapshot.Take(AppAssembly, AdditionalAssemblies).Describe());
        }
    }

    /// <summary>
    /// Renders <see cref="Navigating"/> bare until the first <see cref="OnNavigateAsync"/> has finished, then an
    /// <see cref="ModRouter"/> that discovers the assemblies' pages, renders <see cref="Found"/> for a match and the
    /// not-found content otherwise.
    /// </summary>
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (!_ready)
        {
            builder.AddContent(0, Navigating);

            return;
        }

        var found = Found!;
        var notFound = NotFoundPage is { } notFoundPage
            ? found(new RouteData(notFoundPage, NoRouteValues))
            : NotFound;

        builder.OpenComponent<Components.ModRouter>(1);
        builder.AddComponentParameter(2, nameof(Components.ModRouter.Routes), (RenderFragment)RenderPages);
        builder.AddComponentParameter(3, nameof(Components.ModRouter.Found), found);
        builder.AddComponentParameter(4, nameof(Components.ModRouter.NotFound), notFound);
        builder.AddComponentParameter(5, nameof(Components.ModRouter.Loading), Navigating);
        builder.CloseComponent();
    }

    /// <summary>
    /// Detaches this router's <see cref="OnNavigateAsync"/> hook from the navigation coordinator and cancels the token
    /// of a first run that is still in flight.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;

        if (_hook is not null && ReferenceEquals(Coordinator.OnNavigate, _hook))
        {
            Coordinator.OnNavigate = null;
        }

        _firstNavigation?.Cancel();
        _firstNavigation?.Dispose();
    }

    /// <summary>
    /// Invokes the application's callback with the built-in's path form (<c>NavigationManager.ToBaseRelativePath</c>:
    /// "customers/42?tab=a", no leading slash, "" at the base), on the renderer's dispatcher: an <see cref="EventCallback"/>
    /// re-renders its receiver (the application's root component) when the handler starts.
    /// </summary>
    Task InvokeOnNavigateAsync(string absoluteUri, CancellationToken cancellationToken)
    {
        if (!OnNavigateAsync.HasDelegate)
        {
            return Task.CompletedTask;
        }

        var context = NavigationContextFactory.Create(NavigationContextFactory.ToBaseRelativePath(Platform.BaseUri, absoluteUri), cancellationToken);

        return InvokeAsync(() => OnNavigateAsync.InvokeAsync(context));
    }

    void RenderPages(RenderTreeBuilder builder)
    {
        foreach (var assembly in _assemblies!.Assemblies)
        {
            builder.OpenComponent<ModRoutePages>(0);
            builder.AddComponentParameter(1, nameof(ModRoutePages.Assembly), assembly);
            builder.SetKey(assembly);
            builder.CloseComponent();
        }
    }
}
