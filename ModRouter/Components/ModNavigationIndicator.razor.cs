using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;

namespace ModRouter.Components;

/// <summary>
/// Renders <see cref="ChildContent"/> while a navigation is in progress, but only once it has lasted longer than
/// <see cref="Delay"/>, so fast navigations never flash it. Place it in the layout. The old page stays visible while
/// guards and loaders run; this is how the user knows something is happening.
/// </summary>
public sealed partial class ModNavigationIndicator : ComponentBase, IDisposable
{
    /// <summary>The default <see cref="Delay"/>: 150 milliseconds.</summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(150);

    readonly object _gate = new();
    ITimer? _timer;
    bool _visible;
    bool _disposed;

    /// <summary>How long a navigation must last before the indicator shows. Zero shows it immediately.</summary>
    [Parameter]
    public TimeSpan Delay { get; set; } = DefaultDelay;

    /// <summary>
    /// The content shown while a navigation that outlasts <see cref="Delay"/> is in progress, such as a progress bar.
    /// </summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Inject]
    INavigationService Navigation { get; set; } = default!;

    [Inject]
    TimeProvider TimeProvider { get; set; } = default!;

    /// <summary>
    /// Subscribes to <c>INavigationService.NavigatingChanged</c> and takes the navigation state that is current right
    /// now.
    /// </summary>
    protected override void OnInitialized()
    {
        Navigation.NavigatingChanged += OnNavigatingChanged;
        Update();
    }

    /// <summary>Unsubscribes and stops the pending delay timer; a timer that fires afterwards does nothing.</summary>
    public void Dispose()
    {
        Navigation.NavigatingChanged -= OnNavigatingChanged;

        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }

    void OnNavigatingChanged() => Update();

    void Update()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _timer?.Dispose();
            _timer = null;

            if (!Navigation.IsNavigating)
            {
                SetVisible(false);

                return;
            }

            if (Delay <= TimeSpan.Zero)
            {
                SetVisible(true);

                return;
            }

            _timer = TimeProvider.CreateTimer(_ => Show(), null, Delay, Timeout.InfiniteTimeSpan);
        }
    }

    void Show()
    {
        lock (_gate)
        {
            if (_disposed || !Navigation.IsNavigating)
            {
                return;
            }

            SetVisible(true);
        }
    }

    /// <summary>Must be called under <c>_gate</c>; the render itself is marshaled onto the renderer's context.</summary>
    void SetVisible(bool visible)
    {
        if (_visible == visible)
        {
            return;
        }

        _visible = visible;
        _ = InvokeAsync(StateHasChanged);
    }
}
