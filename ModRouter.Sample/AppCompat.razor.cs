using System.Reflection;
using ModRouter.Sample.Extra;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;

namespace ModRouter.Sample;

/// <summary>
/// The compat-mode root, shaped like a typical site's <c>App</c>: <c>OnNavigate</c> adds the "lazy" assembly (the
/// Extra library, never really lazy-loaded) to the list the Router reads, on every navigation. <c>?slownav=1</c> makes it
/// take 1.5 s, to see what the user gets meanwhile.
/// </summary>
public partial class AppCompat
{
    readonly List<Assembly> _lazyLoadedAssemblies = [];
    bool _slow;

    [Inject]
    IJSRuntime Js { get; set; } = default!;

    /// <summary>How many times OnNavigate ran, the last path it received and how many runs were canceled (superseded); the state panel shows them.</summary>
    public static int OnNavigateCalls { get; private set; }

    public static string? LastOnNavigatePath { get; private set; }

    public static int OnNavigateCanceled { get; private set; }

    protected override void OnInitialized()
        => _slow = Js is IJSInProcessRuntime inProcess && inProcess.Invoke<bool>("sampleSlowNav");

    async Task OnNavigate(NavigationContext context)
    {
        OnNavigateCalls++;
        LastOnNavigatePath = context.Path;

        if (!_lazyLoadedAssemblies.Contains(typeof(ExtraMarker).Assembly))
        {
            _lazyLoadedAssemblies.Add(typeof(ExtraMarker).Assembly);
        }

        if (_slow)
        {
            try
            {
                await Task.Delay(1500, context.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                OnNavigateCanceled++;
            }
        }
    }

    // A typical site navigates to the dashboard here; the sample keeps the not-found content visible so the checks can see it.
    static string RedirectToDashboard() => "Not found (compat).";

    // A typical site does Navigation.NavigateTo("/notauthorized", forceLoad: true, replace: true); the sample shows a message instead.
    static string RedirectToNotAuthorized(RouteData routeData) => $"Not authorized (compat): {routeData.PageType.Name} at {routeData.Template}.";
}
