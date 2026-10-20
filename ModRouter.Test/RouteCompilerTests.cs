using ModRouter.Core.Compilation;
using ModRouter.Core.Constraints;
using ModRouter.Core.Definitions;
using ModRouter.Core.Syntax;

namespace ModRouter.Test;

[TestFixture]
public sealed class RouteCompilerTests
{
    static RouteCompilationException Fails(Action<RouteMap> configure)
        => Assert.Throws<RouteCompilationException>(() => RouteTestKit.Compile(configure))!;

    static string[] Codes(RouteCompilationException exception) => [.. exception.Errors.Select(e => e.Code)];

    static CompiledRoute Canonical(CompiledRouteTable table, string name)
        => table.Routes.Single(r => r.Definition.Name == name && !r.IsAlias);

    [Test]
    public void Flattens_nested_templates_into_full_templates()
    {
        var table = RouteTestKit.CustomersTable();

        Assert.That(Canonical(table, "home").Template, Is.EqualTo("/"));
        Assert.That(Canonical(table, "customers").Template, Is.EqualTo("/customers"));
        Assert.That(Canonical(table, "customer-list").Template, Is.EqualTo("/customers"));
        Assert.That(Canonical(table, "customer-details").Template, Is.EqualTo("/customers/{id:int}"));
        Assert.That(Canonical(table, "customer-order-details").Template, Is.EqualTo("/customers/{id:int}/orders/{orderId:guid}"));
    }

    [Test]
    public void Entries_remember_their_ancestor_chain_and_per_level_templates()
    {
        var entry = Canonical(RouteTestKit.CustomersTable(), "customer-order-details");

        Assert.That(entry.Chain.Select(d => d.Name), Is.EqualTo(new[] { "customers", "customer-details", "customer-order-details" }));
        Assert.That(entry.LevelTemplates, Is.EqualTo(new[]
        {
            "/customers",
            "/customers/{id:int}",
            "/customers/{id:int}/orders/{orderId:guid}",
        }));
        Assert.That(entry.LevelEnds, Is.EqualTo(new[] { 1, 2, 4 }));
    }

    [Test]
    public void An_index_child_makes_its_parent_non_matchable_and_shares_the_parent_path()
    {
        var table = RouteTestKit.CustomersTable();
        var index = Canonical(table, "customer-list");

        Assert.That(index.Chain.Select(d => d.Name), Is.EqualTo(new[] { "customers", "customer-list" }));
        Assert.That(index.IsMatchable, Is.True);
        Assert.That(Canonical(table, "customers").IsMatchable, Is.False);
        Assert.That(Canonical(table, "customer-details").IsMatchable, Is.True);
    }

    [Test]
    public void A_parent_without_an_index_child_is_matchable_itself()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/a", typeof(HomePage), name: "a", children: c => c.Route("b", typeof(AboutPage), name: "b")));

