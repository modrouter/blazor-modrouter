using ModRouter.Core.Definitions;
using ModRouter.Crm.Crm.Pages;
using ModRouter.Crm.Crm.Routing;
using ModRouter.Crm.Data;
using ModRouter.Crm.Shell;

namespace ModRouter.Crm.Crm;

/// <summary>
/// The whole route table of the CRM. Each chapter of the guide adds one idea to it; the table is compiled once at startup.
/// </summary>
public static class CrmRoutes
{
    public static void Declare(RouteMap routes)
    {
        // Chapter 5: the sign-in page lives outside the CRM layout, so it shows no navigation.
        routes.Route("/sign-in", typeof(SignInPage), name: "sign-in", title: "Sign in");

        // A group adds a layout without adding to the address: everything inside shows in the CRM chrome.
        routes.Group(typeof(CrmLayout), crm => crm
            // Chapter 1: a route is a template and the component that renders it.
            .Index(typeof(DashboardPage), name: "home")
            // Lists and their detail pages nest, so the breadcrumb reads Home > Contacts > Robin Calloway.
            .Route("contacts", typeof(Section), name: "contacts", title: "Contacts", children: contacts => contacts
                .Index(typeof(ContactsPage), name: "contacts-list")
                // Chapter 2: {id:int} is a parameter. It reaches the page converted to an int; "abc" matches nothing.
                .Route("{id:int}", typeof(ContactPage), p => int.TryParse($"{p["id"]}", out var id) && CrmData.Contact(id) is { } contact ? contact.Name : $"Contact {p["id"]}", name: "contact"))

            // Chapter 3: children nest. Each child renders where its parent places a ModRouterOutlet.
            // Chapter 6: the loader runs before any of this appears; CacheFor keeps the answer for a while.
            .Route("accounts", typeof(Section), name: "accounts", title: "Accounts", children: accounts => accounts
                .Index(typeof(AccountsPage), name: "accounts-list")
                .Route("{id:int}", typeof(AccountPage), p => int.TryParse($"{p["id"]}", out var id) && CrmData.Account(id) is { } account ? account.Name : $"Account {p["id"]}", name: "account", children: account => account
                    .Index(typeof(AccountOverview), name: "account-overview")
                    .Route("contacts", typeof(AccountContactsTab), name: "account-contacts", title: "Contacts")
                    .Route("activity", typeof(AccountActivityTab), name: "account-activity", title: "Activity").Loader<ActivityLoader>()
                    // Chapter 7: the leave guard asks before the edit page is left with unsaved changes.
                    .Route("edit", typeof(AccountEdit), name: "account-edit", title: "Edit").LeaveGuard<UnsavedChangesGuard>())
                    .Loader<AccountLoader>()
                    .CacheFor(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)))

            // Chapter 5: a guard on the parent covers every child. The billing child adds its own, stricter guard.
            .Route("settings", typeof(SettingsPage), name: "settings", title: "Settings", children: settings => settings
                .Index(typeof(SettingsGeneral), name: "settings-general")
                .Route("billing", typeof(SettingsBilling), name: "settings-billing", title: "Billing").Guard<AdminOnlyGuard>())
                .Guard<SignedInGuard>()

            // Chapter 8: an old URL forwards to a named route. Its {id} follows it.
            .Redirect("clients/{id:int}", "account"));
    }
}
