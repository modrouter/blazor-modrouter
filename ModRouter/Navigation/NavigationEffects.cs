namespace ModRouter.Navigation;

/// <summary>How the page is scrolled after a navigation.</summary>
internal enum ScrollMode
{
    /// <summary>To the top.</summary>
    Top,

    /// <summary>To the element named by the URL fragment, once it exists.</summary>
    Fragment,

    /// <summary>To the position saved for the history entry (back and forward).</summary>
    Restore,
}

/// <summary>Focus the first element matching <paramref name="Selector"/>, waiting up to <paramref name="TimeoutMilliseconds"/> for it.</summary>
internal sealed record FocusEffect(string Selector, int TimeoutMilliseconds);

/// <summary>Scroll <paramref name="Container"/> (null: the window) according to <paramref name="Mode"/>.</summary>
internal sealed record ScrollEffect(ScrollMode Mode, string? Fragment, string? Container, int TimeoutMilliseconds);

/// <summary>
/// What the router does in the browser after a state committed and rendered: set the document title, announce the title,
/// move focus, scroll. Produced by <see cref="NavigationEffectsPlanner"/>; a null member means "do nothing".
/// </summary>
internal sealed record NavigationEffects(string? Title, string? Announcement, FocusEffect? Focus, ScrollEffect? Scroll)
{
    public static NavigationEffects None { get; } = new(null, null, null, null);

    internal bool IsEmpty => Title is null && Announcement is null && Focus is null && Scroll is null;
}
