using System.Diagnostics.CodeAnalysis;
using ModRouter.Core.Matching;

namespace ModRouter.Core.Definitions;

/// <summary>
/// An immutable route declaration. It carries no matching logic: <c>RouteCompiler</c> turns definitions into a
/// <c>CompiledRouteTable</c>. Child templates are relative to their parent; root templates start with '/'.
/// </summary>
public sealed class RouteDefinition
{
    /// <summary>
    /// The route's own template: relative to the parent for a child (never starting with <c>/</c>), starting with
    /// <c>/</c> for a root, empty for an index route. May hold parameters, optional parameters and a trailing
    /// catch-all.
    /// </summary>
    public required string Template { get; init; }

    /// <summary>
    /// The unique, case-insensitive name used for URL generation and redirects; null or white space leaves the route
    /// unnamed. Compilation fails (RT0010) when two routes share a name.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>The component rendered for this route (an <c>IComponent</c>; the binder validates that at startup).</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public required Type Component { get; init; }

    /// <summary>A fixed title; used only when <see cref="Title"/> is null.</summary>
    public string? StaticTitle { get; init; }

    /// <summary>
    /// Computes the title from this level's inherited route parameters; wins over <see cref="StaticTitle"/>.
    /// </summary>
    public Func<RouteParameters, string>? Title { get; init; }

    /// <summary>Nested routes, rendered in this route's outlet. Their templates are relative to this route's.</summary>
    public IReadOnlyList<RouteDefinition> Children { get; init; } = [];

    /// <summary>
    /// Typed items attached to the route: the guard, loader, redirect, alias, breadcrumb, focus and announce items the
    /// router reads, plus any of your own.
    /// </summary>
    public RouteMetadataCollection Metadata { get; init; } = RouteMetadataCollection.Empty;

    /// <summary>Alternative templates that also match. URL generation always uses <see cref="Template"/>.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>An index route renders at its parent's path. Its template must be empty.</summary>
    public bool IsIndex { get; init; }

    /// <summary>
    /// A pathless group: a level with no URL segment of its own whose component (a layout with an outlet) wraps its
    /// children, which keep their own paths. Its template must be empty; it has no name, aliases or title and is never matched
    /// by itself.
    /// </summary>
    public bool IsGroup { get; init; }

    /// <summary>True for a level that adds nothing to its parent's path (an index route or a group): breadcrumbs and titles pass over it.</summary>
    internal bool SharesParentPath => IsIndex || IsGroup;

    /// <summary>Whether the level's component instance is kept when a navigation keeps the level.</summary>
    public RouteComponentReuse ReusePolicy { get; init; } = RouteComponentReuse.ReuseWhenComponentTypeMatches;

    /// <summary>Where the definition came from (for example <c>fluent: Routes.cs:42</c>), used in error messages.</summary>
    public string? Source { get; init; }

    /// <summary>A copy of this definition with <paramref name="item"/> appended to its metadata.</summary>
    internal RouteDefinition WithMetadata(object item) => With(Metadata.With(item), Children);

    /// <summary>A copy of this definition with <paramref name="children"/> as its children.</summary>
    internal RouteDefinition WithChildren(IReadOnlyList<RouteDefinition> children) => With(Metadata, children);

    /// <summary>A copy of this definition with the given metadata and children.</summary>
    internal RouteDefinition With(RouteMetadataCollection metadata, IReadOnlyList<RouteDefinition> children)
        => new()
        {
            Template = Template,
            Name = Name,
            Component = Component,
            StaticTitle = StaticTitle,
            Title = Title,
            Children = children,
            Metadata = metadata,
            Aliases = Aliases,
            IsIndex = IsIndex,
            IsGroup = IsGroup,
            ReusePolicy = ReusePolicy,
            Source = Source,
        };
}
