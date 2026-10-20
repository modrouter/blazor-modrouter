using System.Globalization;
using ModRouter.Core.Constraints;

namespace ModRouter.Components;

/// <summary>Converts a matched route value to the type of a component parameter (culture-invariant).</summary>
internal static class ValueConverter
{
    public static bool TryConvert(object? value, Type target, out object? result)
    {
        var underlying = Nullable.GetUnderlyingType(target);
        var type = underlying ?? target;

        if (value is null)
        {
            result = null;

            return underlying is not null || !target.IsValueType;
        }

        if (type.IsInstanceOfType(value))
        {
            result = value;

            return true;
        }

        if (type == typeof(string))
        {
            result = ValueFormatter.Format(value);

            return true;
        }

        if (value is string text)
        {
            return TryParse(text, type, out result);
        }

        if (type.IsEnum && value is IConvertible)
        {
            try
            {
                result = Enum.ToObject(type, value);

                return Enum.IsDefined(type, result);
            }
            catch (ArgumentException)
            {
                result = null;

                return false;
            }
        }

        if (IsNumeric(value.GetType()) && IsNumeric(type))
        {
            return TryConvertNumber(value, type, out result);
        }

        if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(type))
        {
            try
            {
                result = Convert.ChangeType(value, type, CultureInfo.InvariantCulture);

                return true;
            }
            catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException)
            {
                result = null;

                return false;
            }
        }

        result = null;

        return false;
    }

    /// <summary>
    /// Whether a value of <paramref name="source"/> could ever be converted to <paramref name="target"/>; used for
    /// startup validation. Numeric pairs allow widening only: a floating-point or decimal source can never bind to an
    /// integral (or enum) target, because the only values that would convert are the whole ones.
    /// </summary>
    public static bool CanEverConvert(Type source, Type target)
    {
        var type = Nullable.GetUnderlyingType(target) ?? target;

        if (type.IsAssignableFrom(source) || type == typeof(string))
        {
            return true;
        }

        if (source == typeof(string))
        {
            return type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type.IsEnum || typeof(IConvertible).IsAssignableFrom(type);
        }

        if (IsNumeric(source))
        {
            return IsFloatingPoint(source) ? IsFloatingPoint(type) : IsNumeric(type) || type.IsEnum;
        }

        return false;
    }

    // Converts between numeric types but refuses a lossy result (2.5 to int would be rounded to 2) or an overflow.
    // The converted value is converted back to the source type and compared with the original, so a widening such
    // as float 0.1f to double always passes while any truncation or rounding fails.
    static bool TryConvertNumber(object value, Type type, out object? result)
    {
        try
        {
            result = Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
            var roundTrip = Convert.ChangeType(result, value.GetType(), CultureInfo.InvariantCulture);

            if (Equals(roundTrip, value))
            {
                return true;
            }
        }
        catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException)
        {
        }

        result = null;

        return false;
    }

    static bool TryParse(string text, Type type, out object? result)
    {
        result = null;

        if (type == typeof(Guid))
        {
            if (Guid.TryParse(text, out var guid))
            {
                result = guid;

                return true;
            }

            return false;
        }

        if (type == typeof(DateTime))
        {
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
            {
                result = date;

                return true;
            }

            return false;
        }

        if (type == typeof(DateTimeOffset))
        {
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var offset))
            {
                result = offset;

                return true;
            }

            return false;
        }

        if (type.IsEnum)
        {
            if (Enum.TryParse(type, text, ignoreCase: true, out var parsed) && Enum.IsDefined(type, parsed!))
            {
                result = parsed;

                return true;
            }

            return false;
        }

        if (typeof(IConvertible).IsAssignableFrom(type))
        {
            try
            {
                result = Convert.ChangeType(text, type, CultureInfo.InvariantCulture);

                return true;
            }
            catch (Exception e) when (e is InvalidCastException or FormatException or OverflowException)
            {
                return false;
            }
        }

        return false;
    }

    static bool IsNumeric(Type type)
        => type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
            || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong)
            || IsFloatingPoint(type);

    static bool IsFloatingPoint(Type type) => type == typeof(float) || type == typeof(double) || type == typeof(decimal);
}
