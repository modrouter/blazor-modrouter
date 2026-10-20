using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace ModRouter.Test;

/// <summary>
/// Helpers for asserting on components that re-render off the test thread (streams, deferred values, timers). bUnit's
/// Find/FindAll read a snapshot that is refreshed lazily and can miss such a render, and WaitForAssertion only re-checks
/// when the component renders, so these read the always-current markup and poll.
/// </summary>
internal static class ComponentTestKit
{
    public static RenderFragment Markup(string html) => builder => builder.AddMarkupContent(0, html);

    public static IReadOnlyList<IElement> All<T>(IRenderedComponent<T> cut, string selector)
        where T : IComponent
        => [.. new HtmlParser().ParseDocument($"<body>{cut.Markup}</body>").QuerySelectorAll(selector)];

    public static IElement One<T>(IRenderedComponent<T> cut, string selector)
        where T : IComponent
        => All(cut, selector) is [var only] ? only : throw new InvalidOperationException($"expected exactly one '{selector}' in: {cut.Markup}");

    /// <summary>
    /// Asserts that <paramref name="element"/> is inside the router's error host, the <c>display: contents</c> wrapper that
    /// scopes the browser module's alert fallback to the router's own error content.
    /// </summary>
    public static void AssertInErrorHost(IElement element)
    {
        var host = element.Closest("[data-mod-route-error-host]");

        Assert.That(host, Is.Not.Null, () => $"error content is not wrapped in the error host: {element.OuterHtml}");
        Assert.That(host!.GetAttribute("style"), Is.EqualTo("display:contents"), "the host must not change the layout");
    }

    /// <summary>
    /// Polls until <paramref name="constraint"/> holds for <paramref name="actual"/>, then asserts it. It must not call
    /// Assert inside the loop: NUnit remembers a failed assertion even when the exception is caught.
    /// </summary>
    public static async Task EventuallyAsync<T>(Func<T> actual, NUnit.Framework.Constraints.IResolveConstraint constraint)
    {
        var resolved = constraint.Resolve();

        for (var attempt = 0; attempt < 500; attempt++)
        {
            try
            {
                if (resolved.ApplyTo(actual()).IsSuccess)
                {
                    return;
                }
            }
            catch (Exception exception) when (exception is Bunit.ElementNotFoundException or InvalidOperationException or ArgumentOutOfRangeException)
            {
                // Not rendered yet.
            }

            await Task.Delay(10);
        }

        Assert.That(actual(), constraint);
    }
}
