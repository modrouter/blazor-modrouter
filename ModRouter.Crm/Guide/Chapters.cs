using ModRouter.Crm.Data;

namespace ModRouter.Crm.Guide;

/// <summary>One thing to try in a chapter: open an address, or change who is signed in.</summary>
public sealed record Step(string Label, string? Url = null, Role? SignIn = null);

/// <summary>One idea of the router, with what to read, what to try and the code that makes it work.</summary>
public sealed record Chapter(int Number, string Title, string Hook, string[] Paragraphs, Step[] Steps, string Code, string Watch, string? From = null)
{
    /// <summary>Where the CRM goes when the chapter opens: the chapter's own start, or the first address it asks you to try.</summary>
    public string Start => From ?? Steps.FirstOrDefault(s => s.Url is not null)?.Url ?? "/";
}

/// <summary>The eight chapters, in order. Each adds one idea and unlocks one more part of the CRM.</summary>
public static class Chapters
{
    public static readonly IReadOnlyList<Chapter> All =
    [
        new(1, "A URL is a screen", "Every screen has an address.",
            [
                "ModRouter replaces Blazor's built-in router. You declare a route table: a template, the component it shows, and a name.",
                "Click around the CRM. Each click is a navigation: the router reads the address, finds the route and shows its component.",
            ],
            [new("Dashboard", "/"), new("Accounts", "/accounts"), new("Contacts", "/contacts")],
            """
            routes.Route("accounts", typeof(AccountsPage),
                name: "accounts", title: "Accounts");
            """,
            "The log shows the route that matched and the stages the navigation passed."),

        new(2, "Parameters", "Part of the address is data.",
            [
                "A template like accounts/{id:int} has a hole. The router fills a parameter from the address and converts it to an int before your page runs.",
                "Type /accounts/abc in the address bar. It is not an int, so no route matches and the router shows the not-found page.",
            ],
            [new("Open account 3", "/accounts/3"), new("Open contact 7", "/contacts/7"), new("Try /accounts/abc", "/accounts/abc")],
            """
            .Route("accounts/{id:int}", typeof(AccountPage), name: "account")

            // AccountPage.razor.cs
            [Parameter] public int Id { get; set; }

            // a link by name
            <RouteLink Route="account" Parameters="@(new { id = 3 })">Open</RouteLink>
            """,
            "The log lists the parameter and its value."),

        new(3, "Nested routes", "A page inside a page.",
            [
                "The tabs on an account are child routes. The account page stays on screen and only the tab changes.",
                "A ModRouterOutlet marks where the child renders. The index route is the child shown when nothing more is in the address.",
            ],
            [new("Account 3, overview", "/accounts/3"), new("Contacts tab", "/accounts/3/contacts"), new("Activity tab", "/accounts/3/activity")],
            """
            .Route("accounts/{id:int}", typeof(AccountPage), children: account => account
                .Index(typeof(AccountOverview))
                .Route("contacts", typeof(AccountContactsTab))
                .Route("activity", typeof(AccountActivityTab)))

            // AccountPage.razor
            <ModRouterOutlet />
            """,
            "The log shows one level per route: account, then the tab."),

        new(4, "The query string", "State you can share.",
            [
                "Filters belong in the address, so a link restores them and Back undoes them. The accounts list reads q, stage and sort from the query string.",
                "Change a filter, then reload the frame: the list is exactly as you left it.",
            ],
            [new("Won accounts", "/accounts?stage=Won"), new("Search for “wind”", "/accounts?q=wind"), new("Sort by revenue", "/accounts?sort=revenue")],
            """
            [SupplyParameterFromQuery] public string? Q { get; set; }
            [SupplyParameterFromQuery] public string? Stage { get; set; }

            Navigation.NavigateTo(
                Navigation.GetUriWithQueryParameter("stage", "Won"));
            """,
            "The log shows the query next to the path."),

        new(5, "Guards", "Who may enter?",
            [
                "A guard runs before a page appears. It can allow, cancel, redirect or say not authorized. Settings has a sign-in guard; Billing adds an admin guard.",
                "Sign out, then open Settings. The guard sends you to the sign-in page and remembers where you were going.",
            ],
            [new("Sign out", "/", Role.SignedOut), new("Open settings", "/settings"), new("Sign in as a sales rep", SignIn: Role.Rep), new("Open billing", "/settings/billing"), new("Sign in as an admin", SignIn: Role.Admin)],
            """
            .Route("settings", typeof(SettingsPage), children: s => s
                .Index(typeof(SettingsGeneral))
                .Route("billing", typeof(SettingsBilling)).Guard<AdminOnlyGuard>())
                .Guard<SignedInGuard>()

            RouteGuardResult.RedirectToRoute("sign-in", query: ...);
            RouteGuardResult.NotAuthorized("Admins only.");
            """,
            "A redirect shows as two navigations. A denied page commits with a not-allowed level.",
            From: "/"),

        new(6, "Loaders", "Fetch before you show.",
            [
                "A loader gets a page's data before the page appears. The old page stays on screen until it arrives, so the CRM never shows half a page.",
                "Slow the network down and open an account: watch the old page wait. Go offline and the loader fails into an error with a Retry. The Activity tab has its own loader that defers the feed: the tab shows first and the feed fills in. A deferred result is never cached.",
            ],
            [new("Open account 1", "/accounts/1"), new("Open account 4", "/accounts/4"), new("Open account 99 (no such account)", "/accounts/99")],
            """
            .Route("accounts/{id:int}", typeof(AccountPage), ...)
                .Loader<AccountLoader>()
                .CacheFor(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30))

            // the Activity tab's own loader
            return RouteLoadResult.Create(id)
                .Defer("activity", LoadActivityAsync(id));
            """,
            "The loader stage shows its time. Revisit an account within ten seconds: no loader call."),

        new(7, "Leave guards", "Do not lose work.",
            [
                "A leave guard is asked before a page that is on screen is left. The edit page asks when something is unsaved.",
                "Change the name, then click any link. Choose Keep editing: the page, the URL and your text stay.",
            ],
            [new("Edit account 3", "/accounts/3/edit"), new("Edit account 5", "/accounts/5/edit")],
            """
            .Route("edit", typeof(AccountEdit)).LeaveGuard<UnsavedChangesGuard>()

            return leave ? RouteLeaveResult.Allow()
                         : RouteLeaveResult.Cancel();
            """,
            "A canceled navigation shows in the log as canceled, with no new page."),

        new(8, "Names and redirects", "Addresses change; names do not.",
            [
                "Name each route and link by name. If an address changes later, no link breaks.",
                "A redirect rule forwards an old address to a named route, and its parameter follows. The breadcrumb reads the route chain, so it is never out of date.",
            ],
            [new("Old address /clients/3", "/clients/3"), new("Account 3, activity", "/accounts/3/activity"), new("Open a page that does not exist", "/nowhere")],
            """
            .Redirect("clients/{id:int}", "account")

            <RouteBreadcrumb Home="Dashboard" />

            Navigation.NavigateToRouteAsync("account", new { id = 3 });
            """,
            "A redirect is recorded with where it came from."),
    ];
}
