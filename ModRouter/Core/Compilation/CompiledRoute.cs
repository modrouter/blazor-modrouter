using ModRouter.Core.Constraints;
using ModRouter.Core.Definitions;
using ModRouter.Core.Syntax;

namespace ModRouter.Core.Compilation;

/// <summary>
/// One flattened route entry: a definition combined with one alias choice for each ancestor and itself. The first
/// entry of a definition is canonical; the rest are aliases. Used for matching, URL generation and explanations.
/// </summary>
public sealed class CompiledRoute
{
    internal CompiledRoute(
        int id,
        RouteDefinition definition,
        IReadOnlyList<RouteDefinition> chain,
        IReadOnlyList<TemplateSegment> segments,
        IReadOnlyList<IRouteConstraint?> constraints,
        IReadOnlyList<int> levelEnds,
        string[] levelTemplates,
        string canonicalTemplate,
        bool isAlias,
        bool isMatchable)
    {
        Id = id;
        Definition = definition;
        Chain = ReadOnlyLists.Seal(chain);
        Segments = ReadOnlyLists.Seal(segments);
        Constraints = ReadOnlyLists.Seal(constraints);
        LevelEnds = ReadOnlyLists.Seal(levelEnds);
        LevelTemplates = levelTemplates;
        Template = TemplateText.From(segments);
        CanonicalTemplate = canonicalTemplate;
        IsAlias = isAlias;
        IsMatchable = isMatchable;
    }

    /// <summary>Position in <see cref="CompiledRouteTable.Routes"/>; follows declaration order.</summary>
    public int Id { get; }

    /// <summary>
    /// The definition this entry flattens: the last element of <see cref="Chain"/>. Every alias entry of a definition
    /// returns the same instance.
    /// </summary>
    public RouteDefinition Definition { get; }

    /// <summary>Ancestors first, this route's definition last.</summary>
    public IReadOnlyList<RouteDefinition> Chain { get; }

    /// <summary>The full flattened segments of this entry.</summary>
    public IReadOnlyList<TemplateSegment> Segments { get; }

    /// <summary>The compiled constraint for each segment, or null where a segment has none.</summary>
    public IReadOnlyList<IRouteConstraint?> Constraints { get; }

    /// <summary>The full template of this entry (the alias form for an alias entry).</summary>
    public string Template { get; }

    /// <summary>
    /// The full template built from every level's own (first) template, whichever alias this entry matches through. For
    /// a canonical entry it equals <see cref="Template"/>; URL generation always starts from it.
    /// </summary>
    public string CanonicalTemplate { get; }

    /// <summary>
    /// True when this entry uses an alias template at its own level or at any ancestor level; false for the canonical
    /// entry.
    /// </summary>
    public bool IsAlias { get; }

    /// <summary>False for a parent that has an index child: the index entry answers for that path instead.</summary>
    public bool IsMatchable { get; }

    /// <summary>For each chain level, how many segments the URL has consumed once that level is complete.</summary>
    internal IReadOnlyList<int> LevelEnds { get; }

    /// <summary>For each chain level, the template of the URL prefix owned by that level and its ancestors.</summary>
    internal string[] LevelTemplates { get; }
}
