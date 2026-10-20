using ModRouter.Core.Compilation;
using ModRouter.Core.Definitions;
using ModRouter.Core.Matching;

namespace ModRouter.Test;

[TestFixture]
public sealed class MatcherTests
{
    static readonly Guid OrderId = Guid.Parse("4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77");

    // ---- precedence -------------------------------------------------------------------------------------------

    [Test]
    public void Matches_a_static_route()
    {
        var table = RouteTestKit.Flat(("/dashboard", "dash"));

        Assert.That(RouteTestKit.Leaf(table, "/dashboard"), Is.EqualTo("dash"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Static_beats_parameter_regardless_of_declaration_order(bool staticFirst)
    {
        var table = staticFirst
            ? RouteTestKit.Flat(("/customers/new", "new"), ("/customers/{id}", "by-id"))
            : RouteTestKit.Flat(("/customers/{id}", "by-id"), ("/customers/new", "new"));

        Assert.That(RouteTestKit.Leaf(table, "/customers/new"), Is.EqualTo("new"));
        Assert.That(RouteTestKit.Leaf(table, "/customers/42"), Is.EqualTo("by-id"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Constrained_beats_unconstrained_regardless_of_declaration_order(bool constrainedFirst)
    {
        var table = constrainedFirst
            ? RouteTestKit.Flat(("/c/{id:int}", "int"), ("/c/{slug}", "slug"))
            : RouteTestKit.Flat(("/c/{slug}", "slug"), ("/c/{id:int}", "int"));

        Assert.That(RouteTestKit.Leaf(table, "/c/5"), Is.EqualTo("int"));
        Assert.That(RouteTestKit.Leaf(table, "/c/five"), Is.EqualTo("slug"));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Equal_constraint_counts_are_ordered_by_constraint_key_not_declaration_order(bool alphaFirst)
    {
        var table = alphaFirst
            ? RouteTestKit.Flat(("/c/{name:alpha}", "alpha"), ("/c/{n:regex(^[a-z]+$)}", "regex"))
            : RouteTestKit.Flat(("/c/{n:regex(^[a-z]+$)}", "regex"), ("/c/{name:alpha}", "alpha"));

        Assert.That(RouteTestKit.Leaf(table, "/c/abc"), Is.EqualTo("alpha"));
    }

    [Test]
    public void More_constraints_beat_fewer()
    {
        var table = RouteTestKit.Flat(("/c/{y:alpha}", "loose"), ("/c/{x:alpha:length(2,4)}", "strict"));

        Assert.That(RouteTestKit.Leaf(table, "/c/abc"), Is.EqualTo("strict"));
        Assert.That(RouteTestKit.Leaf(table, "/c/abcdefg"), Is.EqualTo("loose"));
    }

    [Test]
    public void Backtracks_out_of_a_literal_branch_that_cannot_complete()
    {
        var table = RouteTestKit.Flat(("/a/b/c", "abc"), ("/a/{x}/d", "axd"));

        Assert.That(RouteTestKit.Leaf(table, "/a/b/c"), Is.EqualTo("abc"));
        Assert.That(RouteTestKit.Leaf(table, "/a/b/d"), Is.EqualTo("axd"));
    }

    // ---- optional and catch-all -------------------------------------------------------------------------------

    [Test]
    public void Optional_parameter_may_be_omitted()
    {
        var table = RouteTestKit.Flat(("/a/{id?}", "opt"));

        var without = RouteTestKit.Match(table, "/a");
        var with = RouteTestKit.Match(table, "/a/5");

        Assert.That(without.Leaf.Parameters.Contains("id"), Is.False);
        Assert.That(with.Leaf.Parameters.Get<string>("id"), Is.EqualTo("5"));
    }

    [Test]
    public void Required_beats_optional_when_a_value_is_present_and_optional_alone_matches_without_one()
    {
        var table = RouteTestKit.Flat(("/a/{id?}", "opt"), ("/a/{id}", "req"));

        Assert.That(RouteTestKit.Leaf(table, "/a/5"), Is.EqualTo("req"));
        Assert.That(RouteTestKit.Leaf(table, "/a"), Is.EqualTo("opt"));
    }

    [Test]
    public void Static_route_beats_the_omitted_form_of_an_optional_route()
    {
        var table = RouteTestKit.Flat(("/a/{id?}", "opt"), ("/a", "plain"));

        Assert.That(RouteTestKit.Leaf(table, "/a"), Is.EqualTo("plain"));
        Assert.That(RouteTestKit.Leaf(table, "/a/9"), Is.EqualTo("opt"));
    }

    [Test]
    public void Several_trailing_optionals_bind_from_the_left()
    {
        var table = RouteTestKit.Flat(("/a/{x?}/{y?}", "two"));

        var none = RouteTestKit.Match(table, "/a").Leaf.Parameters;
        var one = RouteTestKit.Match(table, "/a/1").Leaf.Parameters;
        var both = RouteTestKit.Match(table, "/a/1/2").Leaf.Parameters;

        Assert.That(none.Count, Is.EqualTo(0));
        Assert.That(one.Get<string>("x"), Is.EqualTo("1"));
        Assert.That(one.Contains("y"), Is.False);
        Assert.That(both.Get<string>("y"), Is.EqualTo("2"));
    }

    [TestCase("/files/{*path}")]
    [TestCase("/files/{**path}")]
    public void Catch_all_captures_the_rest_with_slashes_and_matches_zero_segments(string template)
    {
        var table = RouteTestKit.Flat((template, "files"));

        Assert.That(RouteTestKit.Match(table, "/files").Leaf.Parameters.Get<string>("path"), Is.Empty);
        Assert.That(RouteTestKit.Match(table, "/files/a/b/c").Leaf.Parameters.Get<string>("path"), Is.EqualTo("a/b/c"));
        Assert.That(RouteTestKit.Match(table, "/files/a%20b/c").Leaf.Parameters.Get<string>("path"), Is.EqualTo("a b/c"));
    }

    [Test]
    public void Catch_all_loses_to_static_and_parameter_siblings()
    {
        var table = RouteTestKit.Flat(("/files/{*path}", "all"), ("/files/readme", "readme"), ("/files", "index"), ("/files/{id:int}", "byid"));

        Assert.That(RouteTestKit.Leaf(table, "/files"), Is.EqualTo("index"));
        Assert.That(RouteTestKit.Leaf(table, "/files/readme"), Is.EqualTo("readme"));
        Assert.That(RouteTestKit.Leaf(table, "/files/7"), Is.EqualTo("byid"));
        Assert.That(RouteTestKit.Leaf(table, "/files/7/extra"), Is.EqualTo("all"));
    }

    [Test]
    public void Root_catch_all_matches_the_empty_path()
    {
        var table = RouteTestKit.Flat(("/{*all}", "all"));

        Assert.That(RouteTestKit.Match(table, "/").Leaf.Parameters.Get<string>("all"), Is.Empty);
        Assert.That(RouteTestKit.Match(table, "/x/y").Leaf.Parameters.Get<string>("all"), Is.EqualTo("x/y"));
    }

    [Test]
    public void Root_optional_parameter()
    {
        var table = RouteTestKit.Flat(("/{id?}", "root"));

        Assert.That(RouteTestKit.Match(table, "/").Leaf.Parameters.Count, Is.EqualTo(0));
        Assert.That(RouteTestKit.Match(table, "/5").Leaf.Parameters.Get<string>("id"), Is.EqualTo("5"));
    }

    // ---- nesting ----------------------------------------------------------------------------------------------

    [Test]
    public void Nested_match_returns_the_full_chain_with_parameters_per_level()
    {
        var table = RouteTestKit.CustomersTable();

        var chain = RouteTestKit.Match(table, $"/customers/42/orders/{OrderId}");

        Assert.That(RouteTestKit.Names(chain), Is.EqualTo(new[] { "customers", "customer-details", "customer-order-details" }));
        Assert.That(chain.Matches[0].Parameters.Count, Is.EqualTo(0));
        Assert.That(chain.Matches[1].Parameters.Get<int>("id"), Is.EqualTo(42));
        Assert.That(chain.Matches[1].Parameters.Contains("orderId"), Is.False);
        Assert.That(chain.Matches[2].Parameters.Get<Guid>("orderId"), Is.EqualTo(OrderId));
        Assert.That(chain.Matches[2].Parameters.Contains("id"), Is.False);
        Assert.That(chain.Matches[2].InheritedParameters.Get<int>("id"), Is.EqualTo(42));
        Assert.That(chain.Matches[2].InheritedParameters.Get<Guid>("orderId"), Is.EqualTo(OrderId));
        Assert.That(chain.Leaf, Is.SameAs(chain.Matches[2]));
    }

    [Test]
    public void Each_level_reports_its_own_templates_and_paths()
    {
        var chain = RouteTestKit.Match(RouteTestKit.CustomersTable(), $"/customers/42/orders/{OrderId}");

        Assert.That(chain.Matches.Select(m => m.Template), Is.EqualTo(new[]
        {
            "/customers",
            "/customers/{id:int}",
            "/customers/{id:int}/orders/{orderId:guid}",
        }));
        Assert.That(chain.Matches.Select(m => m.MatchedPath), Is.EqualTo(new[]
        {
            "/customers",
            "/customers/42",
            $"/customers/42/orders/{OrderId}",
        }));
        Assert.That(chain.Matches.Select(m => m.RemainingPath), Is.EqualTo(new[]
        {
            $"/42/orders/{OrderId}",
            $"/orders/{OrderId}",
            "",
        }));
        Assert.That(chain.MatchedTemplate, Is.EqualTo("/customers/{id:int}/orders/{orderId:guid}"));
        Assert.That(chain.MatchedViaAlias, Is.False);
    }

    [Test]
    public void Titles_come_from_static_text_or_the_factory_with_inherited_parameters()
    {
        var chain = RouteTestKit.Match(RouteTestKit.CustomersTable(), $"/customers/42/orders/{OrderId}");

        Assert.That(chain.Matches.Select(m => m.Title), Is.EqualTo(new[] { "Customers", "Customer 42", $"Order {OrderId}" }));
    }

    [Test]
    public void Contexts_are_linked_parent_to_child()
    {
        var chain = RouteTestKit.Match(RouteTestKit.CustomersTable(), "/customers/42");

        Assert.That(chain.Matches[0].Parent, Is.Null);
        Assert.That(chain.Matches[0].Child, Is.SameAs(chain.Matches[1]));
        Assert.That(chain.Matches[1].Parent, Is.SameAs(chain.Matches[0]));
        Assert.That(chain.Matches[1].Child, Is.Null);
    }

    [Test]
    public void An_index_child_renders_at_the_parents_path()
    {
        var chain = RouteTestKit.Match(RouteTestKit.CustomersTable(), "/customers");

        Assert.That(RouteTestKit.Names(chain), Is.EqualTo(new[] { "customers", "customer-list" }));
        Assert.That(chain.Leaf.MatchedPath, Is.EqualTo("/customers"));
        Assert.That(chain.Leaf.RemainingPath, Is.Empty);
        Assert.That(chain.Matches[0].RemainingPath, Is.Empty);
    }

    [Test]
    public void Child_routes_still_match_when_the_parent_has_an_index()
    {
        var chain = RouteTestKit.Match(RouteTestKit.CustomersTable(), "/customers/42");

        Assert.That(RouteTestKit.Names(chain), Is.EqualTo(new[] { "customers", "customer-details" }));
    }

    [Test]
    public void A_parent_without_an_index_matches_alone_with_an_empty_outlet()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/a", typeof(HomePage), name: "a", children: c => c.Route("b", typeof(AboutPage), name: "b")));

        Assert.That(RouteTestKit.Names(RouteTestKit.Match(table, "/a")), Is.EqualTo(new[] { "a" }));
        Assert.That(RouteTestKit.Names(RouteTestKit.Match(table, "/a/b")), Is.EqualTo(new[] { "a", "b" }));
    }

    [Test]
    public void Parameter_values_are_converted_by_their_constraint()
    {
        var chain = RouteTestKit.Match(RouteTestKit.CustomersTable(), $"/customers/42/orders/{OrderId}");

        Assert.That(chain.Matches[1].Parameters["id"], Is.TypeOf<int>());
        Assert.That(chain.Matches[2].Parameters["orderId"], Is.TypeOf<Guid>());
    }

    // ---- aliases ----------------------------------------------------------------------------------------------

    [Test]
    public void An_alias_matches_and_reports_the_canonical_template()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/customers/{id:int}", typeof(CustomerDetails), name: "details", aliases: ["/clients/{id:int}"]));

        var viaAlias = RouteTestKit.Match(table, "/clients/7");
        var canonical = RouteTestKit.Match(table, "/customers/7");

        Assert.That(viaAlias.MatchedViaAlias, Is.True);
        Assert.That(viaAlias.MatchedTemplate, Is.EqualTo("/clients/{id:int}"));
        Assert.That(viaAlias.CanonicalTemplate, Is.EqualTo("/customers/{id:int}"));
        Assert.That(viaAlias.Leaf.RouteName, Is.EqualTo("details"));
        Assert.That(viaAlias.Leaf.Parameters.Get<int>("id"), Is.EqualTo(7));
        Assert.That(canonical.MatchedViaAlias, Is.False);
    }

    // ---- normalization ----------------------------------------------------------------------------------------

    [Test]
    public void Literals_match_case_insensitively_but_parameter_values_keep_their_case()
    {
        var table = RouteTestKit.Flat(("/files/{name}", "file"));

        var chain = RouteTestKit.Match(table, "/FILES/ReadMe.TXT");

        Assert.That(chain.Leaf.Parameters.Get<string>("name"), Is.EqualTo("ReadMe.TXT"));
    }

    [TestCase("/customers/42")]
    [TestCase("/customers/42/")]
    [TestCase("//customers//42")]
    [TestCase("customers/42")]
    public void Slash_variations_match_the_same_route(string path)
    {
        Assert.That(RouteTestKit.Leaf(RouteTestKit.CustomersTable(), path), Is.EqualTo("customer-details"));
    }

    [TestCase("/")]
    [TestCase("")]
    public void The_root_path_matches_the_root_route(string path)
    {
        Assert.That(RouteTestKit.Leaf(RouteTestKit.CustomersTable(), path), Is.EqualTo("home"));
    }

    [Test]
    public void Percent_encoded_segments_are_decoded_once()
    {
        var table = RouteTestKit.Flat(("/users/{name}", "user"));

        Assert.That(RouteTestKit.Match(table, "/users/caf%C3%A9").Leaf.Parameters.Get<string>("name"), Is.EqualTo("café"));
        Assert.That(RouteTestKit.Match(table, "/users/a%2Fb").Leaf.Parameters.Get<string>("name"), Is.EqualTo("a/b"));
        Assert.That(RouteTestKit.Match(table, "/users/100%25").Leaf.Parameters.Get<string>("name"), Is.EqualTo("100%"));
    }

    [Test]
    public void Unicode_literals_match_encoded_and_raw()
    {
        var table = RouteTestKit.Flat(("/café", "cafe"));

        Assert.That(RouteTestKit.Leaf(table, "/caf%C3%A9"), Is.EqualTo("cafe"));
        Assert.That(RouteTestKit.Leaf(table, "/café"), Is.EqualTo("cafe"));
    }

    [TestCase("/users/%zz")]
    [TestCase("/users/100%")]
    [TestCase("/users/%4")]
    [TestCase("/users/%FF")]
    [TestCase("/users/%C3")]
    [TestCase("/users/%C3%28")]
    public void Malformed_percent_encoding_is_a_failure_not_an_exception(string path)
    {
        var result = RouteTestKit.Flat(("/users/{name}", "user")).Match(path);

        Assert.That(result.IsMatch, Is.False);
        Assert.That(result.Chain, Is.Null);
        Assert.That(result.Failure, Is.EqualTo(RouteMatchFailure.MalformedPath));
    }

    [TestCase("/nope")]
    [TestCase("/customers/abc")]
    [TestCase("/customers/42/orders")]
    [TestCase("/customers/42/extra")]
    [TestCase("/customers/42/orders/not-a-guid")]
    public void Unknown_paths_are_NotFound(string path)
    {
        var result = RouteTestKit.CustomersTable().Match(path);

        Assert.That(result.IsMatch, Is.False);
        Assert.That(result.Failure, Is.EqualTo(RouteMatchFailure.NotFound));
    }

    [Test]
    public void Extra_segments_do_not_match_a_static_route()
    {
        Assert.That(RouteTestKit.Flat(("/a", "a")).Match("/a/b").IsMatch, Is.False);
    }

    // ---- robustness -------------------------------------------------------------------------------------------

    // Regression: RT0007 once inspected only the canonical template, so an alias ending in an optional or catch-all
    // on a parent with children compiled and registered a terminal shorter than a non-leaf level; Match then threw
    // IndexOutOfRangeException for '/b' instead of never throwing.
    [TestCase("/b/{x?}")]
    [TestCase("/b/{*rest}")]
    public void An_alias_ending_in_an_optional_or_catch_all_on_a_parent_with_children_is_rejected_at_compile_time(string alias)
    {
        var exception = Assert.Throws<RouteCompilationException>(() => RouteTestKit.Compile(map => map
            .Route("/a", typeof(HomePage), name: "a", aliases: [alias], children: c => c.Index(typeof(CustomerList), name: "idx"))))!;

        Assert.That(exception.Errors.Select(e => e.Code), Is.EqualTo(new[] { "RT0007" }));
    }

    // ---- PathNormalizer ---------------------------------------------------------------------------------------

    [TestCase("/a//b/", new[] { "a", "b" })]
    [TestCase("", new string[0])]
    [TestCase("/", new string[0])]
    [TestCase("a/b%20c", new[] { "a", "b c" })]
    public void PathNormalizer_splits_and_decodes(string path, string[] expected)
    {
        Assert.That(PathNormalizer.TrySplit(path, out var segments), Is.True);
        Assert.That(segments, Is.EqualTo(expected));
    }

    [Test]
    public void PathNormalizer_reports_malformed_escapes()
    {
        Assert.That(PathNormalizer.TrySplit("/a/%g1", out var segments), Is.False);
        Assert.That(segments, Is.Empty);
    }

    [Test]
    public void PathNormalizer_reports_invalid_utf8_escapes()
    {
        Assert.That(PathNormalizer.TrySplit("/a/%FF", out var segments), Is.False);
        Assert.That(segments, Is.Empty);
    }

    [TestCase("/users/%C3%A9", "é")]
    [TestCase("/users/%F0%9F%98%80", "😀")]
    [TestCase("/users/100%25", "100%")]
    [TestCase("/users/%2541", "%41")]
    [TestCase("/users/caf%C3%A9-😀", "café-😀")]
    public void Valid_utf8_escapes_decode_once(string path, string expected)
    {
        Assert.That(PathNormalizer.TrySplit(path, out var segments), Is.True);
        Assert.That(segments, Is.EqualTo(new[] { "users", expected }));
    }
}
