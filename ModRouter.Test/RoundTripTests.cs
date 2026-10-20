using ModRouter.Core.Constraints;
using ModRouter.Core.Matching;

namespace ModRouter.Test;

[TestFixture]
public sealed class RoundTripTests
{
    [Test]
    public void Generated_urls_match_their_own_route_with_equivalent_values()
    {
        var verified = 0;
        var shadowed = 0;

        for (var seed = 1; seed <= 300; seed++)
        {
            var random = new Random(seed);
            var routes = RandomRouteTables.Build(random, out var table);

            if (table is null)
            {
                continue;
            }

            foreach (var route in routes)
            {
                var values = RandomRouteTables.ValuesFor(route, random, out var expected);
                var url = table.GenerateUrl(route.Name, values);
                var template = table.Routes.Single(e => e.Definition.Name == route.Name && !e.IsAlias).Template;

                var explanation = table.Explain(url);
                Assert.That(
                    explanation.Candidates.Any(c => c.Template == template && c.IsMatch),
                    Is.True,
                    () => $"seed {seed}: '{url}' generated for {template} does not match that route.\n{explanation}");

                var match = table.Match(url);
                Assert.That(match.IsMatch, Is.True, () => $"seed {seed}: '{url}' generated for {template} matched nothing.");

                var chain = match.Chain!;

                if (chain.MatchedTemplate != template)
                {
                    var target = explanation.Candidates.First(c => c.Template == template && c.IsMatch);

                    Assert.That(target.Verdict, Is.EqualTo(RouteCandidateVerdict.LostOnPrecedence), () => $"seed {seed}: '{url}' for {template} is shadowed by {chain.MatchedTemplate}, but the explanation does not say it lost on precedence.\n{explanation}");
                    Assert.That(target.PrecedenceReason, Is.Not.Empty);
                    Assert.That(explanation.SelectedTemplate, Is.EqualTo(chain.MatchedTemplate));
                    shadowed++;

                    continue;
                }

                var actual = chain.Leaf.InheritedParameters;
                Assert.That(actual.Count, Is.EqualTo(expected.Count), () => $"seed {seed}: '{url}' parameter count");

                foreach (var (name, text) in expected)
                {
                    Assert.That(
                        ValueFormatter.Format(actual[name]!),
                        Is.EqualTo(text),
                        () => $"seed {seed}: '{url}' parameter '{name}'");
                }

                verified++;
            }
        }

        Assert.That(verified, Is.GreaterThan(500), "too few round trips reached the value comparison to be a meaningful test");
        Assert.That(shadowed, Is.GreaterThan(0), "no round trip was shadowed, so the shadow check is vacuous");
    }

    [Test]
    public void Unicode_and_reserved_characters_round_trip()
    {
        var table = RouteTestKit.Flat(("/people/{name}/files/{*path}", "person"));
        var values = new Dictionary<string, object?> { ["name"] = "José / Müller?#", ["path"] = "dir one/é.txt" };

        var url = table.GenerateUrl("person", values);
        var chain = RouteTestKit.Match(table, url);

        Assert.That(chain.Leaf.Parameters.Get<string>("name"), Is.EqualTo("José / Müller?#"));
        Assert.That(chain.Leaf.Parameters.Get<string>("path"), Is.EqualTo("dir one/é.txt"));
    }

    [Test]
    public void A_url_can_be_shadowed_by_a_more_specific_route_and_Explain_shows_why()
    {
        var table = RouteTestKit.Flat(("/customers/{id}", "by-id"), ("/customers/new", "new"));

        var url = table.GenerateUrl("by-id", new Dictionary<string, object?> { ["id"] = "new" });

        Assert.That(url, Is.EqualTo("/customers/new"));
        Assert.That(RouteTestKit.Leaf(table, url), Is.EqualTo("new"));
        Assert.That(table.Explain(url).Candidates.Single(c => c.RouteName == "by-id").IsMatch, Is.True);
    }
}
