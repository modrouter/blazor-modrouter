namespace ModRouter;

/// <summary>How the browser title is produced from the matched route's title (<c>ModRouterOptions.Title</c>).</summary>
public sealed class TitleOptions
{
    /// <summary><c>string.Format</c> pattern with the route title as <c>{0}</c>, for example <c>"{0} - Acme"</c>.</summary>
    public string Format { get; set; } = "{0}";

    /// <summary>
    /// The complete title of a route that has none (the format is not applied to it). Null leaves the document title as it
    /// is, which is also what happens for a redirect loop or a failed first navigation when this is null.
    /// </summary>
    public string? Default { get; set; }

    /// <summary>The complete title when no route matches the URL.</summary>
    public string NotFound { get; set; } = "Not found";
}

/// <summary>The screen-reader announcement of a new page (<c>ModRouterOptions.Announce</c>).</summary>
public sealed class AnnounceOptions
{
    /// <summary>When true (the default), a polite live region announces the new page's title after a new navigation.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>Moving keyboard focus to the new page (<c>ModRouterOptions.Focus</c>).</summary>
public sealed class FocusOptions
{
    /// <summary>When true (the default), focus moves to the page heading after a new navigation. Back, forward and the first load never move it.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>CSS selector of the element to focus inside the router's content; a route can override it with <c>.Focus(...)</c>.</summary>
    public string Selector { get; set; } = "h1";

    /// <summary>
    /// How long to wait for the focus target (and a fragment target) to appear, for a page whose heading renders after
    /// an <c>await</c>. The wait is abandoned when a newer page commits or the user presses a mouse button, touches the
    /// screen, turns the mouse wheel or presses a key meanwhile (dragging the window's scrollbar does not count).
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// Animating the change from one page to the next with the browser's View Transitions API (<c>ModRouterOptions.ViewTransitions</c>).
/// Style the animation with the <c>::view-transition-old(root)</c> and <c>::view-transition-new(root)</c> pseudo-elements.
/// </summary>
public sealed class ViewTransitionOptions
{
    /// <summary>
    /// When true, a navigation that changes the page is wrapped in <c>document.startViewTransition</c>: the browser keeps a picture
    /// of the old page while the new one renders, then cross-fades (or runs your CSS). Off by default. It does nothing in a browser
    /// without the API, for a user who prefers reduced motion, for the first load, for a change of query or fragment only, or while
    /// the router is statically rendered.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The longest the old page is kept on screen while the new one renders (a page that loads over a slow circuit); after it the
    /// page updates without the animation. The browser pauses painting for this long at most. Default 300 ms.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMilliseconds(300);
}

/// <summary>Scroll handling (<c>ModRouterOptions.Scroll</c>).</summary>
public sealed class ScrollOptions
{
    /// <summary>When true (the default), a new navigation scrolls to the top (or to its <c>#fragment</c>) and back/forward restore.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>CSS selector of the scrolling element; null (the default) is the window.</summary>
    public string? Container { get; set; }

    /// <summary>When true (the default), back and forward restore the position the page had.</summary>
    public bool Restore { get; set; } = true;
}
