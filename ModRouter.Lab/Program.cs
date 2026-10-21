using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ModRouter;
using ModRouter.Lab;
using ModRouter.Lab.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");

// The lab's own state: a pretend security clearance (lesson 4), the knobs and the call ledger of the loaders (lesson 5)
// and the draft with its confirmation dialog (lesson 6). A WebAssembly app has one scope, so singletons are the simplest fit.
builder.Services.AddSingleton<Clearance>();
builder.Services.AddSingleton<LoaderKnobs>();
builder.Services.AddSingleton<LoaderLedger>();
builder.Services.AddSingleton<Draft>();
builder.Services.AddSingleton<ConfirmDialog>();
builder.Services.AddSingleton<LessonProgress>();

// AddModRouter takes the route table first and the options second. The table is compiled once at startup: a mistake in it
// (two routes that cannot be told apart, a name used twice) stops the app here with a code and a message.
builder.Services.AddModRouter(
    routes => LabRoutes.Declare(routes),
    options =>
    {
        options.Title.Format = "{0} - ModRouter Lab";
        options.Title.Default = "ModRouter Lab";
        options.Focus.Selector = "main h1";                       // where keyboard focus goes after a navigation
        options.ViewTransitions.Enabled = true;                   // cross-fade between pages (lesson 8)
        options.Diagnostics.HistorySize = 40;                     // keep the last 40 navigations for the flight recorder (lesson 9)
    });

await builder.Build().RunAsync();
