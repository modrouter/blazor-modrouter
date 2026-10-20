using ModRouter.Core.Compilation;
using ModRouter.Core.Loading;
using ModRouter.Navigation;

namespace ModRouter.Test;

/// <summary>
/// Holds a navigation of one trigger (and, when given, one URI) in the pipeline until its gate is released, so a test
/// decides what happens while that navigation is still preparing. It counts the runs that reached it.
/// </summary>
internal sealed class HoldStage(NavigationTrigger trigger, string? uriEnd = null) : INavigationStage
{
    public TaskCompletionSource Gate { get; } = new();

    public int Entered { get; private set; }

    public async ValueTask<StageResult> RunAsync(RouteNavigationContext context)
    {
        if (context.Trigger != trigger || (uriEnd is not null && !context.To.EndsWith(uriEnd, StringComparison.Ordinal)))
        {
            return StageResult.Continue;
        }

        Entered++;
        await Gate.Task.WaitAsync(context.CancellationToken);

        return StageResult.Continue;
    }
}

/// <summary>
/// The navigation whose location has changed and whose <c>OnNavigate</c> hook is running (or returned while a newer
/// navigation was still preparing): when it commits, what it keeps and discards, and what leaves it alone. Every
/// test holds a gate the test releases, so nothing depends on timing.
/// </summary>
[TestFixture]
public sealed class ArrivedNavigationTests
{
    const string Customers1 = "http://localhost/customers/1";

    static CompiledRouteTable LoaderTable() => Pipeline.Table(map => map
        .Route("/customers", typeof(CustomersPage), name: "customers", children: customers => customers
            .Index(typeof(CustomerList), name: "customer-list")
            .Route("{id:int}", typeof(CustomerDetails), name: "customer", children: customer => customer
                .Route("orders", typeof(OrderDetails), name: "orders").Loader<OrdersLoader>())
            .Loader<CustomerLoader>())
        .Loader<CustomersLoader>());

    sealed record Call(string Uri, CancellationToken Token);

    readonly Dictionary<string, TaskCompletionSource> _gates = [];
    readonly List<Call> _calls = [];
    readonly List<string> _returned = [];
    Pipeline _p = default!;

    [SetUp]
    public void SetUp()
    {
        _gates.Clear();
        _calls.Clear();
        _returned.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var gate in _gates.Values)
        {
            gate.TrySetResult();
        }

        _p?.Dispose();
    }

    /// <summary>A pipeline over the loader table whose hook holds every navigation until the test releases that target's gate.</summary>
    async Task StartAsync(params INavigationStage[] stages)
    {
        _p = Pipeline.Create(LoaderTable(), stages: stages);
        _p.Coordinator.OnNavigate = async (target, token) =>
        {
            _calls.Add(new Call(target, token));
            var gate = _gates[target] = new TaskCompletionSource();
            await gate.Task.WaitAsync(token);
            _returned.Add(target);
        };
        await _p.Coordinator.NavigateInitialAsync();
    }

    IEnumerable<(string Path, NavigationTrigger Trigger)> Committed => _p.Commits.Select(c => (c.Path, c.Trigger));

    /// <summary>Releases the hook of <paramref name="target"/> and waits until it has returned to the coordinator.</summary>
    async Task ReleaseHookAsync(string target)
    {
        var returned = _returned.Count;
        _gates[target].SetResult();
        await ComponentTestKit.EventuallyAsync(() => _returned.Count, Is.EqualTo(returned + 1));
    }

    [Test]
    public async Task A_hook_that_returns_while_a_newer_navigation_prepares_waits_for_that_navigation_to_end_and_then_commits()
    {
        var hold = new HoldStage(NavigationTrigger.Link, "/customers/2");
        await StartAsync(hold);
        Assert.That(await _p.Adapter.NavigateAsync("customers/1", intercepted: true), Is.True);

        using var cancellation = new CancellationTokenSource();
        var newer = _p.Adapter.NavigateAsync("customers/2", intercepted: true, cancellationToken: cancellation.Token);
        Assert.That(hold.Entered, Is.EqualTo(1), "the newer navigation is preparing");

        await ReleaseHookAsync(Customers1);

        Assert.That(_p.Commits, Has.Count.EqualTo(1), "the returned navigation does not commit under a navigation that may still replace it");
        Assert.That(_p.Store.IsNavigating, Is.True);

        cancellation.Cancel();

        Assert.That(await newer, Is.False, "the newer navigation was canceled");
        Assert.That(Committed, Is.EqualTo(new[] { ("/", NavigationTrigger.InitialLoad), ("/customers/1", NavigationTrigger.Link) }), "its end commits the page of the location the platform shows");
        Assert.That(_p.Store.IsNavigating, Is.False);
    }

    [Test]
    public async Task A_navigation_waiting_on_its_hook_discards_what_its_loaders_produced_when_the_location_moves_away()
    {
        CancellationToken lifetime = default;
        await StartAsync();
        _p.Loaders.Set("customer", context =>
        {
            lifetime = context.LifetimeToken;

            return ValueTask.FromResult(RouteLoadResult.Create("customer one"));
        });
        Assert.That(await _p.Adapter.NavigateAsync("customers/1", intercepted: true), Is.True);
        Assert.That(_p.Loaders.Calls, Does.Contain("customer@customer"));
        Assert.That(lifetime.IsCancellationRequested, Is.False, "the navigation still waits on its hook");

        Assert.That(await _p.Adapter.NavigateAsync("login", intercepted: true), Is.True);

        Assert.That(lifetime.IsCancellationRequested, Is.True, "the location moved away: the data it loaded is discarded");
        Assert.That(_calls[0].Token.IsCancellationRequested, Is.True, "and its hook is canceled");
        Assert.That(_p.Commits, Has.Count.EqualTo(1), "it never committed");
    }

    [Test]
    public async Task A_fragment_change_on_the_location_whose_hook_is_running_leaves_that_navigation_alone()
    {
        await StartAsync();
        Assert.That(await _p.Adapter.NavigateAsync("customers/1", intercepted: true), Is.True);

        // The platform moved within the page (a hash link): no handler ran for it.
        _p.Adapter.RaiseLocationChanged("customers/1#top", intercepted: true);

        Assert.That(_calls, Has.Count.EqualTo(1), "no second hook, no navigation after the fact");
        Assert.That(_calls[0].Token.IsCancellationRequested, Is.False, "the hook still runs");
        Assert.That(_p.Store.IsNavigating, Is.True);

        await ReleaseHookAsync(Customers1);

        Assert.That(Committed, Is.EqualTo(new[] { ("/", NavigationTrigger.InitialLoad), ("/customers/1", NavigationTrigger.Link) }), "one commit for the navigation");
        Assert.That(_p.Store.IsNavigating, Is.False);
    }
}
