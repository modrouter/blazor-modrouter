using System.Diagnostics.CodeAnalysis;
using ModRouter.Core.Definitions;
using ModRouter.Core.Matching;

namespace ModRouter.Core.Compilation;

/// <summary>
/// The immutable result of compiling route definitions. Matching, URL generation and explanations live in the other
/// partial files of this type.
/// </summary>
public sealed partial class CompiledRouteTable
{
    readonly IReadOnlyDictionary<string, CompiledRoute> _named;
    readonly TrieNode _root;

    internal CompiledRouteTable(IReadOnlyList<CompiledRoute> routes, IReadOnlyDictionary<string, CompiledRoute> named, TrieNode root)
    {
        Routes = ReadOnlyLists.Seal(routes);
        _named = named;
        _root = root;
    }

    /// <summary>Every flattened entry (canonical and alias), in declaration order.</summary>
    public IReadOnlyList<CompiledRoute> Routes { get; }

    /// <summary>
    /// Looks a route up by name, ignoring case. Only a canonical entry registers a name, so an alias URL resolves to
    /// the same definition as its canonical one. Returns false (and a null <paramref name="route"/>) when no route has
    /// that name.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="routeName"/> is null.</exception>
    public bool TryGetRoute(string routeName, [NotNullWhen(true)] out RouteDefinition? route)
    {
        ArgumentNullException.ThrowIfNull(routeName);

        if (_named.TryGetValue(routeName, out var entry))
        {
            route = entry.Definition;

            return true;
        }

        route = null;

        return false;
    }

    /// <summary>Like <see cref="TryGetRoute"/> but throws instead of returning false.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="routeName"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">No route has that name; the message lists the registered names.</exception>
    public RouteDefinition GetRequiredRoute(string routeName)
    {
        if (TryGetRoute(routeName, out var route))
        {
            return route;
        }

        throw new KeyNotFoundException(UnknownRouteMessage(routeName));
    }

    /// <summary>The message of a failed name lookup, listing the registered names.</summary>
    internal string UnknownRouteMessage(string routeName)
    {
        var names = _named.Count == 0 ? "(none)" : string.Join(", ", _named.Keys.Order(StringComparer.OrdinalIgnoreCase));

        return $"No route named '{routeName}' exists. Registered route names: {names}.";
    }

    internal bool TryGetNamed(string routeName, [NotNullWhen(true)] out CompiledRoute? entry)
        => _named.TryGetValue(routeName, out entry);

    /// <summary>The entry that carries <paramref name="definition"/>'s canonical template (no alias at any level).</summary>
    internal CompiledRoute FindCanonical(RouteDefinition definition)
        => Routes.First(entry => !entry.IsAlias && ReferenceEquals(entry.Definition, definition));
}
