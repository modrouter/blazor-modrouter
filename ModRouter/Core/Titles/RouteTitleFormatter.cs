namespace ModRouter.Core.Titles;

/// <summary>
/// Turns a route's title into the browser title. A titled route is formatted (<c>string.Format</c> with the title as
/// <c>{0}</c>); an untitled route uses the default title as given. Both are plain functions so the rules are tested
/// without a browser.
/// </summary>
public static class RouteTitleFormatter
{
    /// <summary>The default format: the title itself.</summary>
    public const string DefaultFormat = "{0}";

    /// <summary>
    /// The browser title for a route, or null when the document title should be left as it is (an untitled route and no
    /// <paramref name="defaultTitle"/>). <paramref name="defaultTitle"/> is a complete title: the format is not applied to it.
    /// </summary>
    public static string? Resolve(string? routeTitle, string format, string? defaultTitle)
    {
        ArgumentNullException.ThrowIfNull(format);

        return string.IsNullOrWhiteSpace(routeTitle) ? NullIfEmpty(defaultTitle) : string.Format(format, routeTitle);
    }

    /// <summary>Throws <see cref="ArgumentException"/> when <paramref name="format"/> is not a valid <c>string.Format</c> pattern for one argument.</summary>
    public static void Validate(string format)
    {
        ArgumentNullException.ThrowIfNull(format);

        try
        {
            _ = string.Format(format, "title");
        }
        catch (FormatException exception)
        {
            throw new ArgumentException($"The title format '{format}' is not valid: {exception.Message} Use {{0}} for the route title.", nameof(format), exception);
        }
    }

    static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;
}
