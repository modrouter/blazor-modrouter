using ModRouter.Navigation;
using ModRouter.Sample.Features;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ModRouter.Sample;

public partial class MainLayout : IDisposable
{
    static int instances;
    static readonly Guid OrderId = Guid.Parse("4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77");

    [Inject]
    INavigationService Navigation { get; set; } = default!;

    [Inject]
    SampleSession Session { get; set; } = default!;

    [Inject]
    IJSRuntime Js { get; set; } = default!;

    bool _useScroller;

    int Instance { get; } = Interlocked.Increment(ref instances);

    // Generated from [Route] and [RouteName] on CustomerDetailsPage by ModRouter.Generators: the parameters are the template's.
    string RouteUrl => Navigation.GetCustomerDetailsUrl(42);

    protected override void OnInitialized()
    {
        Session.Changed += OnSessionChanged;
        _useScroller = Js is IJSInProcessRuntime inProcess && inProcess.Invoke<bool>("sampleUsesScroller");
    }

    public void Dispose() => Session.Changed -= OnSessionChanged;

    void SignInAsUser() => Session.SignIn(admin: false);

    void SignInAsAdmin() => Session.SignIn(admin: true);

    void OnSessionChanged() => _ = InvokeAsync(StateHasChanged);

    void NavigateToCustomer43() => Navigation.NavigateTo("customers/43");

    // A rooted, base-prefixed URL ("/customers/43" at "/", "/myapp/customers/43" under "/myapp/").
    void NavigateToCustomer43Rooted() => Navigation.NavigateTo(Navigation.GetCustomerDetailsUrl(43));

    void NavigateToCustomer42ByName() => Navigation.NavigateToRoute("customer-details", new { id = 42 });

    void NavigateToOrderWithAmbientId() => Navigation.NavigateToRoute("customer-order-details", new { orderId = OrderId }, useAmbientValues: true);
}