        Assert.That(Canonical(table, "a").IsMatchable, Is.True);
        Assert.That(Canonical(table, "b").Template, Is.EqualTo("/a/b"));
    }

    [Test]
    public void Aliases_become_extra_entries_that_point_at_the_canonical_template()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/customers/{id:int}", typeof(CustomerDetails), name: "details", aliases: ["/clients/{id:int}", "/customer/{id:int}"]));

        Assert.That(table.Routes.Select(r => r.Template), Is.EqualTo(new[] { "/customers/{id:int}", "/clients/{id:int}", "/customer/{id:int}" }));
        Assert.That(table.Routes.Select(r => r.IsAlias), Is.EqualTo(new[] { false, true, true }));
        Assert.That(table.Routes.Select(r => r.CanonicalTemplate), Is.All.EqualTo("/customers/{id:int}"));
    }

    [Test]
    public void A_parents_alias_also_prefixes_its_children()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/customers", typeof(CustomersPage), aliases: ["/clients"], children: c => c
                .Route("{id:int}", typeof(CustomerDetails), name: "details")));

        var details = table.Routes.Where(r => r.Definition.Name == "details").ToList();

        Assert.That(details.Select(r => r.Template), Is.EqualTo(new[] { "/customers/{id:int}", "/clients/{id:int}" }));
        Assert.That(details.Select(r => r.IsAlias), Is.EqualTo(new[] { false, true }));
        Assert.That(details.Select(r => r.CanonicalTemplate), Is.All.EqualTo("/customers/{id:int}"));
    }

    [Test]
    public void Name_lookup_is_case_insensitive()
    {
        var table = RouteTestKit.CustomersTable();

        Assert.That(table.TryGetRoute("CUSTOMER-DETAILS", out var route), Is.True);
        Assert.That(route!.Component, Is.EqualTo(typeof(CustomerDetails)));
        Assert.That(table.GetRequiredRoute("Home").Template, Is.EqualTo("/"));
        Assert.That(table.TryGetRoute("nope", out _), Is.False);
    }

    [Test]
    public void TryGetRoute_with_a_null_name_throws_ArgumentNullException_naming_routeName()
    {
        var table = RouteTestKit.CustomersTable();

        var exception = Assert.Throws<ArgumentNullException>(() => table.TryGetRoute(null!, out _))!;

        Assert.That(exception.ParamName, Is.EqualTo("routeName"));
    }

    [Test]
    public void GetRequiredRoute_for_an_unknown_name_lists_the_registered_names()
    {
        var exception = Assert.Throws<KeyNotFoundException>(() => RouteTestKit.CustomersTable().GetRequiredRoute("nope"));

        Assert.That(exception!.Message, Does.Contain("'nope'").And.Contain("customer-details").And.Contain("home"));
    }

    [Test]
    public void Unnamed_routes_compile_and_are_simply_not_indexed()
    {
        var table = RouteTestKit.Compile(map => map.Route("/a", typeof(HomePage)).Route("/b", typeof(AboutPage)));

        Assert.That(table.Routes, Has.Count.EqualTo(2));
        Assert.That(table.TryGetRoute("a", out _), Is.False);
    }

    [Test]
    public void Reports_RT0001_for_template_syntax_errors_with_the_position()
    {
        var exception = Fails(map => map.Route("/a/{", typeof(HomePage)));

        Assert.That(Codes(exception), Is.EqualTo(new[] { "RT0001" }));
        Assert.That(exception.Errors[0].Position, Is.EqualTo(3));
        Assert.That(exception.Errors[0].Message, Does.Contain("'/a/{'").And.Contain("Unclosed"));
    }

    [Test]
    public void Reports_RT0002_when_a_child_template_or_alias_starts_with_a_slash()
    {
        Assert.That(Codes(Fails(map => map
            .Route("/a", typeof(HomePage), children: c => c.Route("/x", typeof(AboutPage))))), Is.EqualTo(new[] { "RT0002" }));
        Assert.That(Codes(Fails(map => map
            .Route("/a", typeof(HomePage), children: c => c.Route("x", typeof(AboutPage), aliases: ["/y"])))), Is.EqualTo(new[] { "RT0002" }));
    }

    [Test]
    public void Reports_RT0003_for_an_empty_child_template()
    {
        var exception = Fails(map => map.Route("/a", typeof(HomePage), children: c => c.Route("", typeof(AboutPage))));

        Assert.That(Codes(exception), Is.EqualTo(new[] { "RT0003" }));
        Assert.That(exception.Errors[0].Message, Does.Contain("RouteMap.Index"));
    }

    [Test]
    public void Reports_RT0004_for_an_unknown_constraint()
    {
        var exception = Fails(map => map.Route("/a/{id:foo}", typeof(HomePage)));

        Assert.That(Codes(exception), Is.EqualTo(new[] { "RT0004" }));
        Assert.That(exception.Errors[0].Message, Does.Contain("Unknown route constraint 'foo'").And.Contain("'id'"));
    }

    [Test]
    public void Reports_RT0005_when_a_catch_all_is_not_last()
    {
        Assert.That(Codes(Fails(map => map.Route("/a/{*p}/b", typeof(HomePage)))), Is.EqualTo(new[] { "RT0005" }));
    }

    [TestCase("/a/{x?}/{y}")]
    [TestCase("/a/{x?}/{*rest}")]
    public void Reports_RT0006_when_a_required_segment_or_catch_all_follows_an_optional(string template)
    {
        Assert.That(Codes(Fails(map => map.Route(template, typeof(HomePage)))), Is.EqualTo(new[] { "RT0006" }));
    }

    [TestCase("/a/{x?}")]
    [TestCase("/a/{*rest}")]
    public void Reports_RT0007_for_children_under_an_optional_or_catch_all_route(string template)
    {
        var exception = Fails(map => map.Route(template, typeof(HomePage), children: c => c.Route("b", typeof(AboutPage))));

        Assert.That(Codes(exception), Is.EqualTo(new[] { "RT0007" }));
    }

    [Test]
    public void Reports_RT0008_for_two_index_children()
    {
        var exception = Fails(map => map.Route("/a", typeof(HomePage), children: c => c
            .Index(typeof(HomePage), name: "one")
            .Index(typeof(AboutPage), name: "two")));

        Assert.That(Codes(exception), Is.EqualTo(new[] { "RT0008" }));
        Assert.That(exception.Errors[0].Message, Does.Contain("more than one index"));
    }

    [Test]
    public void Reports_RT0009_for_misplaced_index_routes()
    {
        RouteCompilationException Compile(params RouteDefinition[] definitions)
            => Assert.Throws<RouteCompilationException>(() => RouteCompiler.Compile(definitions))!;

        var rootIndex = new RouteDefinition { Template = "", Component = typeof(HomePage), IsIndex = true };
        Assert.That(Codes(Compile(rootIndex)), Is.EqualTo(new[] { "RT0009" }));

        var indexWithPath = new RouteDefinition { Template = "x", Component = typeof(AboutPage), IsIndex = true };
        var indexWithChildren = new RouteDefinition { Template = "", Component = typeof(AboutPage), IsIndex = true, Children = [new RouteDefinition { Template = "y", Component = typeof(OtherPage) }] };
        var indexWithAliases = new RouteDefinition { Template = "", Component = typeof(AboutPage), IsIndex = true, Aliases = ["z"] };

        foreach (var index in new[] { indexWithPath, indexWithChildren, indexWithAliases })
        {
            var parent = new RouteDefinition { Template = "/a", Component = typeof(HomePage), Children = [index] };
            Assert.That(Codes(Compile(parent)), Is.EqualTo(new[] { "RT0009" }));
        }
    }

    [Test]
    public void Reports_RT0010_for_duplicate_names_and_identifies_both_routes()
    {
        var exception = Fails(map => map
            .Route("/c", typeof(HomePage), name: "Dup")
            .Route("/d", typeof(AboutPage), name: "dup"));

        Assert.That(Codes(exception), Is.EqualTo(new[] { "RT0010" }));
        Assert.That(exception.Errors[0].Message, Does.Contain("HomePage").And.Contain("AboutPage").And.Contain("RouteCompilerTests.cs").And.Contain("unique"));
    }

    [Test]
    public void Reports_RT0011_for_duplicate_parameter_names_including_ones_redeclared_by_a_child()
    {
        Assert.That(Codes(Fails(map => map.Route("/a/{id}/{id}", typeof(HomePage)))), Is.EqualTo(new[] { "RT0011" }));
        Assert.That(Codes(Fails(map => map
            .Route("/a/{id}", typeof(HomePage), children: c => c.Route("b/{id}", typeof(AboutPage))))), Is.EqualTo(new[] { "RT0011" }));
    }

    [Test]
    public void Reports_RT0013_for_a_whitespace_name()
    {
        Assert.That(Codes(Fails(map => map.Route("/a", typeof(HomePage), name: "  "))), Is.EqualTo(new[] { "RT0013" }));
    }

    [Test]
    public void Collects_every_problem_instead_of_stopping_at_the_first()
    {
        var exception = Fails(map => map
            .Route("/a/{", typeof(HomePage), name: "a")
            .Route("/b/{id:foo}", typeof(HomePage), name: "b")
            .Route("/c", typeof(HomePage), name: "same")
            .Route("/d", typeof(AboutPage), name: "same"));

        Assert.That(Codes(exception), Is.EquivalentTo(new[] { "RT0001", "RT0004", "RT0010" }));
        Assert.That(exception.Message, Does.Contain("3 errors").And.Contain("RT0001").And.Contain("RT0004").And.Contain("RT0010"));
    }

    [Test]
    public void Custom_constraint_registries_are_honoured()
    {
        var registry = new ConstraintRegistry();
        registry.Register("always", _ => new AlwaysConstraint());

        var table = RouteTestKit.Compile(map => map.Route("/a/{x:always}", typeof(HomePage), name: "a"), registry);

        Assert.That(table.Routes.Single().Template, Is.EqualTo("/a/{x:always}"));
        Assert.That(table.Routes.Single().Constraints[1], Is.TypeOf<AlwaysConstraint>());
    }

    sealed class AlwaysConstraint : IRouteConstraint
    {
        public Type ValueType => typeof(string);

        public bool TryMatch(string rawValue, out object? convertedValue)
        {
            convertedValue = rawValue;

            return true;
        }

        public bool TryFormat(object value, out string text)
        {
            text = value.ToString() ?? string.Empty;

            return true;
        }
    }

    [Test]
    public void A_custom_constraint_matches_and_generates_and_a_later_registration_does_not_change_the_table()
    {
        var registry = new ConstraintRegistry();
        registry.Register("even", _ => new EvenConstraint());

        var table = RouteTestKit.Compile(map => map.Route("/n/{x:even}", typeof(HomePage), name: "n"), registry);
        registry.Register("even", _ => new AlwaysConstraint());

        Assert.That(table.Match("/n/4").IsMatch, Is.True);
        Assert.That(table.Match("/n/5").IsMatch, Is.False);
        Assert.That(table.GenerateUrl("n", new Dictionary<string, object?> { ["x"] = 8 }), Is.EqualTo("/n/8"));
        Assert.That(table.TryGenerateUrl("n", new Dictionary<string, object?> { ["x"] = 7 }, null, null, null, out _, out var error), Is.False);
        Assert.That(error, Is.Not.Null);
    }

    sealed class EvenConstraint : IRouteConstraint
    {
        public Type ValueType => typeof(int);

        public bool TryMatch(string rawValue, out object? convertedValue)
        {
            var ok = int.TryParse(rawValue, out var number) && number % 2 == 0;
            convertedValue = ok ? number : null;

            return ok;
        }

        public bool TryFormat(object value, out string text)
        {
            text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

            return int.TryParse(text, out var number) && number % 2 == 0;
        }
    }

    [Test]
    public void Ambiguities_are_reported_alongside_other_errors()
    {
        var exception = Fails(map => map
            .Route("/a", typeof(HomePage), name: "dup")
            .Route("/b", typeof(AboutPage), name: "dup")
            .Route("/c/{x}", typeof(HomePage))
            .Route("/c/{y}", typeof(AboutPage)));

        Assert.That(Codes(exception), Is.EquivalentTo(new[] { "RT0010", "RT0012" }));
    }

    [Test]
    public void An_alias_syntax_error_reports_the_alias_text_and_a_position_within_it()
    {
        var exception = Fails(map => map.Route("/ok", typeof(HomePage), aliases: ["/bad/{x"]));

        Assert.That(Codes(exception), Is.EqualTo(new[] { "RT0001" }));
        Assert.That(exception.Errors[0].Template, Is.EqualTo("/bad/{x"));
        Assert.That(exception.Errors[0].Position, Is.EqualTo(5));
    }

    [Test]
    public void A_template_error_still_reports_the_templates_own_text()
    {
        var exception = Fails(map => map.Route("/ok", typeof(HomePage), children: c => c.Route("/x", typeof(AboutPage), aliases: ["/y"])));

        Assert.That(exception.Errors.Select(e => e.Template), Is.EquivalentTo(new[] { "/x", "/y" }));
    }

    [Test]
    public void Compiled_data_is_exposed_as_real_read_only_collections()
    {
        var table = RouteTestKit.CustomersTable();
        var route = Canonical(table, "customer-order-details");

        Assert.That(table.Routes, Is.Not.InstanceOf<List<CompiledRoute>>());
        Assert.That(route.Segments, Is.Not.InstanceOf<List<TemplateSegment>>());
        Assert.That(route.Constraints, Is.Not.InstanceOf<List<IRouteConstraint?>>());
        Assert.That(route.Chain, Is.Not.InstanceOf<List<RouteDefinition>>());
        Assert.That(route.LevelEnds, Is.Not.InstanceOf<List<int>>());
        Assert.That(((System.Collections.IList)route.Segments).IsReadOnly, Is.True);
        Assert.That(((System.Collections.IList)table.Routes).IsReadOnly, Is.True);
        Assert.That(((System.Collections.IList)route.Chain).IsReadOnly, Is.True);
        Assert.That(((System.Collections.IList)route.Constraints).IsReadOnly, Is.True);

        var parsed = TemplateParser.Parse("/a/{b");

        Assert.That(parsed.Segments, Is.Not.InstanceOf<List<TemplateSegment>>());
        Assert.That(parsed.Errors, Is.Not.InstanceOf<List<TemplateError>>());
        Assert.That(((System.Collections.IList)parsed.Segments).IsReadOnly, Is.True);
        Assert.That(((System.Collections.IList)parsed.Errors).IsReadOnly, Is.True);
    }
}
