using ModRouter.Core.Compilation;
using ModRouter.Core.Generation;
using ModRouter.Core.Syntax;

namespace ModRouter.Test;

/// <summary>
/// URL generation across a route's templates: when the supplied values cannot build the route's own template, the
/// aliases are tried in order and the first one they satisfy is used (a multi-<c>@page</c> component lists the
/// parameterless template first). A generation the canonical template can satisfy never changes.
/// </summary>
[TestFixture]
public sealed class UrlGenerationFallbackTests
{
    static readonly CompiledRouteTable Table = RouteTestKit.Compile(map => map
        .Route("/org-chart", typeof(OtherPage), name: "org", aliases: ["/org-chart/{employeeId:int}"])
        .Route("/people/{id:int}", typeof(OtherPage), name: "person", aliases: ["/people"])
        .Route("/docs/{section}/{page}", typeof(OtherPage), name: "doc", aliases: ["/docs/{section}", "/docs"])
        .Route("/customers", typeof(CustomersPage), name: "customers", aliases: ["/clients"], children: customers => customers
            .Route("{id:int}", typeof(CustomerDetails), name: "customer", aliases: ["all"]))
        .Route("/vendors/{id:int}", typeof(OtherPage), name: "vendor", aliases: ["/vendors/{id:alpha}"]).CanonicalAlias()
        .Route("/items/{id:int}", typeof(OtherPage), name: "item", aliases: ["/items/{id:alpha}"]));

    static Dictionary<string, object?> Values(params (string Name, object? Value)[] values)
        => values.ToDictionary(v => v.Name, v => v.Value);

    [Test]
    public void A_parameterless_first_template_still_generates_the_richer_alias_when_values_are_supplied()
    {
        Assert.That(Table.GenerateUrl("org"), Is.EqualTo("/org-chart"));
        Assert.That(Table.GenerateUrl("org", Values(("employeeId", 5))), Is.EqualTo("/org-chart/5"));
    }

    [Test]
    public void A_parameterized_first_template_falls_back_to_the_parameterless_alias()
    {
        Assert.That(Table.GenerateUrl("person", Values(("id", 5))), Is.EqualTo("/people/5"));
        Assert.That(Table.GenerateUrl("person"), Is.EqualTo("/people"));
    }

    [Test]
    public void Aliases_are_tried_in_declaration_order_and_the_first_satisfied_wins()
    {
        Assert.That(Table.GenerateUrl("doc", Values(("section", "intro"), ("page", "setup"))), Is.EqualTo("/docs/intro/setup"));
        Assert.That(Table.GenerateUrl("doc", Values(("section", "intro"))), Is.EqualTo("/docs/intro"));
        Assert.That(Table.GenerateUrl("doc"), Is.EqualTo("/docs"));
    }

    [Test]
    public void Ambient_values_count_for_an_alias_as_they_do_for_the_canonical_template()
    {
        Assert.That(Table.GenerateUrl("doc", null, ambientValues: Values(("section", "intro"))), Is.EqualTo("/docs/intro"));
        Assert.That(Table.GenerateUrl("org", null, ambientValues: Values(("employeeId", 7))), Is.EqualTo("/org-chart"), "the canonical template is satisfied, so nothing falls back");
    }

    [Test]
    public void Only_the_routes_own_aliases_under_the_canonical_ancestor_path_are_candidates()
    {
        Assert.That(Table.GenerateUrl("customer", Values(("id", 3))), Is.EqualTo("/customers/3"));
        Assert.That(Table.GenerateUrl("customer"), Is.EqualTo("/customers/all"), "the parent's alias (/clients/all) is never chosen");
    }

    [Test]
    public void A_canonical_alias_route_never_falls_back()
    {
        var exception = Assert.Throws<RouteUrlGenerationException>(() => Table.GenerateUrl("vendor", Values(("id", "abc"))))!;

        Assert.That(exception.Code, Is.EqualTo(UrlGenerationErrorCode.ConstraintViolation));
        Assert.That(Table.GenerateUrl("item", Values(("id", "abc"))), Is.EqualTo("/items/abc"), "the same shape without CanonicalAlias does fall back");
    }

