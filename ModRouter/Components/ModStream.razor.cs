using ModRouter.Core.Loading;
using Microsoft.AspNetCore.Components;

namespace ModRouter.Components;

/// <summary>
/// Renders a loader's stream as items arrive: <see cref="Fallback"/> until the first item, <see cref="Empty"/> if it
/// completes without any, each item through <see cref="ItemTemplate"/>, <see cref="InProgress"/> after the items while
/// the stream is still open, and <see cref="Error"/> (or a plain alert) after the items if it failed. A fast stream
/// is rendered at most once per batch of items. Pass the stream directly, or its <see cref="Name"/>.
/// </summary>
public sealed partial class ModStream<T> : ComponentBase, IDisposable
{
    RouteStream<T>? _stream;
    int _renderQueued;

    [CascadingParameter]
    ModRouteData? Data { get; set; }

    /// <summary>The stream, for example <c>routeData.Stream&lt;Order&gt;("orders")</c>. Wins over <see cref="Name"/>.</summary>
    [Parameter]
    public RouteStream<T>? Value { get; set; }

    /// <summary>The name the loader registered the stream under; looked up in the cascaded <see cref="ModRouteData"/>.</summary>
    [Parameter]
    public string? Name { get; set; }

    /// <summary>Rendered until the first item arrives.</summary>
    [Parameter]
    public RenderFragment? Fallback { get; set; }

    /// <summary>Rendered when the stream completed without any item.</summary>
    [Parameter]
    public RenderFragment? Empty { get; set; }

    /// <summary>Rendered once per item, in arrival order.</summary>
    [Parameter]
    public RenderFragment<T>? ItemTemplate { get; set; }

    /// <summary>Rendered after the items while the stream is still open.</summary>
    [Parameter]
    public RenderFragment? InProgress { get; set; }

    /// <summary>Rendered with the exception that ended the stream.</summary>
    [Parameter]
    public RenderFragment<Exception>? Error { get; set; }

    /// <summary>
    /// Resolves the stream (<see cref="Value"/>, else <see cref="Name"/> looked up in the cascaded
    /// <see cref="ModRouteData"/>) and, when it is a different instance than before, moves the <c>Changed</c>
    /// subscription to it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///   <see cref="Name"/> is set, <see cref="Value"/> is not, and there is no cascaded route data (the component is
    ///   not inside a route rendered by <see cref="ModRouterOutlet"/>).
    /// </exception>
    protected override void OnParametersSet()
    {
        var resolved = Value ?? ResolveByName();

        if (ReferenceEquals(resolved, _stream))
        {
            return;
        }

        Unsubscribe();
        _stream = resolved;

        if (_stream is not null)
        {
            _stream.Changed += OnChanged;
        }
    }

    /// <summary>Unsubscribes from the stream's <c>Changed</c> event.</summary>
    public void Dispose() => Unsubscribe();

    RouteStream<T>? ResolveByName()
    {
        if (Name is null)
        {
            return null;
        }

        // No throw for a name the data does not have: during a navigation this component can receive the next route's data
        // before it is removed. It shows its fallback until a matching slot arrives.
        return (Data ?? throw new InvalidOperationException("ModStream was given a Name but there is no route data: place it inside a route rendered by ModRouterOutlet, or pass Value.")).FindStream<T>(Name);
    }

    void Unsubscribe()
    {
        if (_stream is not null)
        {
            _stream.Changed -= OnChanged;
        }
    }

    /// <summary>Items can arrive far faster than a screen refreshes: one queued render covers every item that arrives before it runs.</summary>
    void OnChanged()
    {
        if (Interlocked.Exchange(ref _renderQueued, 1) == 1)
        {
            return;
        }

        // Yielding first lets every item the producer already has ready land before the one render that shows them all.
        _ = InvokeAsync(async () =>
        {
            await Task.Yield();
            Volatile.Write(ref _renderQueued, 0);
            StateHasChanged();
        });
    }
}
