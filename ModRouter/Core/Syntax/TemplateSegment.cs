namespace ModRouter.Core.Syntax;

/// <summary>The kinds of segment a route template can contain.</summary>
public enum TemplateSegmentKind
{
    /// <summary>Fixed text such as <c>customers</c>.</summary>
    Literal,

    /// <summary>A required parameter such as <c>{id}</c>.</summary>
    Parameter,

    /// <summary>A trailing optional parameter such as <c>{id?}</c>.</summary>
    OptionalParameter,

    /// <summary>A trailing catch-all such as <c>{*path}</c>; matches zero or more segments.</summary>
    CatchAll,
}

/// <summary>One parsed constraint on a parameter, for example <c>int</c> or <c>length(2,40)</c>.</summary>
public sealed record ConstraintSpec(string Name, string? Argument)
{
    /// <summary>
    /// The case-normalized text of one constraint; an empty argument list is the same as none (<c>int()</c> is <c>int</c>).
    /// The argument itself is kept as written, whitespace included.
    /// </summary>
    public string Key => string.IsNullOrEmpty(Argument) ? Name.ToLowerInvariant() : $"{Name.ToLowerInvariant()}({Argument})";

    /// <summary>
    /// The constraint as written: <c>name</c> or <c>name(argument)</c>, with the argument kept verbatim.
    /// <c>ConstraintRegistry</c> accepts this form.
    /// </summary>
    public override string ToString() => Argument is null ? Name : $"{Name}({Argument})";
}

/// <summary>One parsed template segment. For parameters <see cref="Text"/> is the parameter name.</summary>
public sealed record TemplateSegment(TemplateSegmentKind Kind, string Text, IReadOnlyList<ConstraintSpec> Constraints)
{
    /// <summary>
    /// True for every kind except <see cref="TemplateSegmentKind.Literal"/> (required, optional and catch-all
    /// parameters).
    /// </summary>
    public bool IsParameter => Kind != TemplateSegmentKind.Literal;

    /// <summary>
    /// The constraint keys joined by ':' in declaration order; empty for an unconstrained segment. Used in messages and as
    /// the compile-time cache key of resolved constraints. The trie's edge identity is <c>ConstraintKeys.Canonical</c>.
    /// </summary>
    public string ConstraintKey => string.Join(':', Constraints.Select(c => c.Key));

    /// <summary>
    /// The segment as it appears in a template: <c>text</c>, <c>{id:int}</c>, <c>{id:int?}</c> or <c>{*path}</c>. A
    /// catch-all is written without its constraints.
    /// </summary>
    public override string ToString()
    {
        if (Kind == TemplateSegmentKind.Literal)
        {
            return Text;
        }

        var constraints = Constraints.Count == 0 ? string.Empty : ":" + string.Join(':', Constraints);

        return Kind switch
        {
            TemplateSegmentKind.CatchAll => "{*" + Text + "}",
            TemplateSegmentKind.OptionalParameter => "{" + Text + constraints + "?}",
            _ => "{" + Text + constraints + "}",
        };
    }
}

internal static class TemplateText
{
    /// <summary>Renders segments as an absolute template; no segments is <c>/</c>.</summary>
    public static string From(IEnumerable<TemplateSegment> segments)
    {
        var text = string.Concat(segments.Select(s => "/" + s));

        return text.Length == 0 ? "/" : text;
    }
}
