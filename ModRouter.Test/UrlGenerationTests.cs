using ModRouter.Core.Compilation;
using ModRouter.Core.Generation;

namespace ModRouter.Test;

[TestFixture]
public sealed class UrlGenerationTests
{
    static readonly Guid OrderId = Guid.Parse("4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77");

    static readonly CompiledRouteTable Table = RouteTestKit.Compile(map => map
        .Route("/", typeof(HomePage), name: "home")
        .Route("/customers", typeof(CustomersPage), name: "customers", children: customers => customers
            .Index(typeof(CustomerList), name: "customer-list")
            .Route("{id:int}", typeof(CustomerDetails), name: "customer-details", children: customer => customer
                .Route("orders/{orderId:guid}", typeof(OrderDetails), name: "customer-order-details")))
        .Route("/docs/{section}/{page?}", typeof(OtherPage), name: "doc")
        .Route("/x/{a?}/{b?}", typeof(OtherPage), name: "x")
        .Route("/files/{*path}", typeof(FilesPage), name: "file")
        .Route("/events/{when:datetime}", typeof(OtherPage), name: "event")
        .Route("/flags/{on:bool}", typeof(OtherPage), name: "flag")
        .Route("/slugs/{slug:regex(^[a-z-]+$)}", typeof(OtherPage), name: "slug")
        .Route("/people", typeof(OtherPage), name: "people", aliases: ["/staff"]));

    static Dictionary<string, object?> Values(params (string Name, object? Value)[] values)
        => values.ToDictionary(v => v.Name, v => v.Value);

    static RouteUrlGenerationException Fails(string routeName, Dictionary<string, object?>? parameters = null, Dictionary<string, object?>? ambient = null)
        => Assert.Throws<RouteUrlGenerationException>(() => Table.GenerateUrl(routeName, parameters, ambientValues: ambient))!;

    // ---- basics -----------------------------------------------------------------------------------------------

    [Test]
    public void Combines_ancestor_templates_into_the_url()
    {
        Assert.That(Table.GenerateUrl("home"), Is.EqualTo("/"));
        Assert.That(Table.GenerateUrl("customers"), Is.EqualTo("/customers"));
        Assert.That(Table.GenerateUrl("customer-list"), Is.EqualTo("/customers"));
        Assert.That(Table.GenerateUrl("customer-details", Values(("id", 42))), Is.EqualTo("/customers/42"));
        Assert.That(
            Table.GenerateUrl("customer-order-details", Values(("id", 42), ("orderId", OrderId))),
            Is.EqualTo("/customers/42/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77"));
    }

    [Test]
    public void Route_names_and_parameter_names_are_case_insensitive()
    {
        Assert.That(Table.GenerateUrl("CUSTOMER-DETAILS", Values(("ID", 42))), Is.EqualTo("/customers/42"));
    }

    [Test]
    public void Named_routes_with_aliases_always_generate_the_canonical_url()
    {
        Assert.That(Table.GenerateUrl("people"), Is.EqualTo("/people"));
        Assert.That(Table.TryGetRoute("staff", out _), Is.False);
    }

    [Test]
    public void The_table_implements_INamedRouteRegistry()
    {
        INamedRouteRegistry registry = Table;

        Assert.That(registry.GenerateUrl("customer-details", Values(("id", 7))), Is.EqualTo("/customers/7"));
        Assert.That(registry.GetRequiredRoute("home").Component, Is.EqualTo(typeof(HomePage)));
        Assert.That(registry.TryGetRoute("home", out _), Is.True);
    }

    // ---- errors -----------------------------------------------------------------------------------------------

    [Test]
    public void A_missing_required_parameter_names_the_route_the_parameter_and_the_template()
    {
        var exception = Fails("customer-details");

        Assert.That(exception.Code, Is.EqualTo(UrlGenerationErrorCode.MissingParameter));
        Assert.That(exception.RouteName, Is.EqualTo("customer-details"));
        Assert.That(
            exception.Message,
            Is.EqualTo("Route 'customer-details' requires parameter 'id', but no value was supplied while generating the URL '/customers/{id:int}'."));
    }

