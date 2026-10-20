using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace ModRouter.Navigation;

/// <summary><see cref="INavigationPlatformAdapter"/> over Blazor's <see cref="NavigationManager"/>.</summary>
internal sealed class BlazorNavigationAdapter : INavigationPlatformAdapter, IDisposable
{
    readonly NavigationManager _navigation;

    public BlazorNavigationAdapter(NavigationManager navigation)
    {
        _navigation = navigation;
        _navigation.LocationChanged += OnLocationChanged;
    }

    public event Action<LocationChangedNotification>? LocationChanged;

    public string Uri => _navigation.Uri;

    public string BaseUri => _navigation.BaseUri;

    public string? HistoryEntryState => _navigation.HistoryEntryState;

    public void NavigateTo(string uri, bool replaceHistory = false, bool forceLoad = false, string? historyState = null)
        => _navigation.NavigateTo(uri, new NavigationOptions
        {
            ForceLoad = forceLoad,
            ReplaceHistoryEntry = replaceHistory,
            HistoryEntryState = historyState,
        });

    public IDisposable RegisterLocationChanging(Func<LocationChangingRequest, ValueTask<LocationChangingDecision>> handler)
    {
        try
        {
            return _navigation.RegisterLocationChangingHandler(async context =>
            {
                var decision = await handler(new LocationChangingRequest(
                    context.TargetLocation,
                    context.HistoryEntryState,
                    context.IsNavigationIntercepted,
                    context.CancellationToken));

                if (decision == LocationChangingDecision.Prevent)
                {
                    context.PreventNavigation();
                }
            });
        }
        catch (NotSupportedException)
        {
            // Static server-side rendering (its NavigationManager does not override SetNavigationLockState) cannot hold a
            // navigation lock. No navigation can start there, so there is nothing to intercept; the router still matches
            // and renders the request URL.
            return NoRegistration.Instance;
        }
    }

    sealed class NoRegistration : IDisposable
    {
        public static readonly NoRegistration Instance = new();

        public void Dispose()
        {
        }
    }

    public void Dispose() => _navigation.LocationChanged -= OnLocationChanged;

    void OnLocationChanged(object? sender, LocationChangedEventArgs args)
        => LocationChanged?.Invoke(new LocationChangedNotification(args.Location, args.IsNavigationIntercepted, args.HistoryEntryState));
}
