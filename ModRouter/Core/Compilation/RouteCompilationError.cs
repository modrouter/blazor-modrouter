namespace ModRouter.Core.Compilation;

/// <summary>One problem found while compiling route definitions.</summary>
public sealed record RouteCompilationError(
    string Code,
    string Message,
    string? RouteName = null,
    string? Template = null,
    string? Source = null,
    Type? Component = null,
    string? OtherRoute = null,
    int? Position = null)
{
    /// <summary>
    /// The code and the message (<c>RT0010: ...</c>), the form <see cref="RouteCompilationException"/> uses for each
    /// line of its own message.
    /// </summary>
    public override string ToString() => $"{Code}: {Message}";
}

/// <summary>Thrown by <see cref="RouteCompiler"/> with every problem found, not just the first.</summary>
public sealed class RouteCompilationException : Exception
{
    /// <summary>
    /// Creates the exception for <paramref name="errors"/>; the message is a header with the error count followed by
    /// one line per error.
    /// </summary>
    public RouteCompilationException(IReadOnlyList<RouteCompilationError> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors;
    }

    /// <summary>With the exception that caused the problem (a page source whose discovery threw).</summary>
    public RouteCompilationException(IReadOnlyList<RouteCompilationError> errors, Exception innerException)
        : base(BuildMessage(errors), innerException)
    {
        Errors = errors;
    }

    /// <summary>
    /// Every problem found, in the order the compiler reported them. The compiler reports all problems of a compilation
    /// together rather than stopping at the first.
    /// </summary>
    public IReadOnlyList<RouteCompilationError> Errors { get; }

    static string BuildMessage(IReadOnlyList<RouteCompilationError> errors)
    {
        var header = $"Route table compilation failed with {errors.Count} {(errors.Count == 1 ? "error" : "errors")}:";

        return header + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => $"  {e}"));
    }
}
