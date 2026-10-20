using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;

namespace ModRouter.Components;

/// <summary>
/// The breadcrumb trail of the current page as plain <c>nav</c> and <c>ol</c> markup, built from the committed route
/// chain: each level contributes its route title (or its <c>.Breadcrumb(...)</c> label), index routes and untitled or
/// hidden levels contribute nothing, every crumb but the last is a real link, and the last is the current page. It
/// follows navigation by itself and renders nothing when no route matched. It carries no styling.
/// </summary>
public sealed partial class RouteBreadcrumb : ComponentBase, IDisposable
{
    /// <summary>The marker the collapsed middle of a long trail shows (see <see cref="MaxItems"/>).</summary>
    public const string Ellipsis = RouteCrumbs.Ellipsis;

    IReadOnlyList<RouteCrumb> _crumbs = [];

    [Inject]
    INavigationService Navigation { get; set; } = default!;

    /// <summary>The glyph between crumbs.</summary>
    [Parameter]
    public string Separator { get; set; } = "›";

    /// <summary>A leading crumb that links to the application root (for example <c>"Home"</c>), unless a titled root route already starts the trail. Null adds none.</summary>
    [Parameter]
    public string? Home { get; set; }

    /// <summary>
    /// The longest trail to show, at least 3. A longer trail keeps its first crumb and its last crumbs and replaces the
    /// middle with <see cref="Ellipsis"/>. Null (the default) shows everything.
    /// </summary>
    [Parameter]
    public int? MaxItems { get; set; }

    /// <summary>The accessible name of the navigation landmark.</summary>
    [Parameter]
    public string AriaLabel { get; set; } = "Breadcrumb";

    /// <summary>CSS classes of the <c>nav</c> element.</summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>Inline style of the <c>nav</c> element.</summary>
    [Parameter]
    public string? Style { get; set; }

    /// <summary>Any other attribute (<c>data-testid</c>, ...) is passed to the <c>nav</c> element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>Subscribes to <c>INavigationService.RouteStateChanged</c> so the trail follows each commit.</summary>
    protected override void OnInitialized() => Navigation.RouteStateChanged += OnRouteStateChanged;

    /// <summary>Validates <see cref="MaxItems"/> and rebuilds the trail from the committed state.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="MaxItems"/> is set and below 3.</exception>
    protected override void OnParametersSet()
    {
        RouteCrumbs.ValidateMaxItems(MaxItems);
        Build();
    }

    /// <summary>Unsubscribes from <c>INavigationService.RouteStateChanged</c>.</summary>
    public void Dispose() => Navigation.RouteStateChanged -= OnRouteStateChanged;

    void OnRouteStateChanged(RouteState state)
    {
        Build();
        _ = InvokeAsync(StateHasChanged);
    }

    void Build() => _crumbs = RouteCrumbs.Build(Navigation, Home, MaxItems);
}
