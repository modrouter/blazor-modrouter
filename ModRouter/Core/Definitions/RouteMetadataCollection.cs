namespace ModRouter.Core.Definitions;

/// <summary>Immutable, typed route metadata (menu info, analytics names, feature flags, ...). Lookup is by type.</summary>
public sealed class RouteMetadataCollection
{
    readonly object[] _items;

    RouteMetadataCollection(object[] items)
    {
        _items = items;
    }

    /// <summary>The shared empty collection; <see cref="Create"/> with no items returns it too.</summary>
    public static RouteMetadataCollection Empty { get; } = new([]);

    /// <summary>The number of items, of every type.</summary>
    public int Count => _items.Length;

    /// <summary>
    /// Creates a collection holding a copy of <paramref name="items"/>, in the order given; later changes to the array
    /// do not affect it.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="items"/> is null.</exception>
    public static RouteMetadataCollection Create(params object[] items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return items.Length == 0 ? Empty : new RouteMetadataCollection((object[])items.Clone());
    }

    /// <summary>Returns a new collection with <paramref name="item"/> appended.</summary>
    public RouteMetadataCollection With(object item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new RouteMetadataCollection([.. _items, item]);
    }

    /// <summary>Returns a new collection with <paramref name="item"/> first (a guard that must run before the others).</summary>
    internal RouteMetadataCollection WithFirst(object item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return new RouteMetadataCollection([item, .. _items]);
    }

    /// <summary>
    /// The first item (in declaration order) assignable to <typeparamref name="T"/>, which may be an interface or a
    /// base type; default when there is none.
    /// </summary>
    public T? Get<T>()
    {
        foreach (var item in _items)
        {
            if (item is T match)
            {
                return match;
            }
        }

        return default;
    }

    /// <summary>Like <see cref="Get{T}"/> but throws when no item is assignable to <typeparamref name="T"/>.</summary>
    /// <exception cref="InvalidOperationException">The route declares no metadata of that type.</exception>
    public T GetRequired<T>()
    {
        foreach (var item in _items)
        {
            if (item is T match)
            {
                return match;
            }
        }

        throw new InvalidOperationException($"Route metadata of type '{typeof(T).Name}' is required but the route does not declare any.");
    }

    /// <summary>True when at least one item is assignable to <typeparamref name="T"/>.</summary>
    public bool Contains<T>() => _items.Any(item => item is T);

    /// <summary>
    /// Every item assignable to <typeparamref name="T"/>, in declaration order; the sequence is lazy and empty when
    /// there are none.
    /// </summary>
    public IEnumerable<T> GetAll<T>() => _items.OfType<T>();
}
