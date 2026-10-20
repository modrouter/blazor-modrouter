using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Navigation;
using Microsoft.Extensions.Logging;

namespace ModRouter.Test;

/// <summary>
/// Under the router-wide <see cref="AliasBehavior.RedirectToCanonical"/> an alias redirects only when its values can build
/// the canonical URL; otherwise it renders where it matched. A route that declares <c>CanonicalAlias()</c> keeps redirecting.
/// </summary>
[TestFixture]
public sealed class AliasInPlaceTests
{
    static Task<Pipeline> StartAsync(CompiledRouteTable table, Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory = null)
        => StartAsync(table, loggerFactory, options => options.AliasBehavior = AliasBehavior.RedirectToCanonical);

    static async Task<Pipeline> StartAsync(CompiledRouteTable table, Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory, Action<ModRouterOptions> configure)
    {
        var pipeline = Pipeline.Create(table, configure: configure, loggerFactory: loggerFactory);
        await pipeline.Coordinator.NavigateInitialAsync();

        return pipeline;
    }

    static CompiledRouteTable PeopleTable() => RouteTestKit.Compile(map => map
        .Route("/", typeof(HomePage), name: "home")
        .Route("/people/{id:int}", typeof(OtherPage), name: "person", aliases: ["/people"]));

    [Test]
    public async Task A_generated_alias_url_renders_in_place_under_the_router_wide_redirect_option()
    {
        var table = PeopleTable();
        var url = table.GenerateUrl("person");
        Assert.That(url, Is.EqualTo("/people"), "generation falls back to the alias");
        using var p = await StartAsync(table);

        await p.GoAsync(url.TrimStart('/'));

        Assert.That(p.Current.Path, Is.EqualTo("/people"));
        Assert.That(p.Current.Chain!.MatchedViaAlias, Is.True);
        Assert.That(p.Current.Failure, Is.EqualTo(RouteFailure.None));
    }

    [Test]
    public async Task An_alias_with_more_parameters_renders_in_place()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/", typeof(HomePage), name: "home")
            .Route("/org-chart", typeof(OtherPage), name: "org", aliases: ["/org-chart/{employeeId:int}"]));
        var url = table.GenerateUrl("org", new Dictionary<string, object?> { ["employeeId"] = 5 });
        using var p = await StartAsync(table);

        await p.GoAsync(url.TrimStart('/'));

        Assert.That(p.Current.Path, Is.EqualTo("/org-chart/5"));
        Assert.That(p.Current.Chain!.MatchedViaAlias, Is.True);
    }

    [Test]
    public async Task A_typed_alias_url_that_the_canonical_template_rejects_renders_in_place()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/", typeof(HomePage), name: "home")
            .Route("/people/{id:int}", typeof(OtherPage), name: "person", aliases: ["/people/{id:alpha}"]));
        using var p = await StartAsync(table);

        await p.GoAsync("people/abc");

        Assert.That(p.Current.Path, Is.EqualTo("/people/abc"), "the alias accepted a value the canonical constraint rejects");
        Assert.That(p.Current.Chain!.MatchedViaAlias, Is.True);
    }

    [Test]
    public async Task A_same_parameter_alias_still_redirects_under_the_option()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/", typeof(HomePage), name: "home")
            .Route("/customers/{id:int}", typeof(CustomerDetails), name: "customer", aliases: ["/clients/{id:int}"]));
        using var p = await StartAsync(table);

        await p.GoAsync("clients/7?x=1");

        Assert.That(p.Current.Path, Is.EqualTo("/customers/7"));
        Assert.That(p.Current.Query, Is.EqualTo("?x=1"));
        Assert.That(p.Current.Chain!.MatchedViaAlias, Is.False);
        Assert.That(p.Current.Trigger, Is.EqualTo(NavigationTrigger.Redirect));
    }

    [Test]
    public async Task A_generated_same_parameter_alias_url_redirects_under_the_option()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/", typeof(HomePage), name: "home")
            .Route("/customers/{id:int}", typeof(CustomerDetails), name: "customer", aliases: ["/clients/{id:int}"]));
        using var p = await StartAsync(table);

        await p.GoAsync("clients/7");

        Assert.That(p.Current.Path, Is.EqualTo("/customers/7"));
        Assert.That(p.Adapter.History, Is.EqualTo(new[] { "http://localhost/", "http://localhost/customers/7" }));
    }

    [Test]
    public async Task A_declared_CanonicalAlias_is_unaffected_and_still_redirects()
    {
        var table = RouteTestKit.Compile(map =>
        {
            map.Route("/", typeof(HomePage), name: "home");
            map.Route("/customers/{id:int}", typeof(CustomerDetails), name: "customer", aliases: ["/clients/{id:int}"]);
            map.CanonicalAlias();
        });

        // Declared, so it redirects with or without the router-wide option.
        foreach (var option in new[] { AliasBehavior.Render,AliasBehavior.RedirectToCanonical })
        {
            using var p = await StartAsync(table, null, options => options.AliasBehavior = option);

            await p.GoAsync("clients/7");

            Assert.That(p.Current.Path, Is.EqualTo("/customers/7"), option.ToString());
        }
    }

    [Test]
    public async Task A_declared_CanonicalAlias_keeps_its_failure_for_a_value_the_canonical_template_rejects()
    {
        // RT0015 compares parameter names only, so a constraint mismatch can still reach the redirect stage of a declared route.
        var table = RouteTestKit.Compile(map =>
        {
            map.Route("/", typeof(HomePage), name: "home");
            map.Route("/people/{id:int}", typeof(OtherPage), name: "person", aliases: ["/people/{id:alpha}"]);
            map.CanonicalAlias();
        });
        using var capture = new LogCapture();
        using var p = await StartAsync(table, capture);

        var allowed = await p.GoAsync("people/abc");

        Assert.That(allowed, Is.False);
        Assert.That(p.Current.Path, Is.EqualTo("/"), "the navigation was not rendered in place");
        Assert.That(capture.WithId(1004), Has.Count.EqualTo(1), "NavigationFailed: the declared behavior still throws");
        Assert.That(capture.WithId(1102), Is.Empty);
    }

    [Test]
    public async Task Rendering_an_alias_in_place_logs_a_debug_event_with_the_path_and_the_canonical_template()
    {
        using var capture = new LogCapture();
        using var p = await StartAsync(PeopleTable(), capture);

        await p.GoAsync("people");

        var entry = capture.WithId(1102).Single();
        Assert.That(entry.Level, Is.EqualTo(LogLevel.Debug));
        Assert.That(entry.Category, Is.EqualTo(typeof(RedirectStage).FullName));
        Assert.That(entry["Path"], Is.EqualTo("/people"));
        Assert.That(entry["Template"], Is.EqualTo("/people/{id:int}"));
        Assert.That(entry.Message, Is.EqualTo("The alias /people cannot build the canonical URL /people/{id:int}; it renders in place."));
    }

    [Test]
    public async Task A_redirecting_alias_does_not_log_the_in_place_event()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/", typeof(HomePage), name: "home")
            .Route("/customers/{id:int}", typeof(CustomerDetails), name: "customer", aliases: ["/clients/{id:int}"]));
        using var capture = new LogCapture();
        using var p = await StartAsync(table, capture);

        await p.GoAsync("clients/7");

        Assert.That(capture.WithId(1102), Is.Empty);
    }
}
