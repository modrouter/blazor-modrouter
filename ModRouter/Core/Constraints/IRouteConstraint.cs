namespace ModRouter.Core.Constraints;

/// <summary>
/// A parsed, compiled route constraint. Instances must be immutable and thread-safe: the compiler creates each
/// one once and the matcher and URL generator share it. Implementations must not throw from <see cref="TryMatch"/> or
/// <see cref="TryFormat"/>: <c>CompiledRouteTable.Match</c> does not catch, so an exception from a custom constraint
/// escapes it (the router's navigation coordinator logs it and fails that navigation).
/// </summary>
public interface IRouteConstraint
{
    /// <summary>The CLR type of the converted value (<see cref="string"/> when no conversion happens).</summary>
    Type ValueType { get; }

    /// <summary>Tests a decoded path segment and converts it.</summary>
    bool TryMatch(string rawValue, out object? convertedValue);

    /// <summary>Formats a value for URL generation; false if the value cannot satisfy the constraint.</summary>
    bool TryFormat(object value, out string text);
}
