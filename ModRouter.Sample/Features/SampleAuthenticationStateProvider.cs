using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace ModRouter.Sample.Features;

/// <summary>
/// The fake session as an <see cref="AuthenticationStateProvider"/>: a signed-out session is an anonymous identity, a user
/// is authenticated, an administrator also holds the <c>admin</c> role. Every session change raises
/// <c>AuthenticationStateChanged</c>, which <c>CascadingAuthenticationState</c> and the router's re-check listen to.
/// </summary>
public sealed class SampleAuthenticationStateProvider : AuthenticationStateProvider, IDisposable
{
    readonly SampleSession _session;

    public SampleAuthenticationStateProvider(SampleSession session)
    {
        _session = session;
        _session.Changed += OnChanged;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(Principal()));

    public void Dispose() => _session.Changed -= OnChanged;

    ClaimsPrincipal Principal()
    {
        if (!_session.IsSignedIn)
        {
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        var claims = new List<Claim> { new(ClaimTypes.Name, _session.IsAdmin ? "sample admin" : "sample user") };

        if (_session.IsAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "admin"));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "sample"));
    }

    // BL0013: the provider is announcing its own change, so it has no one to subscribe on behalf of.
#pragma warning disable BL0013
    void OnChanged() => NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
#pragma warning restore BL0013
}
