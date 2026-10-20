using ModRouter.Core.Compilation;
using ModRouter.Core.Matching;

namespace ModRouter.Test;

[TestFixture]
public sealed class DifferentialTests
{
    [Test]
    public void The_trie_and_the_brute_force_explainer_select_the_same_route()
    {
        var tables = 0;
        var comparisons = 0;
        var matches = 0;

        for (var seed = 1; seed <= 300; seed++)
        {
            var random = new Random(seed);
            var routes = RandomRouteTables.Build(random, out var table);

            if (table is null)
            {
                continue;
            }

            tables++;

            for (var i = 0; i < 150; i++)
            {
                var path = RandomRouteTables.RandomPath(random);
                var match = table.Match(path);
                var explanation = table.Explain(path);
                var expected = match.IsMatch ? match.Chain.MatchedTemplate : null;

                Assert.That(
                    explanation.SelectedTemplate,
                    Is.EqualTo(expected),
                    () => $"seed {seed}, path '{path}', routes: {string.Join(" | ", routes.Select(r => r.Template))}\n{explanation}");

                comparisons++;
                matches += match.IsMatch ? 1 : 0;
            }
        }

        Assert.That(tables, Is.GreaterThan(100), "too few random tables compiled to be a meaningful test");
        Assert.That(matches, Is.GreaterThan(comparisons / 20), "too few paths matched anything to be a meaningful test");
    }

    // The flat generator never produces aliases, index children or multi-level chains; this one does, and it also
    // checks the selected route name so an index entry (same template as its parent) is told apart from the parent.
    [Test]
    public void The_trie_and_the_brute_force_explainer_agree_on_nested_tables_with_aliases_and_index_children()
    {
        var tables = 0;
        var comparisons = 0;
        var matches = 0;
        var aliasMatches = 0;
        var indexMatches = 0;
        var nestedMatches = 0;

        for (var seed = 1; seed <= 300; seed++)
        {
            var random = new Random(seed);
            var table = RandomRouteTables.BuildNested(random, out var description);

            if (table is null)
            {
                continue;
            }

            tables++;

            for (var i = 0; i < 150; i++)
            {
                var path = RandomRouteTables.RandomPath(random, maxSegments: 6);
                var match = table.Match(path);
                var explanation = table.Explain(path);
                var expectedTemplate = match.IsMatch ? match.Chain.MatchedTemplate : null;
                var expectedName = match.IsMatch ? match.Chain.Leaf.RouteName : null;

                Assert.That(
                    (explanation.SelectedTemplate, explanation.SelectedRouteName),
                    Is.EqualTo((expectedTemplate, expectedName)),
                    () => $"seed {seed}, path '{path}', routes:\n{description}\n{explanation}");

                comparisons++;

                if (!match.IsMatch)
                {
                    continue;
                }

                matches++;
                aliasMatches += match.Chain.MatchedViaAlias ? 1 : 0;
                indexMatches += match.Chain.Leaf.Route.IsIndex ? 1 : 0;
                nestedMatches += match.Chain.Matches.Count > 1 ? 1 : 0;
            }
        }

        Assert.That(tables, Is.GreaterThan(100), "too few random tables compiled to be a meaningful test");
        Assert.That(matches, Is.GreaterThan(comparisons / 20), "too few paths matched anything to be a meaningful test");
        Assert.That(aliasMatches, Is.GreaterThan(100), "too few paths matched via an alias to be a meaningful test");
        Assert.That(indexMatches, Is.GreaterThan(100), "too few paths matched an index child to be a meaningful test");
        Assert.That(nestedMatches, Is.GreaterThan(100), "too few paths matched a multi-level chain to be a meaningful test");
    }

    // Case variants of literals, repeated and trailing slashes, percent-encoded and malformed segments, and every constraint
    // kind (typed, refinement, composite, order-swapped, duplicated). Tables that turn out ambiguous are skipped (retried).
    [Test]
    public void The_trie_and_the_explainer_agree_on_widened_tables_and_paths()
    {
        var comparisons = 0;
        var matches = 0;
        var malformed = 0;
        var tables = 0;

        for (var seed = 1; seed <= 400; seed++)
        {
            var random = new Random(seed);
            CompiledRouteTable? table;
            string description;

            if (seed % 2 == 0)
            {
                var routes = RandomRouteTables.Build(random, out table, widened: true);
                description = string.Join(" | ", routes.Select(r => r.Template));
            }
            else
            {
                table = RandomRouteTables.BuildNested(random, out description, widened: true);
            }

            if (table is null)
            {
                continue;
            }

            tables++;

            for (var i = 0; i < 150; i++)
            {
                var path = RandomRouteTables.RandomPath(random, maxSegments: 6, widened: true);
                var match = table.Match(path);
                var explanation = table.Explain(path);
                var expectedTemplate = match.IsMatch ? match.Chain.MatchedTemplate : null;
                var expectedName = match.IsMatch ? match.Chain.Leaf.RouteName : null;

                Assert.That(
                    (explanation.SelectedTemplate, explanation.SelectedRouteName),
                    Is.EqualTo((expectedTemplate, expectedName)),
                    () => $"seed {seed}, path '{path}', routes:\n{description}\n{explanation}");

                comparisons++;
                matches += match.IsMatch ? 1 : 0;
                malformed += match.Failure == RouteMatchFailure.MalformedPath ? 1 : 0;
            }
        }

        Assert.That(tables, Is.GreaterThan(100), "too few widened tables compiled to be a meaningful test");
        Assert.That(matches, Is.GreaterThan(comparisons / 20), "too few widened paths matched anything");
        Assert.That(malformed, Is.GreaterThan(comparisons / 50), "too few widened paths were malformed (the encoded variants are not reaching the matcher)");
    }
}
