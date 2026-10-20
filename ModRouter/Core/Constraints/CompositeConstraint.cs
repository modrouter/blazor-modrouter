namespace ModRouter.Core.Constraints;

/// <summary>
/// Several constraints on one parameter (<c>{name:alpha:length(2,40)}</c>). Every part must match. The converted
/// value comes from the first part that defines a type (not a refinement); otherwise from the first part.
/// </summary>
internal sealed class CompositeConstraint : IRouteConstraint
{
    readonly IReadOnlyList<IRouteConstraint> _parts;

    public CompositeConstraint(IReadOnlyList<IRouteConstraint> parts)
    {
        ArgumentOutOfRangeException.ThrowIfZero(parts.Count);

        _parts = parts;
        TypedIndex = FirstTypedIndex(parts);
        Typed = parts[TypedIndex];
    }

    /// <summary>The part that defines the converted value: the first that is not a refinement, otherwise the first.</summary>
    public IRouteConstraint Typed { get; }

    /// <summary>The position of <see cref="Typed"/> among the parts (and so among the parsed constraint specs).</summary>
    public int TypedIndex { get; }

    public Type ValueType => Typed.ValueType;

    public bool TryMatch(string rawValue, out object? convertedValue)
    {
        convertedValue = null;

        foreach (var part in _parts)
        {
            if (!part.TryMatch(rawValue, out var value))
            {
                convertedValue = null;

                return false;
            }

            if (ReferenceEquals(part, Typed))
            {
                convertedValue = value;
            }
        }

        return true;
    }

    public bool TryFormat(object value, out string text)
    {
        if (!Typed.TryFormat(value, out text))
        {
            return false;
        }

        return TryMatch(text, out _);
    }

    static int FirstTypedIndex(IReadOnlyList<IRouteConstraint> parts)
    {
        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i] is not IRefinementConstraint)
            {
                return i;
            }
        }

        return 0;
    }
}
