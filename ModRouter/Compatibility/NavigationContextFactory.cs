using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Components.Routing;

namespace ModRouter.Compatibility;

/// <summary>
/// Creates the framework's <see cref="NavigationContext"/> for the compatibility Router's <c>OnNavigateAsync</c>. Its
/// only constructor is internal (<c>NavigationContext(string path, CancellationToken cancellationToken)</c>), so
/// <see cref="Create"/> reaches it with <see cref="UnsafeAccessorAttribute"/>: no runtime reflection, and the trimmer
/// keeps the constructor as it would for any static call. <see cref="ToBaseRelativePath"/> gives the path the built-in
/// hands the callback (<c>NavigationManager.ToBaseRelativePath</c>: no leading slash, query and fragment kept).
/// </summary>
internal static class NavigationContextFactory
{
    [UnsafeAccessor(UnsafeAccessorKind.Constructor)]
    public static extern NavigationContext Create(string path, CancellationToken cancellationToken);

    /// <summary>
    /// <paramref name="uri"/> relative to <paramref name="baseUri"/>, as <c>NavigationManager.ToBaseRelativePath</c>
    /// computes it: empty at the base itself, otherwise the remainder without a leading slash, query and fragment kept.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="uri"/> is not under <paramref name="baseUri"/>.</exception>
    public static string ToBaseRelativePath(string baseUri, string uri)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        ArgumentNullException.ThrowIfNull(uri);

        if (uri.StartsWith(baseUri, StringComparison.Ordinal))
        {
            return uri[baseUri.Length..];
        }

        var trimmed = baseUri.EndsWith('/') ? baseUri[..^1] : baseUri;

        if (string.Equals(uri, trimmed, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (uri.StartsWith(trimmed + "?", StringComparison.Ordinal) || uri.StartsWith(trimmed + "#", StringComparison.Ordinal))
        {
            return uri[trimmed.Length..];
        }

        throw new ArgumentException($"The URI '{uri}' is not contained by the base URI '{baseUri}'.", nameof(uri));
    }
}
