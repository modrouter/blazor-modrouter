namespace ModRouter.Lab;

/// <summary>One stop on the lesson line: what the lesson is about, in plain words, and where it starts.</summary>
public sealed record Lesson(int Number, string RouteName, string Title, string Promise);

/// <summary>The nine lessons in the order they are meant to be followed.</summary>
public static class LessonCatalog
{
    public static IReadOnlyList<Lesson> All { get; } =
    [
        new(1, "l1", "Pages inside pages", "Nest routes so a ship page can live inside a fleet page, and see which parts of the screen survive a click."),
        new(2, "l2", "Names instead of URLs", "Give routes names, build their URLs from values, and let the compiler check the call."),
        new(3, "l3", "Which route wins?", "Type any address and watch every candidate route get ranked. The router never guesses."),
        new(4, "l4", "Gates", "Stop, redirect or refuse a navigation before the page appears, and find out how it ended."),
        new(5, "l5", "Data in flight", "Load data before the page shows, stream the slow parts, and cache the result."),
        new(6, "l6", "Are you sure?", "Ask before the user leaves a page with unsaved work."),
        new(7, "l7", "Shared layouts and side panels", "Share one layout between routes with different paths, and fill a side panel from the route."),
        new(8, "l8", "Pages that stay alive", "Keep a page and its state while the user is elsewhere, and cross-fade between pages."),
        new(9, "l9", "The flight recorder", "Read back every navigation: which stage ran, how long it took, how it ended."),
    ];

    public static Lesson? ByNumber(int number) => All.FirstOrDefault(l => l.Number == number);
}
