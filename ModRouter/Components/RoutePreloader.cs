using ModRouter.Navigation;

namespace ModRouter.Components;

/// <summary>
/// The preload behavior of a route link, shared by <see cref="RouteLink"/> and the design-system link: an intent preload
/// starts after a delay and is cancelled when the pointer or focus leaves first; a render preload runs once. Nothing is
/// attached to the link unless it asks for <see cref="RoutePreload.Intent"/>.
/// </summary>
internal sealed class RoutePreloader(INavigationService navigation, TimeProvider time) : IDisposable
{
    CancellationTokenSource? _intent;

    /// <summary>Starts a preload of <paramref name="href"/> after <paramref name="delay"/>, replacing a pending one.</summary>
    public void Start(string? href, TimeSpan delay)
    {
        if (href is null)
        {
            return;
        }

        Cancel();
        _intent = new CancellationTokenSource();
        _ = PreloadAfterDelayAsync(href, delay, _intent.Token);
    }

    /// <summary>Cancels a pending intent preload.</summary>
    public void Cancel()
    {
        _intent?.Cancel();
        _intent?.Dispose();
        _intent = null;
    }

    /// <summary>Preloads <paramref name="href"/> now (a link that asked for <see cref="RoutePreload.Render"/>).</summary>
    public void Once(string href) => _ = navigation.PreloadAsync(href);

    /// <summary>Cancels a pending intent preload.</summary>
    public void Dispose() => Cancel();

    async Task PreloadAfterDelayAsync(string href, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, time, cancellationToken);
            await navigation.PreloadAsync(href, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // The pointer or focus left before the delay passed, or the link went away: nothing to warm.
        }
    }
}
