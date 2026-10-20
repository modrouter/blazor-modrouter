using System.Diagnostics.CodeAnalysis;

namespace ModRouter.Core.Matching;

/// <summary>Why a path did not match.</summary>
public enum RouteMatchFailure
{
    /// <summary>The path matched.</summary>
    None,
    /// <summary>No route matches the path.</summary>
    NotFound,
    /// <summary>A percent-escape in the path is invalid or does not decode to UTF-8.</summary>
    MalformedPath,
}

/// <summary>The outcome of matching a path. Matching never throws.</summary>
public sealed class RouteMatchResult
{
    RouteMatchResult(RouteMatchChain? chain, RouteMatchFailure failure)
    {
        Chain = chain;
        Failure = failure;
    }

    /// <summary>
    /// The shared result for a path no route matches (<see cref="Failure"/> is
    /// <see cref="RouteMatchFailure.NotFound"/>).
    /// </summary>
    public static RouteMatchResult NoMatch { get; } = new(null, RouteMatchFailure.NotFound);

    /// <summary>
    /// The shared result for a path with a malformed percent-escape (<see cref="Failure"/> is
    /// <see cref="RouteMatchFailure.MalformedPath"/>).
    /// </summary>
    public static RouteMatchResult Malformed { get; } = new(null, RouteMatchFailure.MalformedPath);

    /// <summary>True when a route matched; <see cref="Chain"/> is then non-null.</summary>
    [MemberNotNullWhen(true, nameof(Chain))]
    public bool IsMatch => Chain is not null;

    /// <summary>The matched levels, root first; null when nothing matched.</summary>
    public RouteMatchChain? Chain { get; }

    /// <summary><see cref="RouteMatchFailure.None"/> for a match, else why the path did not match.</summary>
    public RouteMatchFailure Failure { get; }

    internal static RouteMatchResult Success(RouteMatchChain chain) => new(chain, RouteMatchFailure.None);
}
