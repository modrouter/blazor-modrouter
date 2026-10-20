using Bunit;
using ModRouter.Components;
using ModRouter.Core.Definitions;
using ModRouter.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using static ModRouter.Test.ComponentTestKit;

namespace ModRouter.Test;

/// <summary>
/// How <c>ModRouter</c> treats its JavaScript module handle when the import is slow: one import however many states
/// render meanwhile, effects only for the state that is still current once the module arrives, and no handle leaked or
/// used after the router is disposed. The module is a hand-rolled fake whose import and calls can be held back.
/// </summary>
[TestFixture]
public sealed class RouterModuleLifecycleTests
{
    BunitContext _ctx = default!;
    FakeNavigationAdapter _adapter = default!;
    GatedJSRuntime _js = default!;

    [SetUp]
    public void SetUp()
    {
        Probes.Reset();
        _ctx = new BunitContext();
        _js = new GatedJSRuntime();
        _ctx.Services.AddSingleton<IJSRuntime>(_js);
        _ctx.Services.AddSingleton(new GuardScript());
    }

    [TearDown]
    public async Task TearDown()
    {
        Assert.That(_ctx.Renderer.UnhandledException.IsCompleted, Is.False, () => "a render exception was swallowed: " + _ctx.Renderer.UnhandledException.Result);

        await _ctx.DisposeAsync();
    }

    static void Routes(RouteMap map) => map
        .Route("/", typeof(HomeProbe), name: "home", title: "Home")
        .Route("/about", typeof(ShowProbe), name: "about", title: "About");

    IRenderedComponent<Components.ModRouter> Mount()
    {
        _ctx.Services.AddModRouter(Routes);
        _adapter = new FakeNavigationAdapter("http://localhost/", "http://localhost/") { ExecuteNavigateTo = true };
        _ctx.Services.AddSingleton<INavigationPlatformAdapter>(_adapter);

        return _ctx.Render<Components.ModRouter>(p => p.Add(r => r.DefaultLayout, typeof(MainLayoutProbe)));
    }

    async Task GoAsync(string uri)
    {
        await _adapter.NavigateAsync(uri, intercepted: true);
        await _adapter.SettledAsync();
    }

    IEnumerable<string> Titles => _js.Module.Calls.Where(call => call.Identifier == "afterNavigation").Select(call => Title(call.Arguments[0]));

    static string Title(object? effects) => (string?)effects!.GetType().GetProperty("title")!.GetValue(effects) ?? "(none)";

    [Test]
    public async Task The_module_is_imported_once_however_many_states_render_before_it_arrives()
    {
        Mount();
        await GoAsync("about");
        Assert.That(_js.Imports, Is.EqualTo(1), "a second state must wait for the import that is already in flight");

        _js.Import.SetResult(_js.Module);
        await EventuallyAsync(() => Titles.Count(), Is.GreaterThanOrEqualTo(1));

        Assert.That(_js.Imports, Is.EqualTo(1));
        Assert.That(Titles, Is.EqualTo(new[] { "About" }), "the state that was superseded while the module loaded runs no effects");
    }

    [Test]
    public async Task A_router_disposed_while_the_import_is_in_flight_disposes_the_handle_when_it_arrives()
    {
        Mount();
        await _ctx.DisposeComponentsAsync();
        Assert.That(_js.Module.Disposed, Is.False, "nothing to dispose yet: the import has not finished");

        _js.Import.SetResult(_js.Module);

        await EventuallyAsync(() => _js.Module.Disposed, Is.True);
        Assert.That(_js.Module.Calls, Is.Empty, "no call is made on a handle that arrived after the router went away");
    }

    [Test]
    public async Task A_router_disposed_while_its_effects_run_makes_no_further_call_on_the_disposed_handle()
    {
        _js.Import.SetResult(_js.Module);
        _js.Module.Gate = new TaskCompletionSource();
        Mount();
        await EventuallyAsync(() => _js.Module.Calls.Count, Is.EqualTo(1));
        Assert.That(_js.Module.Calls[0].Identifier, Is.EqualTo("trackScroll"));

        await _ctx.DisposeComponentsAsync();
        _js.Module.Gate.SetResult();
        await Task.Delay(50);

        Assert.That(_js.Module.Disposed, Is.True);
        Assert.That(_js.Module.Calls.Select(call => call.Identifier), Is.EqualTo(new[] { "trackScroll" }));
    }

    [Test]
    public async Task A_loaded_module_is_disposed_with_the_router()
    {
        _js.Import.SetResult(_js.Module);
        Mount();
        await EventuallyAsync(() => Titles.Count(), Is.EqualTo(1));

        await _ctx.DisposeComponentsAsync();

        Assert.That(_js.Module.Disposed, Is.True);
    }

    /// <summary>An <see cref="IJSRuntime"/> that only serves the module import, held back until <see cref="Import"/> is completed.</summary>
    sealed class GatedJSRuntime : IJSRuntime
    {
        public TaskCompletionSource<IJSObjectReference> Import { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeModule Module { get; } = new();

        public int Imports { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Assert.That(identifier, Is.EqualTo("import"), "the router only imports its module through the runtime");
            Imports++;

            return (TValue)await Import.Task;
        }
    }

    /// <summary>
    /// A module handle that records every call (including an attempt on a disposed handle, which throws like the real
    /// one) and can hold each call back until <see cref="Gate"/> is completed.
    /// </summary>
    sealed class FakeModule : IJSObjectReference
    {
        public List<(string Identifier, object?[] Arguments)> Calls { get; } = [];

        public bool Disposed { get; set; }

        public TaskCompletionSource? Gate { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            ObjectDisposedException.ThrowIf(Disposed, this);

            if (Gate is { } gate)
            {
                await gate.Task;
            }

            return default!;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;

            return ValueTask.CompletedTask;
        }
    }
}
