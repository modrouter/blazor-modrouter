using ModRouter.Core.Definitions;

namespace ModRouter.Authorization;

/// <summary>
/// Attaches <see cref="AuthorizeAttributeGuard"/> as the first guard of every definition (at any depth) whose component
/// carries <c>IAuthorizeData</c> or whose metadata declares <see cref="RouteAuthorizeMetadata"/>, unless either source
/// allows anonymous access, whichever source declared the route (fluent, discovered, markup). It runs where a table is
/// compiled (<c>AddModRouter</c> and the markup compile) and is idempotent: the startup definitions pass through it
/// twice when markup routes are added to them.
/// </summary>
internal static class AuthorizationRouteContributor
{
    public static IReadOnlyList<RouteDefinition> Apply(IReadOnlyList<RouteDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var result = new RouteDefinition[definitions.Count];

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = Apply(definitions[i]);
        }

        return result;
    }

    static RouteDefinition Apply(RouteDefinition definition)
    {
        var children = definition.Children.Count == 0 ? definition.Children : Apply(definition.Children);
        var metadata = definition.Metadata;

        if (NeedsGuard(definition))
        {
            metadata = metadata.WithFirst(new RouteGuardMetadata(typeof(AuthorizeAttributeGuard)));
        }

        return ReferenceEquals(metadata, definition.Metadata) && ReferenceEquals(children, definition.Children)
            ? definition
            : definition.With(metadata, children);
    }

    static bool NeedsGuard(RouteDefinition definition)
        => definition.Component != typeof(RedirectOnly)
            && !definition.Metadata.GetAll<RouteGuardMetadata>().Any(guard => guard.GuardType == typeof(AuthorizeAttributeGuard))
            && AuthorizationRequirements.Collect(definition) is not null;
}
