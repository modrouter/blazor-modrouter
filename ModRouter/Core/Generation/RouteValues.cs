using System.Collections;
using System.Reflection;

namespace ModRouter.Core.Generation;

/// <summary>Converts the values a caller hands in (an anonymous object, a dictionary) into a dictionary.</summary>
public static class RouteValues
{
    /// <summary>
    /// A read-only dictionary of objects passes through unchanged; any other dictionary (generic, string-keyed
    /// key/value sequence or non-generic) is copied case-insensitively; any other object contributes its public
    /// instance properties. Null is empty. Other enumerables are rejected rather than reflected over. Uses reflection:
    /// not for hot paths.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> From(object? values)
    {
        switch (values)
        {
            case null:
                return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            case IReadOnlyDictionary<string, object?> readOnly:
                return readOnly;
            case IDictionary<string, object?> dictionary:
                return new Dictionary<string, object?>(dictionary, StringComparer.OrdinalIgnoreCase);
            case IDictionary nonGeneric:
                return CopyNonGeneric(nonGeneric);
            case IEnumerable enumerable when values is not string:
                return CopyKeyValuePairs(enumerable);
        }

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in values.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length == 0)
            {
                result[property.Name] = property.GetValue(values);
            }
        }

        return result;
    }

    static Dictionary<string, object?> CopyNonGeneric(IDictionary dictionary)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (DictionaryEntry entry in dictionary)
        {
            result[entry.Key as string ?? throw NotSupported(dictionary)] = entry.Value;
        }

        return result;
    }

    static Dictionary<string, object?> CopyKeyValuePairs(IEnumerable enumerable)
    {
        var isStringKeyedPairs = enumerable.GetType().GetInterfaces().Any(i =>
            i.IsGenericType
            && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            && i.GetGenericArguments()[0] is { IsGenericType: true } pair
            && pair.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)
            && pair.GetGenericArguments()[0] == typeof(string));

        if (!isStringKeyedPairs)
        {
            throw NotSupported(enumerable);
        }

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in enumerable)
        {
            var type = item!.GetType();

            result[(string)type.GetProperty(nameof(KeyValuePair<string, object>.Key))!.GetValue(item)!]
                = type.GetProperty(nameof(KeyValuePair<string, object>.Value))!.GetValue(item);
        }

        return result;
    }

    static ArgumentException NotSupported(object values)
        => new($"Route values of type '{values.GetType().Name}' are not supported: pass an anonymous object, a dictionary with string keys, or null.", nameof(values));
}
