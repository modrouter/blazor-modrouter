using ModRouter.Core.Syntax;

namespace ModRouter.Core.Constraints;

/// <summary>
/// The identity of a parameter's constraint list for the trie: lists that accept the same text and convert it the same way
/// share a key, whatever order they are written in and however often a constraint is repeated.
/// </summary>
internal static class ConstraintKeys
{
    const char DefinesSeparator = '\u001f';

    /// <summary>
    /// The sorted, de-duplicated constraint keys; when more than one remains, followed by what defines the converted value
    /// (the first part that is not a refinement; for refinement-only lists the value type of the first part when it is a
    /// built-in refinement, otherwise its key). An empty list is the empty key.
    /// </summary>
    public static string Canonical(IReadOnlyList<ConstraintSpec> specs, IRouteConstraint? constraint)
    {
        var distinct = Distinct(specs);

        return distinct.Length switch
        {
            0 => string.Empty,
            1 => distinct[0],
            _ => string.Join(':', distinct) + DefinesSeparator + Defines(specs, constraint),
        };
    }

    /// <summary>The number of distinct constraints: the sort rank of an edge (more constraints first).</summary>
    public static int Count(IReadOnlyList<ConstraintSpec> specs) => Distinct(specs).Length;

    /// <summary>The key without the part that says what defines the value, for messages.</summary>
    public static string Display(string key)
    {
        var separator = key.IndexOf(DefinesSeparator);

        return separator < 0 ? key : key[..separator];
    }

    /// <summary>The part of the key that says what defines the value (empty when the key has none), for messages.</summary>
    public static string Defining(string key)
    {
        var separator = key.IndexOf(DefinesSeparator);

        return separator < 0 ? string.Empty : key[(separator + 1)..];
    }

    static string[] Distinct(IReadOnlyList<ConstraintSpec> specs)
        => [.. specs.Select(s => s.Key).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    static string Defines(IReadOnlyList<ConstraintSpec> specs, IRouteConstraint? constraint)
    {
        if (constraint is not CompositeConstraint composite || composite.TypedIndex >= specs.Count)
        {
            return string.Empty;
        }

        // Built-in refinements of one value type convert the same text to the same value, so their type is enough; a custom
        // refinement may convert its own way, so it is named by its key like a typed part.
        return composite.Typed is IRefinementConstraint && IsBuiltIn(composite.Typed)
            ? composite.Typed.ValueType.FullName ?? composite.Typed.ValueType.Name
            : specs[composite.TypedIndex].Key;
    }

    static bool IsBuiltIn(IRouteConstraint constraint) => constraint.GetType().Assembly == typeof(ConstraintKeys).Assembly;
}
