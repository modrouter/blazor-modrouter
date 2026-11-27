using ModRouter.Crm.Ui;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ModRouter;
using ModRouter.Crm;
using ModRouter.Crm.Crm;
using ModRouter.Crm.Data;
using ModRouter.Crm.Guide;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The CRM's own state. A WebAssembly app has one scope, so singletons are the simplest fit.
builder.Services.AddSingleton<Session>();
builder.Services.AddSingleton<Network>();
builder.Services.AddSingleton<EditDraft>();
builder.Services.AddSingleton<Tour>();
builder.Services.AddSingleton<DialogService>();

// AddModRouter takes the route table first and the options second. A mistake in the table stops the app here, with a code and a message.
builder.Services.AddModRouter(
    routes => CrmRoutes.Declare(routes),
    options =>
    {
        options.Title.Format = "{0} - Northwind CRM";
        options.Title.Default = "Northwind CRM";
        options.Focus.Selector = "main h1";
        options.ViewTransitions.Enabled = false;
        options.Diagnostics.HistorySize = 40;          // the guide's route log reads this history
    });

await builder.Build().RunAsync();
