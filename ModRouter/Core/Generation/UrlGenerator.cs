using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using ModRouter.Core.Compilation;
using ModRouter.Core.Constraints;
using ModRouter.Core.Syntax;

namespace ModRouter.Core.Generation;

/// <summary>Builds URLs from a canonical <see cref="CompiledRoute"/>: the same flattened data the matcher uses.</summary>
internal static class UrlGenerator
{
    public static bool TryGenerate(
        CompiledRoute route,
        IReadOnlyDictionary<string, object?>? parameters,
        IReadOnlyDictionary<string, object?>? query,
        string? fragment,
        IReadOnlyDictionary<string, object?>? ambientValues,
        [NotNullWhen(true)] out string? url,
        [NotNullWhen(false)] out RouteUrlGenerationError? error)
    {
        url = null;
        error = null;

        var routeName = route.Definition.Name;
        var display = routeName ?? route.CanonicalTemplate;
        var explicitValues = Copy(parameters);
        var ambient = Copy(ambientValues);
        var declared = route.Segments.Where(s => s.IsParameter).Select(s => s.Text).ToList();

        // A null value counts as absent, so an explicit null for an undeclared name is not an error.
        var unknown = explicitValues
            .Where(pair => pair.Value is not null && !declared.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (unknown.Count > 0)
        {
            error = new RouteUrlGenerationError(
                UrlGenerationErrorCode.UnknownParameter,
                $"Route '{display}' has no parameter named {Quote(unknown)}; its template '{route.CanonicalTemplate}' declares {(declared.Count == 0 ? "no parameters" : Quote(declared))}. Pass query-string values through the query argument.",
                routeName);

            return false;
        }

        var path = new StringBuilder();
        string? skippedOptional = null;

        for (var i = 0; i < route.Segments.Count; i++)
        {
            var segment = route.Segments[i];

            if (segment.Kind == TemplateSegmentKind.Literal)
            {
                path.Append('/').Append(Uri.EscapeDataString(segment.Text));

                continue;
            }

            if (!TryGetValue(segment.Text, explicitValues, ambient, out var value))
            {
                if (segment.Kind == TemplateSegmentKind.Parameter)
                {
                    error = new RouteUrlGenerationError(
                        UrlGenerationErrorCode.MissingParameter,
                        $"Route '{display}' requires parameter '{segment.Text}', but no value was supplied while generating the URL '{route.CanonicalTemplate}'.",
                        routeName);

                    return false;
                }

                skippedOptional ??= segment.Text;

                continue;
            }

            if (segment.Kind == TemplateSegmentKind.CatchAll)
            {
                foreach (var part in ValueFormatter.Format(value).Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (IsDotSegment(part))
                    {
                        error = DotSegmentError(segment.Text, display, route, routeName);

                        return false;
                    }

                    path.Append('/').Append(Uri.EscapeDataString(part));
                }

                continue;
            }

            if (skippedOptional is not null)
            {
                error = new RouteUrlGenerationError(
                    UrlGenerationErrorCode.OptionalGap,
                    $"Route '{display}' was given a value for the optional parameter '{segment.Text}' but not for '{skippedOptional}', which comes before it in '{route.CanonicalTemplate}'. Optional parameters can only be omitted from the right.",
                    routeName);

                return false;
            }

            string text;
            var constraint = route.Constraints[i];

            if (constraint is not null)
            {
                if (!constraint.TryFormat(value, out text) || !constraint.TryMatch(text, out _))
                {
                    error = new RouteUrlGenerationError(
                        UrlGenerationErrorCode.ConstraintViolation,
                        $"The value '{ValueFormatter.Format(value)}' for parameter '{segment.Text}' does not satisfy the constraint '{segment.ConstraintKey}' of route '{display}' ('{route.CanonicalTemplate}').",
                        routeName);

                    return false;
                }
            }
            else
            {
                text = ValueFormatter.Format(value);
            }

            if (text.Length == 0)
            {
                error = new RouteUrlGenerationError(
                    UrlGenerationErrorCode.ConstraintViolation,
                    $"The value for parameter '{segment.Text}' of route '{display}' ('{route.CanonicalTemplate}') is empty; a path segment cannot be empty.",
                    routeName);

                return false;
            }

            if (IsDotSegment(text))
            {
                error = DotSegmentError(segment.Text, display, route, routeName);

                return false;
            }

            path.Append('/').Append(Uri.EscapeDataString(text));
        }

        if (path.Length == 0)
        {
            path.Append('/');
        }

        AppendQuery(path, query);
        AppendFragment(path, fragment);

        url = path.ToString();

        return true;
    }

    // Explicit values (including an explicit null, meaning "omit") win; ambient values only fill names with no explicit entry.
    static bool TryGetValue(
        string name,
        Dictionary<string, object?> explicitValues,
        Dictionary<string, object?> ambient,
        [NotNullWhen(true)] out object? value)
    {
        if (explicitValues.TryGetValue(name, out value))
        {
            return value is not null;
        }

        return ambient.TryGetValue(name, out value) && value is not null;
    }

    static void AppendQuery(StringBuilder path, IReadOnlyDictionary<string, object?>? query)
    {
        if (query is null || query.Count == 0)
        {
            return;
        }

        var parts = new List<string>();

        foreach (var key in query.Keys.Order(StringComparer.Ordinal))
        {
            foreach (var item in Expand(query[key]))
            {
                parts.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(ValueFormatter.Format(item))}");
            }
        }

        if (parts.Count > 0)
        {
            path.Append('?').Append(string.Join('&', parts));
        }
    }

    static void AppendFragment(StringBuilder path, string? fragment)
    {
        var trimmed = fragment?.TrimStart('#');

        if (!string.IsNullOrEmpty(trimmed))
        {
            path.Append('#').Append(Uri.EscapeDataString(trimmed));
        }
    }

    static IEnumerable<object> Expand(object? value)
    {
        switch (value)
        {
            case null:
                yield break;
            case string text:
                yield return text;

                break;
            case IEnumerable items:
                foreach (var item in items)
                {
                    if (item is not null)
                    {
                        yield return item;
                    }
                }

                break;
            default:
                yield return value;

                break;
        }
    }

    static Dictionary<string, object?> Copy(IReadOnlyDictionary<string, object?>? source)
    {
        var copy = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (source is not null)
        {
            foreach (var pair in source)
            {
                copy[pair.Key] = pair.Value;
            }
        }

        return copy;
    }

    // A "." or ".." segment is collapsed by browsers and Uri before the request is made, so it cannot round-trip.
    static bool IsDotSegment(string text) => text is "." or "..";

    static RouteUrlGenerationError DotSegmentError(string parameter, string display, CompiledRoute route, string? routeName)
        => new(
            UrlGenerationErrorCode.ConstraintViolation,
            $"The value for parameter '{parameter}' of route '{display}' ('{route.CanonicalTemplate}') is a dot segment ('.' or '..'), which browsers and URI parsing collapse, so the URL would not round-trip.",
            routeName);

    static string Quote(IEnumerable<string> names) => string.Join(", ", names.Select(n => $"'{n}'"));
}