    [Test]
    public void A_null_value_counts_as_missing_for_a_required_parameter()
    {
        Assert.That(Fails("customer-details", Values(("id", null))).Code, Is.EqualTo(UrlGenerationErrorCode.MissingParameter));
    }

    [TestCase("abc")]
    [TestCase(1.5)]
    [TestCase(true)]
    public void A_value_that_violates_the_constraint_is_rejected(object value)
    {
        var exception = Fails("customer-details", Values(("id", value)));

        Assert.That(exception.Code, Is.EqualTo(UrlGenerationErrorCode.ConstraintViolation));
        Assert.That(exception.Message, Does.Contain("'id'").And.Contain("'int'").And.Contain("customer-details").And.Contain("/customers/{id:int}"));
    }

    [Test]
    public void Regex_and_empty_values_are_rejected_at_generation_time()
    {
        Assert.That(Table.GenerateUrl("slug", Values(("slug", "my-slug"))), Is.EqualTo("/slugs/my-slug"));
        Assert.That(Fails("slug", Values(("slug", "Bad Slug"))).Code, Is.EqualTo(UrlGenerationErrorCode.ConstraintViolation));
        Assert.That(Fails("doc", Values(("section", ""))).Code, Is.EqualTo(UrlGenerationErrorCode.ConstraintViolation));
    }

    [Test]
    public void An_unknown_parameter_is_rejected_with_the_declared_names()
    {
        var exception = Fails("customer-details", Values(("id", 1), ("extra", 2), ("other", 3)));

        Assert.That(exception.Code, Is.EqualTo(UrlGenerationErrorCode.UnknownParameter));
        Assert.That(exception.Message, Does.Contain("'extra'").And.Contain("'other'").And.Contain("'id'").And.Contain("query"));
    }

    [Test]
    public void An_unknown_route_lists_the_registered_names()
    {
        var exception = Fails("nope");

        Assert.That(exception.Code, Is.EqualTo(UrlGenerationErrorCode.UnknownRoute));
        Assert.That(exception.Message, Does.Contain("No route named 'nope'").And.Contain("customer-details"));
    }

    [Test]
    public void TryGenerateUrl_reports_failure_without_throwing()
    {
        Assert.That(Table.TryGenerateUrl("customer-details", null, null, null, null, out var url, out var error), Is.False);
        Assert.That(url, Is.Null);
        Assert.That(error!.Code, Is.EqualTo(UrlGenerationErrorCode.MissingParameter));

        Assert.That(Table.TryGenerateUrl("customer-details", Values(("id", 5)), null, null, null, out url, out error), Is.True);
        Assert.That(url, Is.EqualTo("/customers/5"));
        Assert.That(error, Is.Null);
    }

    // ---- optional and catch-all -------------------------------------------------------------------------------

    [Test]
    public void Optional_parameters_are_omitted_or_included()
    {
        Assert.That(Table.GenerateUrl("doc", Values(("section", "intro"))), Is.EqualTo("/docs/intro"));
        Assert.That(Table.GenerateUrl("doc", Values(("section", "intro"), ("page", "install"))), Is.EqualTo("/docs/intro/install"));
        Assert.That(Table.GenerateUrl("x"), Is.EqualTo("/x"));
        Assert.That(Table.GenerateUrl("x", Values(("a", "1"))), Is.EqualTo("/x/1"));
        Assert.That(Table.GenerateUrl("x", Values(("a", "1"), ("b", "2"))), Is.EqualTo("/x/1/2"));
    }

    [Test]
    public void An_optional_parameter_cannot_be_supplied_after_an_omitted_one()
    {
        var exception = Fails("x", Values(("b", "2")));

        Assert.That(exception.Code, Is.EqualTo(UrlGenerationErrorCode.OptionalGap));
        Assert.That(exception.Message, Does.Contain("'b'").And.Contain("'a'"));
    }

