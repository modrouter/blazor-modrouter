using ModRouter.Core.Loading;
using Microsoft.AspNetCore.Components;

namespace ModRouter.Components;

/// <summary>
/// Renders a loader's deferred value: <see cref="Fallback"/> while it is pending, <see cref="ChildContent"/> when it
/// arrives, <see cref="Error"/> (or a plain alert) when it failed. A canceled value renders nothing. Pass the value
/// directly, or its <see cref="Name"/> to look it up in the cascaded <see cref="ModRouteData"/>.
/// </summary>
public sealed partial class ModDeferred<T> : ComponentBase, IDisposable
{
    DeferredValue<T>? _value;

    [CascadingParameter]
    ModRouteData? Data { get; set; }

    /// <summary>The deferred value, for example <c>routeData.Deferred&lt;Order[]&gt;("orders")</c>. Wins over <see cref="Name"/>.</summary>
    [Parameter]
    public DeferredValue<T>? Value { get; set; }

    /// <summary>The name the loader registered the value under; looked up in the cascaded <see cref="ModRouteData"/>.</summary>
    [Parameter]
    public string? Name { get; set; }

    /// <summary>
    /// Rendered while the value is pending, and while no value can be resolved yet (no <see cref="Value"/>, and a
    /// <see cref="Name"/> that the cascaded route data does not have, which happens briefly when a navigation replaces
    /// the route).
    /// </summary>
    [Parameter]
    public RenderFragment? Fallback { get; set; }

    /// <summary>Rendered with the value once it arrived.</summary>
    [Parameter]
    public RenderFragment<T>? ChildContent { get; set; }

    /// <summary>Rendered with the exception when the value's task faulted.</summary>
    [Parameter]
    public RenderFragment<Exception>? Error { get; set; }

    /// <summary>
    /// Resolves the value (<see cref="Value"/>, else <see cref="Name"/> looked up in the cascaded
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

        if (ReferenceEquals(resolved, _value))
        {
            return;
        }

        Unsubscribe();
        _value = resolved;

        if (_value is not null)
        {
            _value.Changed += OnChanged;
        }
    }

    /// <summary>Unsubscribes from the value's <c>Changed</c> event.</summary>
    public void Dispose() => Unsubscribe();

    DeferredValue<T>? ResolveByName()
    {
        if (Name is null)
        {
            return null;
        }

        // No throw for a name the data does not have: during a navigation this component can receive the next route's data
        // before it is removed. It shows its fallback until a matching slot arrives.
        return (Data ?? throw new InvalidOperationException("ModDeferred was given a Name but there is no route data: place it inside a route rendered by ModRouterOutlet, or pass Value.")).FindDeferred<T>(Name);
    }

    void Unsubscribe()
    {
        if (_value is not null)
        {
            _value.Changed -= OnChanged;
        }
    }

    void OnChanged() => _ = InvokeAsync(StateHasChanged);
}
