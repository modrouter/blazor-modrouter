using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModRouter.Navigation;

namespace ModRouter;

/// <summary>Registers the server-only pieces of the router for the server project of a Blazor Web App: <c>AddModRouterHttpStatus</c>.</summary>
public static class ModRouterServerExtensions
{
    /// <summary>
    /// Makes a prerendered (or static) request answer with the status of the page the router committed: 404 for an
    /// unmatched URL, 401 or 403 for a denied one, 500 for a failure. Call it next to <c>AddModRouter</c> in the server
    /// project of a Blazor Web App; a WebAssembly or interactive page is unaffected (there is no response to change).
    /// </summary>
    public static IServiceCollection AddModRouterHttpStatus(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.TryAddScoped<IRouteStatusSink, HttpContextRouteStatusSink>();

        return services;
    }
}
