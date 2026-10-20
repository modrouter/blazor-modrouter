using System.Collections.Concurrent;
using ModRouter.Core.Definitions;
using Microsoft.AspNetCore.Authorization;

namespace ModRouter.Authorization;

/// <summary>
/// What a route level requires, read the way <c>AuthorizeRouteView</c> reads a page: the component's attributes through
/// <c>GetCustomAttributes(inherit: true)</c>, any <c>IAllowAnonymous</c> among them winning over every
/// <c>IAuthorizeData</c>; the route's own metadata (<see cref="RouteAuthorizeMetadata"/>,
/// <see cref="RouteAllowAnonymousMetadata"/>) is merged in with the same rule. Shared by the contributor that attaches
/// the guard at compile time and by the guard that evaluates it per navigation.
/// </summary>
internal static class AuthorizationRequirements
{
    static readonly ConcurrentDictionary<Type, (bool AllowsAnonymous, IAuthorizeData[] Data)> Attributes = new();

    /// <summary>
    /// The <c>IAuthorizeData</c> the level must satisfy, in attribute order then metadata order; null when the level needs
    /// no authorization (nothing declared, or anonymous access allowed from either source).
    /// </summary>
    public static IReadOnlyList<IAuthorizeData>? Collect(RouteDefinition route)
    {
        ArgumentNullException.ThrowIfNull(route);

        var (allowsAnonymous, attributes) = Attributes.GetOrAdd(route.Component, static component =>
        {
            var all = component.GetCustomAttributes(inherit: true);

            return (all.OfType<IAllowAnonymous>().Any(), all.OfType<IAuthorizeData>().ToArray());
        });

        if (allowsAnonymous || route.Metadata.Contains<RouteAllowAnonymousMetadata>())
        {
            return null;
        }

        var data = new List<IAuthorizeData>(attributes);

        foreach (var metadata in route.Metadata.GetAll<RouteAuthorizeMetadata>())
        {
            data.Add(new MetadataAuthorizeData(metadata));
        }

        return data.Count == 0 ? null : data;
    }

    /// <summary><see cref="RouteAuthorizeMetadata"/> in the shape <c>AuthorizationPolicy.CombineAsync</c> reads.</summary>
    sealed class MetadataAuthorizeData(RouteAuthorizeMetadata metadata) : IAuthorizeData
    {
        public string? Policy { get; set; } = metadata.Policy;

        public string? Roles { get; set; } = metadata.Roles;

        public string? AuthenticationSchemes { get; set; }
    }
}
