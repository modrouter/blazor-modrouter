using System.Collections.ObjectModel;
using System.Globalization;

namespace ModRouter.Core.Matching;

/// <summary>
/// The parameter values of a matched route level. Names are case-insensitive; values are already converted by the
/// parameter's constraint (an <c>{id:int}</c> value is an <see cref="int"/>).
/// </summary>
public sealed class RouteParameters : IEquatable<RouteParameters>
{
    /// <summary>The shared instance with no values.</summary>
    public static RouteParameters Empty { get; } = Wrap(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Copies <paramref name="values"/>; later changes to the input do not affect these parameters.</summary>
    public RouteParameters(IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        Values = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase));
    }

    RouteParameters(ReadOnlyDictionary<string, object?> values) => Values = values;

    /// <summary>The name-to-value map. Read-only and case-insensitive.</summary>
    public IReadOnlyDictionary<string, object?> Values { get; }

    /// <summary>Wraps without copying: the caller gives up ownership and must not touch <paramref name="owned"/> afterwards.</summary>
    internal static RouteParameters Wrap(Dictionary<string, object?> owned) => new(new ReadOnlyDictionary<string, object?>(owned));

    /// <summary>Value equality: <see cref="Equals(RouteParameters?)"/>, with two nulls equal.</summary>
    public static bool operator ==(RouteParameters? left, RouteParameters? right)
        => ReferenceEquals(left, right) || (left is not null && left.Equals(right));

    /// <summary>The negation of <see cref="op_Equality"/>.</summary>
    public static bool operator !=(RouteParameters? left, RouteParameters? right) => !(left == right);

    /// <summary>
    /// True when both hold the same names (ignoring case) with equal values (<see cref="object.Equals(object?)"/>);
    /// order does not matter. Null is never equal.
    /// </summary>
    public bool Equals(RouteParameters? other)
    {
        if (other is null || Values.Count != other.Values.Count)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        foreach (var pair in Values)
        {
            if (!other.Values.TryGetValue(pair.Key, out var otherValue) || !Equals(pair.Value, otherValue))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// True when <paramref name="obj"/> is a <see cref="RouteParameters"/> that <see cref="Equals(RouteParameters?)"/>
    /// accepts.
    /// </summary>
    public override bool Equals(object? obj) => obj is RouteParameters other && Equals(other);

    /// <summary>Order-independent: the sum of per-pair hashes, with names compared case-insensitively.</summary>
    public override int GetHashCode()
    {
        var hash = 0;

        foreach (var pair in Values)
        {
            hash += HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(pair.Key), pair.Value);
        }

        return hash;
    }

    /// <summary>The number of parameters.</summary>
    public int Count => Values.Count;

    /// <summary>
    /// True when a parameter with that name exists, ignoring case; an optional parameter that was omitted from the URL
    /// does not.
    /// </summary>
    public bool Contains(string name) => Values.ContainsKey(name);

    /// <summary>
    /// The converted value (an <c>{id:int}</c> value is an <see cref="int"/>, an unconstrained one a
    /// <see cref="string"/>), matched ignoring case.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No parameter has that name; the message lists the available ones.</exception>
    public object? this[string name]
        => Values.TryGetValue(name, out var value)
            ? value
            : throw new KeyNotFoundException($"Route parameter '{name}' was not found. Available parameters: {DescribeNames()}.");

    /// <summary>
    /// The value as <typeparamref name="T"/>: returned as is when it already is one, else converted between
    /// <see cref="IConvertible"/> types with the invariant culture (a stored <see cref="int"/> reads as
    /// <see cref="long"/>). A null value cannot be read as any type.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No parameter has that name.</exception>
    /// <exception cref="InvalidCastException">
    ///   The value is null or cannot be converted to <typeparamref name="T"/>.
    /// </exception>
    public T Get<T>(string name)
    {
        if (!Values.TryGetValue(name, out var raw))
        {
            throw new KeyNotFoundException($"Route parameter '{name}' was not found. Available parameters: {DescribeNames()}.");
        }

        if (TryConvert<T>(raw, out var value))
        {
            return value!;
        }

        throw new InvalidCastException($"Route parameter '{name}' has the value '{raw}' ({raw?.GetType().Name ?? "null"}), which cannot be converted to {typeof(T).Name}.");
    }

    /// <summary>
    /// The non-throwing form of <see cref="Get{T}"/>: false (and a default <paramref name="value"/>) when the name is
    /// missing, the value is null or it cannot be converted.
    /// </summary>
    public bool TryGet<T>(string name, out T? value)
    {
        value = default;

        return Values.TryGetValue(name, out var raw) && TryConvert(raw, out value);
    }

    string DescribeNames()
        => Values.Count == 0 ? "(none)" : string.Join(", ", Values.Keys.Order(StringComparer.OrdinalIgnoreCase));

    static bool TryConvert<T>(object? raw, out T? value)
    {
        value = default;

        switch (raw)
        {
            case null:
                return false;
            case T typed:
                value = typed;

                return true;
        }

        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

        if (raw is IConvertible && typeof(IConvertible).IsAssignableFrom(target))
        {
            try
            {
                value = (T)Convert.ChangeType(raw, target, CultureInfo.InvariantCulture);

                return true;
            }
            catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException)
            {
                return false;
            }
        }

        return false;
    }
}
