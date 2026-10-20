using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace ModRouter.Components;

/// <summary>
/// Renders a <see cref="RouteError"/>: the outlet's own fragment first, then the router-level one, then a plain
/// <c>role="alert"</c> message. Not-authorized errors use the not-authorized fragments; every other kind uses the error ones.
/// All of it sits inside the error host (see <see cref="OpenHost"/>).
/// </summary>
internal static class RouteErrorContent
{
    /// <summary>The first sequence number free for content inside the host opened by <see cref="OpenHost"/>.</summary>
    public const int FirstContentSequence = 3;

    /// <summary>
    /// Opens the error host, <c>&lt;div data-mod-route-error-host style="display:contents"&gt;</c>, around error content that is
    /// rendered between this call and <see cref="CloseHost"/> (content sequence numbers start at
    /// <see cref="FirstContentSequence"/>). The browser module's focus fallback looks for an alert only inside a host, so an
    /// alert that belongs to a layout or a page is never chosen. <c>display: contents</c> keeps the wrapper out of the layout.
    /// </summary>
    public static void OpenHost(RenderTreeBuilder builder)
    {
        // A region keeps these sequence numbers apart from whatever else the caller renders in the same fragment.
        builder.OpenRegion(0);
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "data-mod-route-error-host", string.Empty);
        builder.AddAttribute(2, "style", "display:contents");
    }

    /// <summary>Closes the host opened by <see cref="OpenHost"/>.</summary>
    public static void CloseHost(RenderTreeBuilder builder)
    {
        builder.CloseElement();
        builder.CloseRegion();
    }

    public static void Render(
        RenderTreeBuilder builder,
        RouteError error,
        RenderFragment<RouteError>? ownNotAuthorized,
        RenderFragment<RouteError>? ownError,
        RouteErrorFragments? router)
    {
        var fragment = error.Kind == RouteErrorKind.NotAuthorized
            ? ownNotAuthorized ?? router?.NotAuthorized
            : ownError ?? router?.Error;

        OpenHost(builder);

        if (fragment is not null)
        {
            builder.AddContent(FirstContentSequence, fragment, error);
        }
        else
        {
            builder.OpenElement(FirstContentSequence, "div");
            builder.AddAttribute(FirstContentSequence + 1, "role", "alert");
            builder.AddAttribute(FirstContentSequence + 2, "data-mod-route-error", error.Kind.ToString());
            builder.AddContent(FirstContentSequence + 3, error.Message);
            builder.CloseElement();
        }

        CloseHost(builder);
    }
}
