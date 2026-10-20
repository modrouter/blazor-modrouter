using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace ModRouter.Authorization;

/// <summary>
/// The coordinator's window onto <c>AuthenticationStateProvider.AuthenticationStateChanged</c>, kept here so that
/// <c>Navigation/</c> never names the authorization package: an application without a provider gets no subscription
/// and pays nothing.
/// </summary>
internal static class AuthenticationChangeSubscription
{
    /// <summary>
    /// Subscribes <paramref name="onChanged"/> to the registered provider's event; null when no
    /// <see cref="AuthenticationStateProvider"/> is registered. Dispose the result to unsubscribe.
    /// </summary>
    public static IDisposable? Subscribe(IServiceProvider services, Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(onChanged);

        if (services.GetService<AuthenticationStateProvider>() is not { } provider)
        {
            return null;
        }

        return new Subscription(provider, onChanged);
    }

    sealed class Subscription : IDisposable
    {
        readonly AuthenticationStateProvider _provider;
        readonly Action _onChanged;
        readonly AuthenticationStateChangedHandler _handler;
        bool _disposed;

        public Subscription(AuthenticationStateProvider provider, Action onChanged)
        {
            _provider = provider;
            _onChanged = onChanged;
            _handler = OnChanged;
            provider.AuthenticationStateChanged += _handler;
        }

        void OnChanged(Task<AuthenticationState> task)
        {
            if (!_disposed)
            {
                _onChanged();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _provider.AuthenticationStateChanged -= _handler;
        }
    }
}
