using System.Globalization;

namespace ModRouter.Core.Constraints;

/// <summary>Culture-invariant formatting shared by constraints, URL generation and query strings.</summary>
internal static class ValueFormatter
{
    public static string Format(object value) => value switch
    {
        string text => text,
        bool flag => flag ? "true" : "false",
        DateTime date => date.TimeOfDay == TimeSpan.Zero
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : date.ToString("o", CultureInfo.InvariantCulture),
        DateTimeOffset offset => offset.ToString("o", CultureInfo.InvariantCulture),
        Guid guid => guid.ToString("D"),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
