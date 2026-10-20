using System.Globalization;

namespace ModRouter.Core.Constraints;

/// <summary>
/// Marks a constraint that only narrows a value (alpha, length, min, ...) rather than defining its type. In a
/// composite such as <c>{x:mine:int}</c> the converted value comes from the first constraint that is not a
/// refinement, so a custom narrowing constraint should implement this.
/// </summary>
public interface IRefinementConstraint : IRouteConstraint
{
}

internal delegate bool TryParseDelegate<T>(string text, out T value);

/// <summary>A constraint that parses to a CLR type (int, guid, datetime, ...).</summary>
internal sealed class TypedConstraint<T>(string name, TryParseDelegate<T> parse) : IRouteConstraint
{
    public string Name { get; } = name;

    public Type ValueType => typeof(T);

    public bool TryMatch(string rawValue, out object? convertedValue)
    {
        if (parse(rawValue, out var value))
        {
            convertedValue = value;

            return true;
        }

        convertedValue = null;

        return false;
    }

    public bool TryFormat(object value, out string text)
    {
        text = string.Empty;

        switch (value)
        {
            case T typed:
                text = ValueFormatter.Format(typed!);

                return true;
            case string raw:
                if (!parse(raw, out _))
                {
                    return false;
                }

                text = raw;

                return true;
        }

        if (value is DateTimeOffset offset && typeof(T) == typeof(DateTime))
        {
            text = ValueFormatter.Format(offset.DateTime);

            return true;
        }

        if (IsNumeric(value.GetType()) && IsNumeric(typeof(T)) && TryConvertNumber(value, out var converted))
        {
            text = ValueFormatter.Format(converted);

            return true;
        }

        return false;
    }

    // Converts between numeric types but refuses a lossy result such as 1.5 to int, or an overflow.
    static bool TryConvertNumber(object value, out object converted)
    {
        converted = value;

        try
        {
            converted = Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);

            return Convert.ToDecimal(converted, CultureInfo.InvariantCulture) == Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
        catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException)
        {
            return false;
        }
    }

    static bool IsNumeric(Type type)
        => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
            || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
            || type == typeof(float) || type == typeof(double) || type == typeof(decimal);
}

/// <summary>A string constraint defined by a predicate (alpha, alphanum, length, regex).</summary>
internal sealed class StringConstraint(string description, Func<string, bool> predicate) : IRefinementConstraint
{
    public string Description { get; } = description;

    public Type ValueType => typeof(string);

    public bool TryMatch(string rawValue, out object? convertedValue)
    {
        convertedValue = rawValue;

        return predicate(rawValue);
    }

    public bool TryFormat(object value, out string text)
    {
        text = ValueFormatter.Format(value);

        return predicate(text);
    }
}

/// <summary>min(n), max(n) and range(min,max): integer bounds, either of which may be absent.</summary>
internal sealed class RangeConstraint(long? minimum, long? maximum) : IRefinementConstraint
{
    public Type ValueType => typeof(long);

    public bool TryMatch(string rawValue, out object? convertedValue)
    {
        if (long.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && InRange(number))
        {
            convertedValue = number;

            return true;
        }

        convertedValue = null;

        return false;
    }

    public bool TryFormat(object value, out string text)
    {
        text = string.Empty;
        long number;

        switch (value)
        {
            case sbyte or byte or short or ushort or int or uint or long:
                number = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                break;
            case string raw when long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed):
                number = parsed;
                break;
            default:
                return false;
        }

        if (!InRange(number))
        {
            return false;
        }

        text = number.ToString(CultureInfo.InvariantCulture);

        return true;
    }

    bool InRange(long number) => (minimum is not { } low || number >= low) && (maximum is not { } high || number <= high);
}
