using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ModRouter.Core.Constraints;

/// <summary>
/// The default registry: every constraint of the built-in router (int, long, bool, decimal, double, float, guid,
/// datetime, alpha, min, max, range, length, minlength, maxlength, file, nonfile, regex; <c>BuiltInConstraintParityTests</c>
/// checks the list and the values against the framework) plus alphanum.
/// </summary>
public sealed class ConstraintRegistry : IConstraintRegistry
{
    readonly Dictionary<string, Func<string, IRouteConstraint>> _factories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a registry that already holds every built-in constraint. Names are matched ignoring case.</summary>
    public ConstraintRegistry()
    {
        RegisterBuiltIns();
    }

    /// <summary>
    /// Registers <paramref name="factory"/> under <paramref name="name"/> (ignoring case), replacing a constraint of
    /// the same name, built-ins included. Not thread-safe: register during startup, before routes are compiled.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null, empty or white space.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
    public void Register(string name, Func<string, IRouteConstraint> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);

        _factories[name] = factory;
    }

    /// <summary>
    /// Creates a constraint from a specification such as <c>int</c> or <c>length(2,40)</c>; false when the name is
    /// unknown, the closing parenthesis is missing or the factory rejects the argument.
    /// </summary>
    public bool TryCreate(string specification, [NotNullWhen(true)] out IRouteConstraint? constraint)
        => TryCreate(specification, out constraint, out _);

    /// <summary>
    /// Like the overload without <paramref name="error"/>, and says why it failed. A factory that throws is reported as
    /// an invalid-argument error, never rethrown.
    /// </summary>
    public bool TryCreate(string specification, [NotNullWhen(true)] out IRouteConstraint? constraint, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(specification);

        constraint = null;
        error = null;

        var open = specification.IndexOf('(');
        string name;
        var argument = string.Empty;

        if (open < 0)
        {
            name = specification;
        }
        else
        {
            if (!specification.EndsWith(')'))
            {
                error = $"The constraint '{specification}' is missing its closing ')'.";

                return false;
            }

            name = specification[..open];
            argument = specification[(open + 1)..^1];
        }

        if (!_factories.TryGetValue(name, out var factory))
        {
            error = $"Unknown route constraint '{name}'. Registered constraints: {string.Join(", ", _factories.Keys.Order(StringComparer.OrdinalIgnoreCase))}.";

            return false;
        }

        try
        {
            constraint = factory(argument);

            return true;
        }
        catch (Exception e)
        {
            error = $"Invalid argument for constraint '{name}': {e.Message}";

            return false;
        }
    }

    void RegisterBuiltIns()
    {
        Register("int", a => NoArgument("int", a, new TypedConstraint<int>("int", ParseInt)));
        Register("long", a => NoArgument("long", a, new TypedConstraint<long>("long", ParseLong)));
        Register("bool", a => NoArgument("bool", a, new TypedConstraint<bool>("bool", ParseBool)));
        Register("decimal", a => NoArgument("decimal", a, new TypedConstraint<decimal>("decimal", ParseDecimal)));
        Register("double", a => NoArgument("double", a, new TypedConstraint<double>("double", ParseDouble)));
        Register("float", a => NoArgument("float", a, new TypedConstraint<float>("float", ParseFloat)));
        Register("guid", a => NoArgument("guid", a, new TypedConstraint<Guid>("guid", ParseGuid)));
        Register("datetime", a => NoArgument("datetime", a, new TypedConstraint<DateTime>("datetime", ParseDateTime)));
        Register("alpha", a => NoArgument("alpha", a, new StringConstraint("alpha", static s => s.Length > 0 && s.All(char.IsAsciiLetter))));
        Register("alphanum", a => NoArgument("alphanum", a, new StringConstraint("alphanum", static s => s.Length > 0 && s.All(char.IsAsciiLetterOrDigit))));
        Register("min", a => new RangeConstraint(ParseBound(a), null));
        Register("max", a => new RangeConstraint(null, ParseBound(a)));
        Register("range", CreateRange);
        Register("length", CreateLength);
        Register("minlength", a => CreateLengthBound("minlength", a, isMinimum: true));
        Register("maxlength", a => CreateLengthBound("maxlength", a, isMinimum: false));
        Register("file", a => NoArgument("file", a, new StringConstraint("file", IsFileName)));
        Register("nonfile", a => NoArgument("nonfile", a, new StringConstraint("nonfile", static s => !IsFileName(s))));
        Register("regex", CreateRegex);
    }

    static IRouteConstraint NoArgument(string name, string argument, IRouteConstraint constraint)
        => argument.Length == 0 ? constraint : throw new ArgumentException($"'{name}' does not take an argument.");

    static long ParseBound(string argument)
        => long.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bound)
            ? bound
            : throw new FormatException($"'{argument}' is not a whole number.");

    static IRouteConstraint CreateRange(string argument)
    {
        var parts = argument.Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length != 2 || parts.Any(p => !long.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            throw new FormatException("range expects 'range(min,max)' with whole numbers.");
        }

        var minimum = long.Parse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture);
        var maximum = long.Parse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture);

        if (maximum < minimum)
        {
            throw new ArgumentException("the maximum is smaller than the minimum.");
        }

        return new RangeConstraint(minimum, maximum);
    }

    static IRouteConstraint CreateLengthBound(string name, string argument, bool isMinimum)
    {
        if (!int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out var bound))
        {
            throw new FormatException($"{name} expects '{name}(n)' with a whole number.");
        }

        return new StringConstraint($"{name}({argument})", isMinimum ? s => s.Length >= bound : s => s.Length <= bound);
    }

    /// <summary>
    /// The built-in router's file-name rule: the last path segment has a '.' followed by at least one character that is not
    /// a '.' (<c>report.pdf</c>, <c>.htaccess</c>; not <c>readme</c>, <c>file.</c>, <c>dir.d/report</c> or an empty value).
    /// </summary>
    static bool IsFileName(string value)
    {
        var name = value.AsSpan();
        var slash = name.LastIndexOf('/');

        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }

        var dot = name.IndexOf('.');

        if (dot < 0)
        {
            return false;
        }

        for (var i = dot + 1; i < name.Length; i++)
        {
            if (name[i] != '.')
            {
                return true;
            }
        }

        return false;
    }

    static IRouteConstraint CreateLength(string argument)
    {
        var parts = argument.Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length > 2 || parts.Any(p => !int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            throw new FormatException("length expects 'length(n)' or 'length(min,max)' with whole numbers.");
        }

        var minimum = int.Parse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture);
        var maximum = parts.Length == 2 ? int.Parse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture) : minimum;

        if (maximum < minimum)
        {
            throw new ArgumentException("the maximum is smaller than the minimum.");
        }

        return new StringConstraint($"length({argument})", s => s.Length >= minimum && s.Length <= maximum);
    }

    static IRouteConstraint CreateRegex(string argument)
    {
        if (argument.Length == 0)
        {
            throw new ArgumentException("regex expects a pattern, for example regex(^[a-z]+$).");
        }

        // Built once at compile time. Unanchored: add ^ and $ for a whole-segment match.
        var regex = new Regex(argument, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));

        return new StringConstraint($"regex({argument})", s =>
        {
            try
            {
                return regex.IsMatch(s);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        });
    }

    static bool ParseInt(string text, out int value) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    static bool ParseLong(string text, out long value) => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    static bool ParseBool(string text, out bool value) => bool.TryParse(text, out value);

    static bool ParseDecimal(string text, out decimal value) => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    static bool ParseDouble(string text, out double value) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    static bool ParseFloat(string text, out float value) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    static bool ParseGuid(string text, out Guid value) => Guid.TryParse(text, out value);

    static bool ParseDateTime(string text, out DateTime value) => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
}
