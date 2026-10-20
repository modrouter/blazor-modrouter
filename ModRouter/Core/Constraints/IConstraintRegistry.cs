using System.Diagnostics.CodeAnalysis;

namespace ModRouter.Core.Constraints;

/// <summary>Maps constraint names (<c>int</c>, <c>length</c>, ...) to factories. Resolved once, at compile time.</summary>
public interface IConstraintRegistry
{
    /// <summary>Registers or replaces a constraint. The factory receives the text inside the parentheses, or an empty string.</summary>
    void Register(string name, Func<string, IRouteConstraint> factory);

    /// <summary>Creates a constraint from a specification such as <c>int</c> or <c>length(2,40)</c>.</summary>
    bool TryCreate(string specification, [NotNullWhen(true)] out IRouteConstraint? constraint);

    /// <summary>Like <see cref="TryCreate(string, out IRouteConstraint?)"/> but explains why creation failed.</summary>
    bool TryCreate(string specification, [NotNullWhen(true)] out IRouteConstraint? constraint, [NotNullWhen(false)] out string? error);
}
