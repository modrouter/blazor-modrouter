using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;

namespace ModRouter.Test;

[TestFixture]
public sealed class AmbiguityTests
{
    static RouteCompilationException Fails(Action<RouteMap> configure)
        => Assert.Throws<RouteCompilationException>(() => RouteTestKit.Compile(configure))!;

    static string[] Codes(RouteCompilationException exception) => [.. exception.Errors.Select(e => e.Code)];

    [Test]
    public void Same_shape_with_different_parameter_names_is_ambiguous_and_names_both_routes()
    {
        var exception = Fails(map => map
            .Route("/a/{x}", typeof(HomePage), name: "one")
            .Route("/a/{y}", typeof(AboutPage), name: "two"));

        Assert.That(Codes(exception), Is.EqualTo(new[] { "RT0012" }));
        Assert.That(exception.Errors[0].Message, Does.Contain("'/a/{x}'").And.Contain("'/a/{y}'")
            .And.Contain("HomePage").And.Contain("AboutPage").And.Contain("equal precedence"));
    }

    [Test]
    public void The_same_template_declared_twice_is_ambiguous()
    {
        Assert.That(Codes(Fails(map => map.Route("/a", typeof(HomePage)).Route("/a", typeof(AboutPage)))), Is.EqualTo(new[] { "RT0012" }));
    }

    [Test]
    public void An_alias_equal_to_its_own_canonical_template_is_ambiguous()
    {
        Assert.That(Codes(Fails(map => map.Route("/a", typeof(HomePage), aliases: ["/a"]))), Is.EqualTo(new[] { "RT0012" }));
    }

    [Test]
    public void An_alias_that_clashes_with_another_route_is_ambiguous()
    {
        var exception = Fails(map => map
            .Route("/customers/{id:int}", typeof(CustomerDetails), aliases: ["/clients/{id:int}"])
            .Route("/clients/{n:int}", typeof(OtherPage)));

        Assert.That(Codes(exception), Is.EqualTo(new[] { "RT0012" }));
    }

    [Test]
    public void An_index_child_and_a_sibling_at_the_same_path_are_ambiguous()
    {
        var exception = Fails(map => map
            .Route("/a", typeof(HomePage), children: c => c.Index(typeof(AboutPage)))
            .Route("/a", typeof(OtherPage)));

        Assert.That(Codes(exception), Is.EqualTo(new[] { "RT0012" }));
    }

    [Test]
    public void Two_catch_alls_at_the_same_position_are_ambiguous()
    {
        Assert.That(Codes(Fails(map => map.Route("/f/{*a}", typeof(HomePage)).Route("/f/{*b}", typeof(AboutPage)))), Is.EqualTo(new[] { "RT0012" }));
    }

    [Test]
    public void Two_optional_routes_sharing_a_prefix_are_ambiguous()
    {
        Assert.That(Codes(Fails(map => map.Route("/a/{x?}", typeof(HomePage)).Route("/a/{y?}", typeof(AboutPage)))), Is.EqualTo(new[] { "RT0012" }));
    }

    [Test]
    public void Legitimate_overlaps_compile()
    {
        Assert.DoesNotThrow(() => RouteTestKit.Compile(map => map
            .Route("/a/{id}", typeof(HomePage))
            .Route("/a/{id?}", typeof(AboutPage))
            .Route("/a/{id:int}", typeof(OtherPage))
            .Route("/a/{name:alpha}", typeof(FilesPage))
            .Route("/a/new", typeof(CustomersPage))
            .Route("/a", typeof(CustomerList))
            .Route("/a/{*rest}", typeof(CustomerDetails))));
    }

    [Test]
    public void Only_one_error_is_reported_per_ambiguous_pair_even_when_it_spans_several_nodes()
    {
        var exception = Fails(map => map
            .Route("/a/{x?}/{y?}", typeof(HomePage))
            .Route("/a/{p?}/{q?}", typeof(AboutPage)));

        Assert.That(Codes(exception), Is.EqualTo(new[] { "RT0012" }));
    }
}
