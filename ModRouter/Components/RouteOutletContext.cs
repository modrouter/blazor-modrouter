using ModRouter.Core.Matching;
using ModRouter.Navigation;
using Microsoft.AspNetCore.Components;

namespace ModRouter.Components;

/// <summary>The router-level content for chain-level errors; an outlet's own fragments take precedence.</summary>
internal sealed record RouteErrorFragments(RenderFragment<RouteError>? NotAuthorized, RenderFragment<RouteError>? Error);

/// <summary>
/// The cascading value that tells a <see cref="ModRouterOutlet"/> which level of the committed chain to render.
/// A new instance is created for every committed state; <see cref="Next"/> is stable per instance.
/// </summary>
public sealed class RouteOutletContext
{
    object? _claimedBy;
    RouteOutletContext? _next;
    ModRouteData? _data;

    internal RouteOutletContext(RouteState state, int depth, RouteErrorFragments? fragments = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        State = state;
        Depth = depth;
        Fragments = fragments;
    }

    /// <summary>The committed state this context was created for. The same instance for every depth of one state.</summary>
    public RouteState State { get; }

    /// <summary>The zero-based level of the chain the outlet holding this context renders.</summary>
    public int Depth { get; }

    internal RouteErrorFragments? Fragments { get; }

    /// <summary>
    /// The resolved chain; throws for a state without one. A chain-less context only reaches an outlet that is about to
    /// be removed (its parent is being replaced by the not-found content) or one placed in the not-found content itself.
    /// </summary>
    public RouteMatchChain Chain => State.Chain ?? throw new InvalidOperationException("The current route state has no matched chain.");

    /// <summary>The level this outlet renders, or null when the chain has no level at this depth.</summary>
    public RouteContext? Current => State.Chain is { } chain && Depth < chain.Matches.Count ? chain.Matches[Depth] : null;

    /// <summary>True when this outlet's level is the one the state's chain-level error belongs to: it shows the error instead of the page.</summary>
    internal bool IsFailedLevel => State.Error is { IsChainLevel: true } error && error.Level == Depth;

    /// <summary>The loaded data this outlet cascades: this level's, else the nearest ancestor's, else <see cref="ModRouteData.Empty"/>.</summary>
    internal ModRouteData Data => _data ??= DataAt(Depth);

    /// <summary>
    /// The context for the outlet one level deeper. It is created on the first call and returned again afterwards, so
    /// it is stable per state.
    /// </summary>
    public RouteOutletContext Next() => _next ??= new RouteOutletContext(State, Depth + 1, Fragments);

    /// <summary>
    /// The first outlet to claim a depth keeps it until it releases the claim; a different outlet claiming the same
    /// depth while the first still holds it is a mistake.
    /// </summary>
    internal bool TryClaim(object owner)
    {
        if (_claimedBy is null || ReferenceEquals(_claimedBy, owner))
        {
            _claimedBy = owner;
            EverClaimed = true;

            return true;
        }

        return false;
    }

    /// <summary>True once an outlet has claimed this depth while the state was committed (the outlet warning reads it).</summary>
    internal bool EverClaimed { get; private set; }

    /// <summary>The next depth's context if an outlet at this depth ever asked for it, without creating it.</summary>
    internal RouteOutletContext? NextIfCreated => _next;

    /// <summary>Releases <paramref name="owner"/>'s claim (a disposed outlet must not block its replacement).</summary>
    internal void Release(object owner)
    {
        if (ReferenceEquals(_claimedBy, owner))
        {
            _claimedBy = null;
        }
    }

    /// <summary>The loaded data of chain level <paramref name="level"/>, else the nearest ancestor's, else <see cref="ModRouteData.Empty"/>.</summary>
    internal ModRouteData DataAt(int level)
    {
        var levels = State.LevelData;

        for (var depth = Math.Min(level, levels.Count - 1); depth >= 0; depth--)
        {
            if (levels[depth] is { } data)
            {
                return data.Accessor;
            }
        }

        return ModRouteData.Empty;
    }
}
