namespace ModRouter.Crm.Ui;

/// <summary>The icon names <see cref="UiIcon"/> knows. Each is a 24 by 24 stroke drawing, kept here so the app needs no icon package.</summary>
public static class Icons
{
    /// <summary>Outlined icons.</summary>
    public static class Outlined
    {
        public const string Home = "home";
        public const string Building = "building";
        public const string People = "people";
        public const string Person = "person";
        public const string Settings = "settings";
        public const string Search = "search";
        public const string Edit = "edit";
        public const string ChevronRight = "chevronright";
        public const string ArrowLeft = "arrowleft";
        public const string ArrowClockwise = "arrowclockwise";
        public const string Trophy = "trophy";
        public const string Info = "info";
        public const string LockClosed = "lockclosed";
        public const string ArrowTrending = "arrowtrending";
    }

    static readonly Dictionary<string, string> Paths = new()
    {
        [Outlined.Home] = "M3 11l9-8 9 8M5 10v10h5v-6h4v6h5V10",
        [Outlined.Building] = "M4 21V4a1 1 0 011-1h9a1 1 0 011 1v17M15 9h4a1 1 0 011 1v11M3 21h18M8 7h3M8 11h3M8 15h3",
        [Outlined.People] = "M9 11a3.5 3.5 0 100-7 3.5 3.5 0 000 7zM2.5 20c0-3.3 2.9-5.5 6.5-5.5s6.5 2.2 6.5 5.5M16 4.3a3.5 3.5 0 010 6.8M18 14.7c2.2.6 3.5 2.4 3.5 5.3",
        [Outlined.Person] = "M12 12a4 4 0 100-8 4 4 0 000 8zM4.5 21c0-4 3.4-6.5 7.5-6.5s7.5 2.5 7.5 6.5",
        [Outlined.Settings] = "M4 7h10M18 7h2M4 17h2M10 17h10M16 4v6M8 14v6",
        [Outlined.Search] = "M11 18a7 7 0 100-14 7 7 0 000 14zM20 20l-4-4",
        [Outlined.Edit] = "M4 20l1-4L16.5 4.5a2.1 2.1 0 013 3L8 19l-4 1z",
        [Outlined.ChevronRight] = "M9 5l7 7-7 7",
        [Outlined.ArrowLeft] = "M19 12H5M11 6l-6 6 6 6",
        [Outlined.ArrowClockwise] = "M20 11a8 8 0 10-2.3 6M20 4v7h-7",
        [Outlined.Trophy] = "M8 4h8v5a4 4 0 01-8 0V4zM8 6H4v1a4 4 0 004 4M16 6h4v1a4 4 0 01-4 4M12 13v4M8 21h8M10 17h4",
        [Outlined.Info] = "M12 21a9 9 0 100-18 9 9 0 000 18zM12 11v5M12 8v.01",
        [Outlined.LockClosed] = "M6 11h12v9H6zM8 11V8a4 4 0 018 0v3",
        [Outlined.ArrowTrending] = "M3 17l6-6 4 4 8-8M15 7h6v6",
    };

    /// <summary>The path data of an icon, or an empty string for a name that is not known.</summary>
    internal static string PathOf(string? name) => name is not null && Paths.TryGetValue(name, out var path) ? path : string.Empty;
}
