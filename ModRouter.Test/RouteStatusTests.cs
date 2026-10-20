using Bunit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using ModRouter.Core.Definitions;
using ModRouter.Core.Guards;
using ModRouter.Core.Loading;
using ModRouter.Navigation;

namespace ModRouter.Test;

/// <summary>
/// What a committed state means for an HTTP response: the router reports it to an <see cref="IRouteStatusSink"/>, and the
/// ASP.NET Core sink turns it into a status code.
/// </summary>
[TestFixture]
public sealed class RouteStatusTests
{
    sealed class RecordingSink : IRouteStatusSink
    {
        public List<RouteStatusKind> Reports { get; } = [];

        public void Report(RouteStatusKind status) => Reports.Add(status);
    }

    sealed class DenyGuard : IRouteGuard
    {
        public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context) => ValueTask.FromResult(RouteGuardResult.NotAuthorized("no"));
    }

    sealed class ThrowingGuard : IRouteGuard
    {
        public ValueTask<RouteGuardResult> CanActivateAsync(RouteGuardContext context) => throw new InvalidOperationException("boom");
    }

    sealed class ThrowingLoader : IRouteLoader
    {
        public ValueTask<RouteLoadResult> LoadAsync(RouteLoadContext context) => throw new InvalidOperationException("boom");
    }

    static void Routes(RouteMap map) => map
        .Route("/", typeof(ShowProbe), name: "home")
        .Route("/items/{id:int}", typeof(ShowProbe), name: "item")
        .Route("/denied", typeof(ShowProbe), name: "denied").Guard<DenyGuard>()
        .Route("/guard-fails", typeof(ShowProbe), name: "guard-fails").Guard<ThrowingGuard>()
        .Route("/load-fails", typeof(ShowProbe), name: "load-fails").Loader<ThrowingLoader>();

    static async Task<IReadOnlyList<RouteStatusKind>> ReportsForAsync(string path)
    {
        await using var context = new BunitContext();
        var sink = new RecordingSink();

        context.Services.AddModRouter(Routes);
        context.Services.AddSingleton<IRouteStatusSink>(sink);
        context.Services.AddSingleton<INavigationPlatformAdapter>(new FakeNavigationAdapter("http://localhost/", "http://localhost" + path));

        context.Render<Components.ModRouter>();

        return [.. sink.Reports];
    }

    [Test]
    public async Task A_normal_page_reports_nothing()
        => Assert.That(await ReportsForAsync("/items/3"), Is.Empty);

    [TestCase("/nowhere")]
    [TestCase("/items/abc")]
    public async Task An_unmatched_url_reports_not_found(string path)
        => Assert.That(await ReportsForAsync(path), Is.EqualTo(new[] { RouteStatusKind.NotFound }));

    [Test]
    public async Task A_denied_level_reports_not_authorized()
        => Assert.That(await ReportsForAsync("/denied"), Is.EqualTo(new[] { RouteStatusKind.NotAuthorized }));

    [TestCase("/guard-fails")]
    [TestCase("/load-fails")]
    public async Task A_failing_guard_or_loader_reports_failed(string path)
        => Assert.That(await ReportsForAsync(path), Is.EqualTo(new[] { RouteStatusKind.Failed }));

    [Test]
    public async Task Without_a_sink_the_router_works_as_before()
    {
        await using var context = new BunitContext();

        context.Services.AddModRouter(Routes);
        context.Services.AddSingleton<INavigationPlatformAdapter>(new FakeNavigationAdapter("http://localhost/", "http://localhost/nowhere"));

        Assert.DoesNotThrow(() => context.Render<Components.ModRouter>());
    }

    // ---- the ASP.NET Core sink ------------------------------------------------------------------------------------------

    sealed class StartedResponse : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    [TestCase(RouteStatusKind.NotFound, false, StatusCodes.Status404NotFound)]
    [TestCase(RouteStatusKind.NotAuthorized, false, StatusCodes.Status401Unauthorized)]
    [TestCase(RouteStatusKind.NotAuthorized, true, StatusCodes.Status403Forbidden)]
    [TestCase(RouteStatusKind.Failed, true, StatusCodes.Status500InternalServerError)]
    public void The_http_sink_sets_the_status_of_the_response(RouteStatusKind status, bool authenticated, int expected)
    {
        var http = new DefaultHttpContext();

        if (authenticated)
        {
            http.User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity("test"));
        }

        new HttpContextRouteStatusSink(new HttpContextAccessor { HttpContext = http }).Report(status);

        Assert.That(http.Response.StatusCode, Is.EqualTo(expected));
    }

    [Test]
    public void The_http_sink_leaves_a_started_response_alone()
    {
        var http = new DefaultHttpContext();
        http.Features.Set<IHttpResponseFeature>(new StartedResponse { StatusCode = StatusCodes.Status200OK });

        new HttpContextRouteStatusSink(new HttpContextAccessor { HttpContext = http }).Report(RouteStatusKind.NotFound);

        Assert.That(http.Response.StatusCode, Is.EqualTo(StatusCodes.Status200OK));
    }

    [Test]
    public void The_http_sink_does_nothing_without_a_request()
        => Assert.DoesNotThrow(() => new HttpContextRouteStatusSink(new HttpContextAccessor()).Report(RouteStatusKind.NotFound));

    [Test]
    public void AddModRouterHttpStatus_registers_the_sink_once_and_the_accessor()
    {
        var services = new ServiceCollection();

        services.AddModRouterHttpStatus().AddModRouterHttpStatus();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.That(scope.ServiceProvider.GetRequiredService<IRouteStatusSink>(), Is.TypeOf<HttpContextRouteStatusSink>());
        Assert.That(services.Count(d => d.ServiceType == typeof(IRouteStatusSink)), Is.EqualTo(1));
    }
}
