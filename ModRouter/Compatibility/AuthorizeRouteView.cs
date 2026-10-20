using ModRouter.Components;
using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace ModRouter.Compatibility;

/// <summary>
/// A drop-in replacement for <c>Microsoft.AspNetCore.Components.Authorization.AuthorizeRouteView</c>. It has no
/// authorization logic of its own: <c>[Authorize]</c> and <c>[AllowAnonymous]</c> are enforced before the navigation
/// commits by the router's authorization guard. This view maps the result, a <see cref="RouteError"/> of kind
/// <see cref="RouteErrorKind.NotAuthorized"/> at any chain level, onto <see cref="NotAuthorized"/> with the
/// <see cref="AuthenticationState"/> from the cascaded <c>Task&lt;AuthenticationState&gt;</c> (<c>CascadingAuthenticationState</c>)
/// or, without one, from the registered <see cref="AuthenticationStateProvider"/>, inside the layout as the built-in
/// does. <see cref="Authorizing"/> is accepted and never shown: authorization completes before the commit.
/// </summary>
public sealed class AuthorizeRouteView : RouteView
{
    static readonly RenderFragment<AuthenticationState> DefaultNotAuthorized = _ => builder => builder.AddContent(0, "Not authorized");

    Task<AuthenticationState>? _resolving;
    AuthenticationState? _resolved;

    /// <summary>Rendered, inside the layout, instead of the page when the user may not see a level of the matched chain.</summary>
    [Parameter]
    public RenderFragment<AuthenticationState>? NotAuthorized { get; set; }

    /// <summary>Accepted for source compatibility; never shown, because authorization is decided before the navigation commits.</summary>
    [Parameter]
    public RenderFragment? Authorizing { get; set; }

    /// <summary>Accepted for source compatibility and ignored: the guard always passes a null resource (the built-in passes this value when it is set).</summary>
    [Parameter]
    public object? Resource { get; set; }

    [CascadingParameter]
    Task<AuthenticationState>? CascadedAuthenticationState { get; set; }

    [Inject]
    IServiceProvider Services { get; set; } = default!;

    RouteError? NotAuthorizedError => OutletContext.State.Error is { Kind: RouteErrorKind.NotAuthorized } error ? error : null;

    /// <summary>
    /// When the committed state carries a not-authorized error, resolves the authentication state to pass to
    /// <see cref="NotAuthorized"/>: the cascaded <c>Task&lt;AuthenticationState&gt;</c>, else the registered
    /// <see cref="AuthenticationStateProvider"/>'s. Does nothing otherwise.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///   The state is not authorized and neither a cascading authentication state nor an
    ///   <see cref="AuthenticationStateProvider"/> is available.
    /// </exception>
    protected override async Task OnParametersSetAsync()
    {
        if (NotAuthorizedError is null)
        {
            return;
        }

        var task = CascadedAuthenticationState
            ?? Services.GetService<AuthenticationStateProvider>()?.GetAuthenticationStateAsync()
            ?? throw new InvalidOperationException($"{nameof(AuthorizeRouteView)} needs a cascading Task<AuthenticationState> (wrap the Router in <CascadingAuthenticationState>) or a registered AuthenticationStateProvider to render {nameof(NotAuthorized)}.");

        if (ReferenceEquals(task, _resolving))
        {
            return;
        }

        // The provider's task is normally already complete (the guard awaited it): awaiting it costs nothing and renders
        // nothing extra. A new, unfinished one renders nothing until the state is known, never the previous state.
        _resolving = task;
        _resolved = null;
        _resolved = await task;
    }

    /// <summary>
    /// For a not-authorized state, renders <see cref="NotAuthorized"/> (or the text "Not authorized") with the resolved
    /// authentication state, and nothing until that state is known; otherwise renders the page as
    /// <see cref="RouteView"/> does.
    /// </summary>
    protected override void RenderBody(RenderTreeBuilder builder)
    {
        if (NotAuthorizedError is null)
        {
            base.RenderBody(builder);

            return;
        }

        // Nothing until the state is known (the provider's task is normally already complete: the guard awaited it).
        if (_resolved is { } state)
        {
            // Inside the error host like every other error content, so the focus fallback finds an alert in it.
            RouteErrorContent.OpenHost(builder);
            builder.AddContent(RouteErrorContent.FirstContentSequence, NotAuthorized ?? DefaultNotAuthorized, state);
            RouteErrorContent.CloseHost(builder);
        }
    }
}
