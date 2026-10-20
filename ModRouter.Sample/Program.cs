using ModRouter;
using ModRouter.Core.Definitions;
using ModRouter.Sample;
using ModRouter.Sample.Extra;
using ModRouter.Sample.Features;
using ModRouter.Sample.Pages;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.JSInterop;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.Services.AddSingleton<SampleSession>();

// The fake session doubles as the application's AuthenticationStateProvider, so the pages that carry [Authorize]
// (Pages/Compat) are enforced by the router's authorization guard in every mode, and a sign-in or
// sign-out re-runs it (CascadingAuthenticationState in compat mode sees it too).
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<AuthenticationStateProvider, SampleAuthenticationStateProvider>();

// The sample runs in one of five modes, chosen by the page URL before the host is built (?mode=..., read like
// ?scroller=1 through a tiny function in index.html): the default declares every route in the fluent map; "pages"
// discovers the @page components of the sample and of the Extra class library instead; "markup" declares the customers
// tree and the slice 3b pages in App markup (<Routes>); "markup-dup" makes that markup fail to compile (a duplicate name);
// "compat" is a real site's App.razor over the compatibility Router (the pages are discovered by the Router itself).
var mode = builder.Services.BuildServiceProvider().GetRequiredService<IJSRuntime>() is IJSInProcessRuntime early ? early.Invoke<string>("sampleMode") : string.Empty;
var markup = mode.StartsWith("markup", StringComparison.Ordinal);

switch (mode)
{
    case "markup":
        builder.RootComponents.Add<AppMarkup>("#app");
        break;
    case "markup-dup":
        builder.RootComponents.Add(typeof(AppMarkup), "#app", ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(AppMarkup.Duplicate)] = true }));
        break;
    case "compat":
        builder.RootComponents.Add<AppCompat>("#app");
        break;
    default:
        builder.RootComponents.Add<App>("#app");
        break;
}

builder.Services.AddModRouter(
    routes =>
    {
        if (mode == "compat")
        {
            // Only what has no page form. The redirect rule is left out as in markup mode: the compat Router discovers the
            // pages at its first navigation, so the startup compile cannot see a page's name (RT0017).
            FluentOnly(routes);

            return;
        }

        if (mode == "pages")
        {
            // Slice 4a: every page declares itself with @page and the Route* attributes; the Extra assembly is referenced
            // by nothing but found by reflection. Only what has no page form stays fluent.
            routes
            .AddPages(typeof(Program).Assembly)
            .AddPages(typeof(ExtraMarker).Assembly)
            .Redirect("/old/customers/{id:int}", "customer-details");
            FluentOnly(routes);

            return;
        }

        routes.Route("/", typeof(HomePage), name: "home", title: "Home");

        if (!markup)
        {
            routes
            .Route("/customers", typeof(CustomersPage), name: "customers", title: "Customers", children: customers => customers
                .Index(typeof(CustomerListPage), name: "customer-list")
                .Route("{id:int}", typeof(CustomerDetailsPage), p => $"Customer {p["id"]}", name: "customer-details", children: customer => customer
                    .Route("orders/{orderId:guid}", typeof(OrderPage), p => $"Order {p["orderId"]}", name: "customer-order-details")))

            // Slice 3b: title, focus, scroll and fragments.
            .Route("/long/{id:int}", typeof(LongPage), p => $"Long {p["id"]}", name: "long")
            .Route("/late", typeof(LatePage), name: "late", title: "Late")
            .Route("/anchors", typeof(AnchorsPage), name: "anchors", title: "Anchors")
            .Route("/no-heading", typeof(NoHeadingPage), name: "no-heading", title: "No heading")

            // A redirect's target must be in the startup table: in markup mode "customer-details" is declared in markup.
            .Redirect("/old/customers/{id:int}", "customer-details");
        }

        routes
        .Route("/vendors", typeof(VendorsPage), name: "vendors", aliases: ["/suppliers"], children: vendors => vendors
            .Index(typeof(VendorListPage), name: "vendor-list")
            .Route("{id:int}", typeof(VendorDetailsPage), name: "vendor-details").Breadcrumb(p => $"Vendor {p["id"]}"))
        .CanonicalAlias()
        .Breadcrumb("Vendors")
        .Route("/query", typeof(QueryPage), name: "query", title: "Query")

        // Slice 3a: guards, loaders, redirects and error states.
        .Route("/login", typeof(LoginPage), name: "login", title: "Sign in")
        .Route("/account", typeof(AccountPage), name: "account", title: "Account", children: account => account
            .Index(typeof(AccountHomePage), name: "account-home")
            .Route("admin", typeof(AdminPage), name: "account-admin", title: "Admin").Guard<AdminOnlyGuard>())
        .Guard<SignedInGuard>()
        .Route("/slow/{id:int}", typeof(SlowPage), name: "slow").Loader<SlowCustomerLoader>()
        .Route("/flaky", typeof(FlakyPage), name: "flaky", children: flaky => flaky
            .Route("{id:int}", typeof(FlakyDetailsPage), name: "flaky-details").Loader<FlakyLoader>());
        FluentOnly(routes);
    },
    options =>
    {
        options.Title.Format = "{0} - Router sample";
        options.Title.Default = "Router sample";
        options.Focus.Selector = "#body h1, #body h2";
        options.Focus.Timeout = TimeSpan.FromMilliseconds(1500);
    });

// Three routes that reuse the home component with a guard each: a component has one set of attributes, so these stay fluent in every mode.
static void FluentOnly(RouteMap routes)
    => routes
    .Route("/loop-a", typeof(HomePage), name: "loop-a").Guard<LoopAGuard>()
    .Route("/loop-b", typeof(HomePage), name: "loop-b").Guard<LoopBGuard>()
    .Route("/blocked", typeof(HomePage), name: "blocked").Guard<BlockedGuard>();

var host = builder.Build();

// ?scroller=1 puts the content in a scrolling container, and the router is told to manage that container's scroll.
if (host.Services.GetRequiredService<IJSRuntime>() is IJSInProcessRuntime js && js.Invoke<bool>("sampleUsesScroller"))
{
    host.Services.GetRequiredService<ModRouterOptions>().Scroll.Container = "#scroller";
}

await host.RunAsync();