    [Test]
    public void Catch_all_keeps_slashes_between_encoded_sub_segments()
    {
        Assert.That(Table.GenerateUrl("file", Values(("path", "a/b c"))), Is.EqualTo("/files/a/b%20c"));
        Assert.That(Table.GenerateUrl("file", Values(("path", "a//b"))), Is.EqualTo("/files/a/b"));
        Assert.That(Table.GenerateUrl("file"), Is.EqualTo("/files"));
        Assert.That(Table.GenerateUrl("file", Values(("path", ""))), Is.EqualTo("/files"));
    }

    // ---- encoding and formatting ------------------------------------------------------------------------------

    [TestCase("a/b", "/docs/a%2Fb")]
    [TestCase("café", "/docs/caf%C3%A9")]
    [TestCase("a b", "/docs/a%20b")]
    [TestCase("q?x#y", "/docs/q%3Fx%23y")]
    public void Parameter_values_are_percent_encoded_as_utf8(string value, string expectedUrl)
    {
        Assert.That(Table.GenerateUrl("doc", Values(("section", value))), Is.EqualTo(expectedUrl));
    }

    [Test]
    public void Values_are_formatted_with_the_invariant_culture_and_canonical_forms()
    {
        Assert.That(Table.GenerateUrl("event", Values(("when", new DateTime(2024, 5, 1)))), Is.EqualTo("/events/2024-05-01"));
        Assert.That(Table.GenerateUrl("flag", Values(("on", true))), Is.EqualTo("/flags/true"));
        Assert.That(Table.GenerateUrl("customer-details", Values(("id", 1L))), Is.EqualTo("/customers/1"));
        Assert.That(Table.GenerateUrl("customer-order-details", Values(("id", 1), ("orderId", OrderId))), Does.EndWith("/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77"));
    }

    // ---- query and fragment -----------------------------------------------------------------------------------

    [Test]
    public void Query_keys_are_sorted_collections_repeat_and_nulls_are_omitted()
    {
        var query = new Dictionary<string, object?>
        {
            ["b"] = 2,
            ["a"] = new[] { "x", "y" },
            ["c"] = null,
            ["d"] = "",
            ["e"] = true,
            ["f"] = "a b",
        };

        Assert.That(Table.GenerateUrl("customers", query: query, fragment: "#top"), Is.EqualTo("/customers?a=x&a=y&b=2&d=&e=true&f=a%20b#top"));
    }

    [TestCase("top", "#top")]
    [TestCase("#top", "#top")]
    [TestCase("a b", "#a%20b")]
    [TestCase("", "")]
    [TestCase("#", "")]
    public void Fragments_are_escaped_and_a_leading_hash_is_trimmed(string fragment, string expectedSuffix)
    {
        Assert.That(Table.GenerateUrl("customers", fragment: fragment), Is.EqualTo("/customers" + expectedSuffix));
    }

    [Test]
    public void An_empty_query_adds_nothing()
    {
        Assert.That(Table.GenerateUrl("customers", query: new Dictionary<string, object?>()), Is.EqualTo("/customers"));
        Assert.That(Table.GenerateUrl("customers", query: new Dictionary<string, object?> { ["a"] = null }), Is.EqualTo("/customers"));
    }

    // ---- ambient values ---------------------------------------------------------------------------------------

    [Test]
    public void Ambient_values_fill_missing_parameters_when_supplied()
    {
        var url = Table.GenerateUrl("customer-order-details", Values(("orderId", OrderId)), ambientValues: Values(("id", 42)));

        Assert.That(url, Is.EqualTo("/customers/42/orders/4ec9a2a2-1b9f-47b9-8d1a-6e8b2f2b9d77"));
    }

