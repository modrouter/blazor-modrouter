namespace ModRouter.Components;

/// <summary>
/// Implemented by a page that a route keeps alive (<c>RouteMap.KeepAlive</c>) to learn when it is hidden and shown again:
/// stop a timer, a subscription or a poll while the user is elsewhere, and resume it on return. The page stays rendered and keeps
/// its state, but its route is no longer the current one.
/// </summary>
public interface IKeepAliveRoute
{
    /// <summary>The route was left: the page is now hidden and inert. Not called when the instance is dropped (it is disposed instead).</summary>
    ValueTask OnDeactivatedAsync();

    /// <summary>The route came back: the page is shown again. Not called for the first time it is shown.</summary>
    ValueTask OnActivatedAsync();
}