    [Test]
    public void When_no_template_is_satisfied_the_canonical_templates_error_is_reported()
    {
        var exception = Assert.Throws<RouteUrlGenerationException>(() => Table.GenerateUrl("org", Values(("employeeId", 5), ("extra", 1))))!;

        Assert.That(exception.Code, Is.EqualTo(UrlGenerationErrorCode.UnknownParameter));
        Assert.That(exception.Message, Does.Contain("'/org-chart'").And.Contain("'employeeId', 'extra'"));

        Assert.That(Table.TryGenerateUrl("person", Values(("id", "x")), null, null, null, out _, out var error), Is.False);
        Assert.That(error!.Code, Is.EqualTo(UrlGenerationErrorCode.ConstraintViolation));
        Assert.That(error.Message, Does.Contain("'/people/{id:int}'"));
    }

    [Test]
    public void The_query_and_fragment_are_appended_to_the_alias_url_too()
    {
        Assert.That(Table.GenerateUrl("person", null, Values(("tab", "a")), "top"), Is.EqualTo("/people?tab=a#top"));
    }

    // ---- property: the fallback never changes a generation that succeeds on the canonical template --------------

    [Test]
    public void A_generation_the_canonical_template_satisfies_is_unchanged_and_a_fallback_url_matches_its_own_route()
    {
        var unchanged = 0;
        var fallbacks = 0;

        for (var seed = 1; seed <= 300; seed++)
        {
            var random = new Random(seed);
            var table = RandomRouteTables.BuildNested(random, out var description);

            if (table is null)
            {
                continue;
            }

            foreach (var canonical in table.Routes.Where(r => !r.IsAlias && r.Definition.Name is not null))
            {
                var name = canonical.Definition.Name!;

                // Values shaped for the canonical template and for each of the route's own entries, so both paths run.
                foreach (var shaped in table.Routes.Where(r => ReferenceEquals(r.Definition, canonical.Definition)))
                {
                    var values = ValuesFor(shaped, random);
                    var before = UrlGenerator.TryGenerate(canonical, values, null, null, null, out var expected, out _);
                    var after = table.TryGenerateUrl(name, values, null, null, null, out var url, out _);

                    if (before)
                    {
                        Assert.That(after, Is.True, () => $"seed {seed}, route {name}: the canonical generation must still succeed\n{description}");
                        Assert.That(url, Is.EqualTo(expected), () => $"seed {seed}, route {name}: a canonical generation changed\n{description}");
                        unchanged++;
                    }
                    else if (after)
                    {
                        // Like the round-trip test: the URL is matched by one of the route's own templates (another route
                        // may shadow it with a more specific template, which is the table's business, not the generator's;
                        // a parent with an index child is reported under the index's name but its own template).
                        var explanation = table.Explain(url!);
                        var ownTemplates = table.Routes.Where(r => ReferenceEquals(r.Definition, canonical.Definition)).Select(r => r.Template).ToList();
                        Assert.That(explanation.Candidates.Any(c => c.IsMatch && ownTemplates.Contains(c.Template)), Is.True, () => $"seed {seed}, route {name}: the fallback URL '{url}' is not matched by its own route\n{description}\n{explanation}");
                        fallbacks++;
                    }
                }
            }
        }

        Assert.That(unchanged, Is.GreaterThan(1000), "too few canonical generations to be a meaningful test");
        Assert.That(fallbacks, Is.GreaterThan(100), "too few fallbacks to be a meaningful test");
    }

    /// <summary>Values that satisfy <paramref name="entry"/>: every required parameter, a prefix of the optionals, maybe the catch-all.</summary>
    static Dictionary<string, object?> ValuesFor(CompiledRoute entry, Random random)
    {
        var values = new Dictionary<string, object?>();
        var skipOptionals = random.Next(2) == 0;

        foreach (var segment in entry.Segments.Where(s => s.IsParameter))
        {
            if (segment.Kind == TemplateSegmentKind.OptionalParameter && (skipOptionals || random.Next(3) == 0))
            {
                skipOptionals = true;

                continue;
            }

            if (segment.Kind == TemplateSegmentKind.CatchAll && random.Next(2) == 0)
            {
                continue;
            }

            values[segment.Text] = segment.ConstraintKey switch
            {
                "int" => random.Next(1, 999),
                "int:min(10)" => random.Next(10, 999),
                "alpha" => new string(Enumerable.Range(0, random.Next(3, 7)).Select(_ => (char)('a' + random.Next(26))).ToArray()),
                _ => $"q{random.Next(100, 999)}z",
            };
        }

        return values;
    }
}
