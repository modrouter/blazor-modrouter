using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Matching;

namespace ModRouter.Test;

[TestFixture]
public sealed class ExperienceMetadataTests
{
    static RouteCompilationException Fails(Action<RouteMap> configure)
        => Assert.Throws<RouteCompilationException>(() => RouteTestKit.Compile(configure))!;

    static RouteParameters Parameters(string name, object value)
        => new(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { [name] = value });

    [Test]
    public void Breadcrumb_overloads_attach_a_label_a_factory_or_a_hidden_flag_to_the_route_declared_last()
    {
        var definitions = RouteMap.Create()
            .Route("/a", typeof(HomePage)).Breadcrumb("Alpha")
            .Route("/b/{id:int}", typeof(OtherPage)).Breadcrumb(p => $"#{p["id"]}")
            .Route("/c", typeof(CustomersPage)).Breadcrumb(hidden: true)
            .Build();

        var label = definitions[0].Metadata.Get<RouteBreadcrumbMetadata>()!;
        var factory = definitions[1].Metadata.Get<RouteBreadcrumbMetadata>()!;
        var hidden = definitions[2].Metadata.Get<RouteBreadcrumbMetadata>()!;

        Assert.That(label.Label, Is.EqualTo("Alpha"));
        Assert.That(label.Factory, Is.Null);
        Assert.That(label.Hidden, Is.False);
        Assert.That(factory.Label, Is.Null);
        Assert.That(factory.Factory!(Parameters("id", 5)), Is.EqualTo("#5"));
        Assert.That(hidden.Label, Is.Null);
        Assert.That(hidden.Factory, Is.Null);
        Assert.That(hidden.Hidden, Is.True);
    }

    [Test]
    public void Focus_and_NoAnnounce_attach_typed_metadata()
    {
        var definitions = RouteMap.Create()
            .Route("/a", typeof(HomePage)).Focus("#heading").NoAnnounce()
            .Build();

        Assert.That(definitions[0].Metadata.Get<RouteFocusMetadata>(), Is.EqualTo(new RouteFocusMetadata("#heading")));
        Assert.That(definitions[0].Metadata.Get<RouteAnnounceMetadata>(), Is.EqualTo(new RouteAnnounceMetadata(false)));
    }

    [Test]
    public void The_calls_apply_to_a_child_declared_inside_its_children_callback()
    {
        var definitions = RouteMap.Create()
            .Route("/p", typeof(HomePage), children: c => c.Route("x", typeof(OtherPage)).Breadcrumb("X"))
            .Build();

        Assert.That(definitions[0].Metadata.Count, Is.Zero);
        Assert.That(definitions[0].Children[0].Metadata.Contains<RouteBreadcrumbMetadata>(), Is.True);
    }

    [TestCase("")]
    [TestCase("  ")]
    public void A_blank_label_or_selector_is_rejected(string blank)
    {
        var map = RouteMap.Create().Route("/a", typeof(HomePage));

        Assert.Throws<ArgumentException>(() => map.Breadcrumb(blank));
        Assert.Throws<ArgumentException>(() => map.Focus(blank));
    }

    [Test]
    public void Attaching_before_any_route_is_a_clear_error()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => RouteMap.Create().NoAnnounce())!;

        Assert.That(exception.Message, Does.Contain("NoAnnounce").And.Contain("no route has been declared"));
    }

    [Test]
    public void Declaring_the_same_kind_twice_is_a_compile_error()
    {
        var exception = Fails(map => map
            .Route("/a", typeof(HomePage)).Breadcrumb("One").Breadcrumb("Two"));

        Assert.That(exception.Errors.Select(e => e.Code), Is.EqualTo(new[] { "RT0018" }));
        Assert.That(exception.Message, Does.Contain(".Breadcrumb(...)"));
    }

    [Test]
    public void Each_repeated_kind_is_reported()
    {
        var exception = Fails(map => map
            .Route("/a", typeof(HomePage)).Focus("#a").Focus("#b").NoAnnounce().NoAnnounce());

        Assert.That(exception.Errors.Select(e => e.Code), Is.EqualTo(new[] { "RT0018", "RT0018" }));
        Assert.That(exception.Message, Does.Contain(".Focus(...)").And.Contain(".NoAnnounce()"));
    }

    [Test]
    public void One_of_each_kind_on_one_route_compiles()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/a", typeof(HomePage), name: "a").Breadcrumb("A").Focus("#h").NoAnnounce());

        Assert.That(table.TryGetRoute("a", out var route), Is.True);
        Assert.That(route!.Metadata.Count, Is.EqualTo(3));
    }
}
