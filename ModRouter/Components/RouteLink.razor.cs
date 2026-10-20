using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace ModRouter.Components;

/// <summary>When a <see cref="RouteLink"/> runs the cacheable loaders of its target into the cache, before the click.</summary>
public enum RoutePreload
{
    /// <summary>Never (the default).</summary>
    None,

    /// <summary>When the pointer rests on the link, it gets focus or it is touched, after <see cref="RouteLink.PreloadDelay"/>; leaving cancels it.</summary>
    Intent,

    /// <summary>Once, when the link first renders.</summary>
    Render,
}

/// <summary>
/// A plain anchor to a route with a generated href and a current-page state. Give it a <see cref="Route"/> name with
/// <see cref="Parameters"/> (or a plain <see cref="Href"/>); it renders <c>aria-current</c> and the class
/// <see cref="ActiveClass"/> when it points at the current page and follows navigation by itself. An unknown route name
/// or a missing required parameter throws while rendering, like <c>INavigationService.GetRouteUrl</c>. It carries no
/// styling.
/// </summary>
public sealed partial class RouteLink : ComponentBase, IDisposable
{
    /// <summary>The CSS class added to the link while it counts as the current page (see <see cref="Match"/>).</summary>
    public const string ActiveClass = RouteLinkState.ActiveClass;

    RouteLinkState _state;
    RoutePreloader? _preloader;

    [Inject]
    INavigationService Navigation { get; set; } = default!;

    [Inject]
    TimeProvider Time { get; set; } = default!;

    /// <summary>The name of the route to link to. Wins over <see cref="Href"/> when both are set.</summary>
    [Parameter]
    public string? Route { get; set; }

    /// <summary>The route's parameters: an anonymous object such as <c>new { id = 42 }</c> or a dictionary with string keys.</summary>
    [Parameter]
    public object? Parameters { get; set; }

    /// <summary>Query string values appended to the generated URL.</summary>
    [Parameter]
    public IReadOnlyDictionary<string, object?>? Query { get; set; }

    /// <summary>The URL fragment appended to the generated URL.</summary>
    [Parameter]
    public string? Fragment { get; set; }

    /// <summary>Fill parameters the link does not give from the current route's values.</summary>
    [Parameter]
    public bool UseAmbientValues { get; set; }

    /// <summary>A plain URL (relative to the base, rooted, or absolute) instead of a route name.</summary>
    [Parameter]
    public string? Href { get; set; }

    /// <summary>How the current page is recognized. Default: <see cref="RouteLinkMatch.Prefix"/> for a <see cref="Route"/>, <see cref="RouteLinkMatch.Exact"/> for an <see cref="Href"/>.</summary>
    [Parameter]
    public RouteLinkMatch? Match { get; set; }

    /// <summary>
    /// Renders the link inert: no <c>href</c>, announced as disabled, so it neither navigates nor takes focus. The
    /// current-page state is still computed.
    /// </summary>
    [Parameter]
    public bool Disabled { get; set; }

    /// <summary>The browsing context the link opens in (<c>_blank</c>, <c>_self</c>, ...); applied only when the link has an href.</summary>
    [Parameter]
    public string? Target { get; set; }

    /// <summary>
    /// When to warm the cache of the target's loaders (those that declare <c>CacheFor</c>): on intent (hover, focus or touch,
    /// after <see cref="PreloadDelay"/>) or once at render. No guard runs and nothing commits; see
    /// <see cref="INavigationService.PreloadAsync"/>. Default: <see cref="RoutePreload.None"/>.
    /// </summary>
    [Parameter]
    public RoutePreload Preload { get; set; }

    /// <summary>How long the pointer or focus must stay before an <see cref="RoutePreload.Intent"/> preload starts. Default 50 ms.</summary>
    [Parameter]
    public TimeSpan PreloadDelay { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>CSS classes of the anchor; <see cref="ActiveClass"/> is added while the link is the current page.</summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>Inline style of the anchor.</summary>
    [Parameter]
    public string? Style { get; set; }

    /// <summary>The link's content.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>Any other attribute (<c>title</c>, <c>data-testid</c>, ...) is passed to the anchor.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    RoutePreloader Preloader => _preloader ??= new RoutePreloader(Navigation, Time);

    /// <summary>Subscribes to <c>INavigationService.RouteStateChanged</c> so the current-page state follows each commit.</summary>
    protected override void OnInitialized() => Navigation.RouteStateChanged += OnRouteStateChanged;

    /// <summary>Builds the href and works out whether the link points at the current page.</summary>
    /// <exception cref="Core.Generation.RouteUrlGenerationException">
    ///   <see cref="Route"/> is unknown, or a required parameter is missing or invalid.
    /// </exception>
    protected override void OnParametersSet() => Evaluate();

    /// <summary>Preloads once when <see cref="Preload"/> is <see cref="RoutePreload.Render"/>.</summary>
    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender && Preload == RoutePreload.Render && !Disabled && _state.Href is { } href)
        {
            Preloader.Once(href);
        }
    }

    /// <summary>Unsubscribes from <c>INavigationService.RouteStateChanged</c> and cancels a pending preload.</summary>
    public void Dispose()
    {
        Navigation.RouteStateChanged -= OnRouteStateChanged;
        _preloader?.Dispose();
    }

    // Attached only for an intent preload: a handler on every link would cost a round trip per hover on a server circuit.
    EventCallback<MouseEventArgs> IntentMouse => Preload == RoutePreload.Intent ? EventCallback.Factory.Create<MouseEventArgs>(this, StartIntent) : default;

    EventCallback<MouseEventArgs> LeaveMouse => Preload == RoutePreload.Intent ? EventCallback.Factory.Create<MouseEventArgs>(this, CancelIntent) : default;

    EventCallback<FocusEventArgs> IntentFocus => Preload == RoutePreload.Intent ? EventCallback.Factory.Create<FocusEventArgs>(this, StartIntent) : default;

    EventCallback<FocusEventArgs> LeaveFocus => Preload == RoutePreload.Intent ? EventCallback.Factory.Create<FocusEventArgs>(this, CancelIntent) : default;

    EventCallback<TouchEventArgs> IntentTouch => Preload == RoutePreload.Intent ? EventCallback.Factory.Create<TouchEventArgs>(this, StartIntent) : default;

    void StartIntent()
    {
        if (!Disabled)
        {
            Preloader.Start(_state.Href, PreloadDelay);
        }
    }

    void CancelIntent() => _preloader?.Cancel();

    void OnRouteStateChanged(RouteState state)
    {
        Evaluate();
        _ = InvokeAsync(StateHasChanged);
    }

    void Evaluate()
        => _state = RouteLinkEvaluator.Evaluate(Navigation, Route, Parameters, Query, Fragment, UseAmbientValues, Href, Match);
}
