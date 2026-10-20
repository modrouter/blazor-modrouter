using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace ModRouter.Core.Syntax;

/// <summary>A syntax problem in a template and the character position it was found at.</summary>
internal sealed record TemplateError(string Message, int Position);

/// <summary>The result of parsing one template string.</summary>
internal sealed record TemplateParseResult(IReadOnlyList<TemplateSegment> Segments, IReadOnlyList<TemplateError> Errors, bool HasLeadingSlash)
{
    public bool Succeeded => Errors.Count == 0;
}

/// <summary>Parses route templates such as <c>/customers/{id:int}/orders/{orderId:guid}</c>.</summary>
internal static partial class TemplateParser
{
    public static TemplateParseResult Parse(string template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var segments = new List<TemplateSegment>();
        var errors = new List<TemplateError>();
        var hasLeadingSlash = template.StartsWith('/');
        var position = hasLeadingSlash ? 1 : 0;
        var end = template.Length;

        var trimmedTrailingSlash = false;

        if (end > position && template[end - 1] == '/')
        {
            end--;
            trimmedTrailingSlash = true;
        }

        if (trimmedTrailingSlash && end == position && hasLeadingSlash)
        {
            errors.Add(new TemplateError("Empty segment: '//' is not allowed.", 1));
        }

        while (position < end)
        {
            position = ParseSegment(template, position, end, segments, errors);

            if (position < end)
            {
                position++;

                if (position >= end)
                {
                    errors.Add(new TemplateError("Empty segment: the template ends with '//'.", position - 1));
                }
            }
        }

        return new TemplateParseResult(ReadOnlyLists.Seal(segments), ReadOnlyLists.Seal(errors), hasLeadingSlash);
    }

    static int ParseSegment(string template, int start, int end, List<TemplateSegment> segments, List<TemplateError> errors)
    {
        if (template[start] == '/')
        {
            errors.Add(new TemplateError("Empty segment: '//' is not allowed.", start));

            return start;
        }

        if (template[start] == '{')
        {
            var close = FindClosingBrace(template, start + 1, end);

            if (close < 0)
            {
                errors.Add(new TemplateError("Unclosed '{': add a matching '}'.", start));

                return end;
            }

            ParseParameter(template.Substring(start + 1, close - start - 1), start, segments, errors);

            var next = close + 1;

            if (next < end && template[next] != '/')
            {
                errors.Add(new TemplateError("A segment must be either literal text or a single {parameter}; text after '}' is not allowed.", next));

                while (next < end && template[next] != '/')
                {
                    next++;
                }
            }

            return next;
        }

        var stop = start;

        while (stop < end && template[stop] != '/')
        {
            stop++;
        }

        var text = template.Substring(start, stop - start);

        if (text.Contains('{') || text.Contains('}'))
        {
            errors.Add(new TemplateError($"The segment '{text}' mixes literal text with a parameter or has an unmatched brace; a segment must be either literal text or a single {{parameter}}.", start));
        }
        else if (text.Contains('?') || text.Contains('#'))
        {
            errors.Add(new TemplateError($"The literal segment '{text}' contains '?' or '#', which start the query string or fragment, so it can never match a path segment.", start));
        }
        else if (PercentEscapeRegex().IsMatch(text))
        {
            errors.Add(new TemplateError($"The literal segment '{text}' contains a percent-encoded sequence. Write the character itself (for example café); paths are decoded before matching, so the encoded form can never match.", start));
        }
        else
        {
            segments.Add(new TemplateSegment(TemplateSegmentKind.Literal, text, []));
        }

        return stop;
    }

    // A '}' ends the parameter only outside parentheses, so a constraint argument such as regex(^[a-z]{3}$) may contain braces.
    static int FindClosingBrace(string template, int from, int end)
    {
        var parenDepth = 0;

        for (var i = from; i < end; i++)
        {
            switch (template[i])
            {
                case '(':
                    parenDepth++;
                    break;
                case ')':
                    if (parenDepth > 0)
                    {
                        parenDepth--;
                    }

                    break;
                case '}' when parenDepth == 0:
                    return i;
            }
        }

        return -1;
    }

    static List<string> SplitTopLevel(string text, char separator)
    {
        var parts = new List<string>();
        var parenDepth = 0;
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '(')
            {
                parenDepth++;
            }
            else if (text[i] == ')' && parenDepth > 0)
            {
                parenDepth--;
            }
            else if (text[i] == separator && parenDepth == 0)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }

        parts.Add(text[start..]);

        return parts;
    }

    static void ParseParameter(string body, int position, List<TemplateSegment> segments, List<TemplateError> errors)
    {
        var optional = false;

        if (body.EndsWith('?'))
        {
            optional = true;
            body = body[..^1];
        }

        var parts = SplitTopLevel(body, ':');
        var name = parts[0];
        var catchAll = false;

        if (name.StartsWith("**", StringComparison.Ordinal))
        {
            catchAll = true;
            name = name[2..];
        }
        else if (name.StartsWith('*'))
        {
            catchAll = true;
            name = name[1..];
        }

        if (!ParameterNameRegex().IsMatch(name))
        {
            errors.Add(new TemplateError($"'{name}' is not a valid parameter name; use letters, digits and '_', starting with a letter or '_'.", position));

            return;
        }

        var constraints = new List<ConstraintSpec>();

        for (var i = 1; i < parts.Count; i++)
        {
            if (TryParseConstraint(parts[i], out var spec, out var error))
            {
                constraints.Add(spec);
            }
            else
            {
                errors.Add(new TemplateError($"Parameter '{name}': {error}", position));
            }
        }

        if (catchAll && optional)
        {
            errors.Add(new TemplateError($"Parameter '{name}': a catch-all parameter cannot also be optional ('?'); a catch-all already matches zero or more segments.", position));
        }

        if (catchAll && constraints.Count > 0)
        {
            errors.Add(new TemplateError($"Parameter '{name}': constraints are not supported on catch-all parameters.", position));
        }

        var kind = catchAll
            ? TemplateSegmentKind.CatchAll
            : optional ? TemplateSegmentKind.OptionalParameter : TemplateSegmentKind.Parameter;

        segments.Add(new TemplateSegment(kind, name, constraints));
    }

    static bool TryParseConstraint(string part, [NotNullWhen(true)] out ConstraintSpec? spec, [NotNullWhen(false)] out string? error)
    {
        spec = null;
        error = null;

        if (part.Length == 0)
        {
            error = "an empty constraint was found; remove the extra ':'.";

            return false;
        }

        var open = part.IndexOf('(');
        string name;
        string? argument = null;

        if (open < 0)
        {
            name = part;
        }
        else
        {
            if (!part.EndsWith(')'))
            {
                error = $"constraint '{part}' is missing its closing ')'.";

                return false;
            }

            name = part[..open];
            argument = part[(open + 1)..^1];
        }

        if (!ConstraintNameRegex().IsMatch(name))
        {
            error = $"'{name}' is not a valid constraint name.";

            return false;
        }

        spec = new ConstraintSpec(name, argument);

        return true;
    }

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex ParameterNameRegex();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*$")]
    private static partial Regex ConstraintNameRegex();

    [GeneratedRegex("%[0-9A-Fa-f]{2}")]
    private static partial Regex PercentEscapeRegex();
}
