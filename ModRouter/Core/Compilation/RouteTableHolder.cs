using System.Diagnostics.CodeAnalysis;
using ModRouter.Core.Definitions;
using ModRouter.Core.Generation;

namespace ModRouter.Core.Compilation;

/// <summary>
/// Holds the <see cref="CompiledRouteTable"/> every consumer reads per use. It starts with the table compiled in
/// <c>AddModRouter</c> (the startup table: fluent routes and discovered pages) and is frozen exactly once by <c>ModRouter</c>,
/// either with that same table or with the startup definitions merged with the markup <c>&lt;Routes&gt;</c> entries. Before
/// the freeze it serves the startup table, so a fluent-only app may read it before the router mounts; a failed name lookup
/// then carries a hint that the table is not final yet. A second freeze throws, except a repeat with the same
/// <see cref="FrozenSignature"/> (a remounted router declaring the same routes), which is a no-op. Only <c>AddModRouter</c>
/// creates it and only <c>ModRouter</c> freezes it; an app reads <see cref="Table"/> and <see cref="IsFrozen"/>.
/// </summary>
public sealed class RouteTableHolder : INamedRouteRegistry
{
    /// <summary>Appended to a failed name lookup while the table is not frozen.</summary>
    public const string NotFinalHint = "The route table is not final yet: markup <Routes> entries are compiled after ModRouter's first render.";

    // The holder is a singleton, so every circuit of a Blazor Server host shares it: the freeze and the failure flag are
    // guarded, and the readers take volatile reads (the table is read per use and never locked).
    readonly object _gate = new();
    CompiledRouteTable _table;
    bool _compilationFailed;
    bool _frozen;

    /// <summary>Created by <c>AddModRouter</c> only, with the table it compiled and the definitions behind it.</summary>
    internal RouteTableHolder(CompiledRouteTable startupTable, IReadOnlyList<RouteDefinition>? startupDefinitions = null)
    {
        ArgumentNullException.ThrowIfNull(startupTable);

        _table = startupTable;
        StartupDefinitions = startupDefinitions ?? [];
    }

    /// <summary>The definitions <c>AddModRouter</c> compiled; markup entries are appended to them.</summary>
    internal IReadOnlyList<RouteDefinition> StartupDefinitions { get; }

    /// <summary>True once <see cref="Freeze"/> ran: the table is final.</summary>
    public bool IsFrozen => Volatile.Read(ref _frozen);

    /// <summary>What the frozen table was compiled from, as given to <see cref="Freeze"/>; null before the freeze.</summary>
    internal string? FrozenSignature { get; private set; }

    /// <summary>The table to use right now. Read it per use; never cache it across a navigation.</summary>
    public CompiledRouteTable Table => Volatile.Read(ref _table);

    /// <summary>
    /// Makes <paramref name="table"/> the final table. <paramref name="signature"/> identifies the declarations it was
    /// compiled from: a repeat call with the same non-null signature is a no-op (the table of the first call stays), any
    /// other call after the first throws.
    /// </summary>
    /// <exception cref="InvalidOperationException">The table is already frozen from different declarations.</exception>
    internal void Freeze(CompiledRouteTable table, string? signature = null)
    {
        ArgumentNullException.ThrowIfNull(table);

        lock (_gate)
        {
            if (_frozen)
            {
                if (signature is not null && string.Equals(signature, FrozenSignature, StringComparison.Ordinal))
                {
                    return;
                }

                throw new InvalidOperationException("The route table is already frozen. It is frozen exactly once, by ModRouter before its first navigation; a second ModRouter, a remount with different <Routes> markup, or entries rendered later cannot change it.");
            }

            Volatile.Write(ref _table, table);
            FrozenSignature = signature;
            Volatile.Write(ref _frozen, true);
        }
    }

    /// <summary>
    /// Records that the markup compile failed, so the table stays the startup one and will not become final: a failed name
    /// lookup no longer carries the not-final hint. A later <see cref="Freeze"/> (a remount with corrected markup) still works.
    /// </summary>
    internal void MarkCompilationFailed() => Volatile.Write(ref _compilationFailed, true);

    /// <summary>True when the markup compile failed (the table is the startup one and stays unfrozen); the debug panel says so.</summary>
    internal bool CompilationFailed => Volatile.Read(ref _compilationFailed);

    // ---- INamedRouteRegistry: forwarded to the current table ----------------------------------------------------

    /// <summary>Looks a route up by name (ignoring case) in the current <see cref="Table"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="routeName"/> is null.</exception>
    public bool TryGetRoute(string routeName, [NotNullWhen(true)] out RouteDefinition? route)
    {
        ArgumentNullException.ThrowIfNull(routeName);

        return Table.TryGetRoute(routeName, out route);
    }

    /// <summary>
    /// Like <see cref="TryGetRoute"/> but throws when the name is unknown. Before the table is frozen the message ends
    /// with <see cref="NotFinalHint"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="routeName"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">No route has that name in the current table.</exception>
    public RouteDefinition GetRequiredRoute(string routeName)
    {
        var table = Table;

        if (table.TryGetRoute(routeName, out var route))
        {
            return route;
        }

        throw new KeyNotFoundException(WithHint(table.UnknownRouteMessage(routeName)));
    }

    /// <summary>
    /// Generates a URL from the current <see cref="Table"/>, exactly as <see cref="CompiledRouteTable.GenerateUrl"/>
    /// does.
    /// </summary>
    /// <exception cref="RouteUrlGenerationException">
    ///   The route is unknown or a value is invalid; for an unknown route before the freeze the message ends with
    ///   <see cref="NotFinalHint"/>.
    /// </exception>
    public string GenerateUrl(
        string routeName,
        IReadOnlyDictionary<string, object?>? parameters = null,
        IReadOnlyDictionary<string, object?>? query = null,
        string? fragment = null,
        IReadOnlyDictionary<string, object?>? ambientValues = null)
    {
        if (!TryGenerateUrl(routeName, parameters, query, fragment, ambientValues, out var url, out var error))
        {
            throw new RouteUrlGenerationException(error);
        }

        return url;
    }

    /// <summary>
    /// The non-throwing form of <see cref="GenerateUrl"/>. For an unknown route before the freeze,
    /// <paramref name="error"/>'s message ends with <see cref="NotFinalHint"/>.
    /// </summary>
    public bool TryGenerateUrl(
        string routeName,
        IReadOnlyDictionary<string, object?>? parameters,
        IReadOnlyDictionary<string, object?>? query,
        string? fragment,
        IReadOnlyDictionary<string, object?>? ambientValues,
        [NotNullWhen(true)] out string? url,
        [NotNullWhen(false)] out RouteUrlGenerationError? error)
    {
        if (Table.TryGenerateUrl(routeName, parameters, query, fragment, ambientValues, out url, out error))
        {
            return true;
        }

        if (error.Code == UrlGenerationErrorCode.UnknownRoute)
        {
            error = error with { Message = WithHint(error.Message) };
        }

        return false;
    }

    string WithHint(string message) => IsFrozen || _compilationFailed ? message : message + " " + NotFinalHint;
}