    [Test]
    public void Explicit_values_beat_ambient_values()
    {
        var url = Table.GenerateUrl("customer-details", Values(("id", 7)), ambientValues: Values(("id", 42)));

        Assert.That(url, Is.EqualTo("/customers/7"));
    }

    [Test]
    public void Without_ambient_values_nothing_is_inherited()
    {
        Assert.That(Fails("customer-order-details", Values(("orderId", OrderId))).Code, Is.EqualTo(UrlGenerationErrorCode.MissingParameter));
    }

    [Test]
    public void Ambient_values_the_target_route_does_not_declare_are_ignored()
    {
        Assert.That(Table.GenerateUrl("customers", ambientValues: Values(("id", 42), ("junk", 1))), Is.EqualTo("/customers"));
    }

    // ---- RouteValues ------------------------------------------------------------------------------------------

    [Test]
    public void RouteValues_converts_anonymous_objects_case_insensitively()
    {
        var values = RouteValues.From(new { id = 42, Name = "x" });

        Assert.That(values["ID"], Is.EqualTo(42));
        Assert.That(values["name"], Is.EqualTo("x"));
        Assert.That(Table.GenerateUrl("customer-details", RouteValues.From(new { id = 42 })), Is.EqualTo("/customers/42"));
    }

    [Test]
    public void RouteValues_passes_dictionaries_through_and_treats_null_as_empty()
    {
        var dictionary = new Dictionary<string, object?> { ["a"] = 1 };

        Assert.That(RouteValues.From(dictionary), Is.SameAs(dictionary));
        Assert.That(RouteValues.From(null), Is.Empty);
    }

    [Test]
    public void RouteValues_copies_any_string_keyed_generic_dictionary_instead_of_reflecting_over_it()
    {
        var strings = RouteValues.From(new Dictionary<string, string> { ["Id"] = "a" });
        var ints = RouteValues.From(new Dictionary<string, int> { ["id"] = 5 });

        Assert.That(strings.Keys, Is.EquivalentTo(new[] { "Id" }));
        Assert.That(strings["id"], Is.EqualTo("a"));
        Assert.That(ints.Keys, Is.EquivalentTo(new[] { "id" }));
        Assert.That(ints["ID"], Is.EqualTo(5));
        Assert.That(Table.GenerateUrl("customer-details", ints), Is.EqualTo("/customers/5"));
    }

    [Test]
    public void RouteValues_copies_a_non_generic_dictionary()
    {
        var values = RouteValues.From(new System.Collections.Hashtable { ["id"] = 7 });

        Assert.That(values.Keys, Is.EquivalentTo(new[] { "id" }));
        Assert.That(values["ID"], Is.EqualTo(7));
    }

    [Test]
    public void RouteValues_rejects_other_enumerables_with_a_clear_message()
    {
        var exception = Assert.Throws<ArgumentException>(() => RouteValues.From(new List<int> { 1, 2 }))!;

        Assert.That(exception.Message, Does.Contain("List"));
    }

    [Test]
    public void RouteValues_still_reads_properties_of_anonymous_objects()
    {
        var values = RouteValues.From(new { id = 1, tab = "x" });

        Assert.That(values.Keys, Is.EquivalentTo(new[] { "id", "tab" }));
    }

    [Test]
    public void An_explicit_null_for_an_undeclared_name_counts_as_absent()
    {
        Assert.That(Table.GenerateUrl("customer-details", RouteValues.From(new { id = 1, tab = (string?)null })), Is.EqualTo("/customers/1"));
        Assert.That(Fails("customer-details", Values(("id", 1), ("tab", "x"))).Code, Is.EqualTo(UrlGenerationErrorCode.UnknownParameter));
    }

    [Test]
    public void An_explicit_null_for_a_declared_name_still_blocks_the_ambient_value()
    {
        Assert.That(
            Fails("customer-details", Values(("id", null)), ambient: Values(("id", 42))).Code,
            Is.EqualTo(UrlGenerationErrorCode.MissingParameter));
    }
}
