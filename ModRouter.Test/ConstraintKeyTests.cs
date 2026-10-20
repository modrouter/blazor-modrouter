using ModRouter.Core.Compilation;
using ModRouter.Core.Constraints;

namespace ModRouter.Test;

/// <summary>
/// Equivalent constraint lists are one trie edge, so RT0012 reports them; lists that define the converted value
/// differently stay separate edges.
/// </summary>
[TestFixture]
public sealed class ConstraintKeyTests
{
    [TestCase("/a/{x:int:min(1)}", "/a/{y:min(1):int}")]
    [TestCase("/a/{x:int()}", "/a/{y:int}")]
    [TestCase("/a/{x:int:int}", "/a/{y:int}")]
    [TestCase("/a/{x:min(1):max(9)}", "/a/{y:max(9):min(1)}")]
    [TestCase("/a/{x:alpha:alpha}", "/a/{y:ALPHA}")]
    public void Equivalent_constraint_lists_are_ambiguous(string first, string second)
    {
        var error = Assert.Throws<RouteCompilationException>(
            () => RouteTestKit.Compile(map => map.Route(first, typeof(HomePage), name: "first").Route(second, typeof(OtherPage), name: "second")));

        Assert.That(error!.Errors.Select(e => e.Code), Does.Contain("RT0012"));
    }

    [TestCase("/a/{x:int:guid}", "/a/{y:guid:int}")]
    [TestCase("/a/{x:alpha:min(1)}", "/a/{y:min(1):alpha}")]
    [TestCase("/a/{x:int}", "/a/{y:long}")]
    public void Lists_that_define_the_value_differently_stay_separate_edges(string first, string second)
    {
        var table = RouteTestKit.Compile(map => map.Route(first, typeof(HomePage), name: "first").Route(second, typeof(OtherPage), name: "second"));

        Assert.That(table.Routes, Has.Count.EqualTo(2));
    }

    [Test]
    public void A_canonical_edge_keeps_the_converted_type_of_the_typed_part_whatever_the_order()
    {
        var intFirst = RouteTestKit.Compile(map => map.Route("/n/{x:int:min(1)}", typeof(HomePage), name: "n"));
        var intLast = RouteTestKit.Compile(map => map.Route("/n/{x:min(1):int}", typeof(HomePage), name: "n"));
        var bounds = RouteTestKit.Compile(map => map.Route("/n/{x:max(9):min(1)}", typeof(HomePage), name: "n"));

        Assert.That(RouteTestKit.Match(intFirst, "/n/5").Leaf.Parameters["x"], Is.EqualTo(5).And.TypeOf<int>());
        Assert.That(RouteTestKit.Match(intLast, "/n/5").Leaf.Parameters["x"], Is.EqualTo(5).And.TypeOf<int>());
        Assert.That(RouteTestKit.Match(bounds, "/n/5").Leaf.Parameters["x"], Is.EqualTo(5L).And.TypeOf<long>());
    }

    [Test]
    public void Explain_ranks_equivalent_edges_the_same_way_the_trie_does()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/b/{x:int:min(1)}", typeof(HomePage), name: "typed")
            .Route("/b/{y}", typeof(OtherPage), name: "plain"));

        var explanation = table.Explain("/b/5");

        Assert.That(explanation.SelectedRouteName, Is.EqualTo("typed"));
        Assert.That(RouteTestKit.Leaf(table, "/b/5"), Is.EqualTo("typed"));
    }

    [Test]
    public void A_precedence_reason_shows_the_readable_canonical_key()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/k/{x:min(1):int}", typeof(HomePage), name: "min")
            .Route("/k/{y:int:max(9)}", typeof(OtherPage), name: "max"));

        var loser = table.Explain("/k/5").Candidates.Single(c => c.RouteName == "min");

        Assert.That(
            loser.PrecedenceReason,
            Is.EqualTo("At segment 2 (\"5\") the selected route /k/{y:int:max(9)} has the constraint 'int:max(9)', which sorts before 'int:min(1)'."));
    }

    // Two built-in refinement-only lists with the same parts that both accept "5" but convert it differently (long against
    // string): each route keeps its own conversion, and Explain names what tells the two keys apart.
    [Test]
    public void Built_in_lists_that_accept_the_same_text_keep_their_own_conversion()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/k/{x:min(1):length(1)}/a", typeof(HomePage), name: "long")
            .Route("/k/{y:length(1):min(1)}/b", typeof(OtherPage), name: "string"));

        Assert.That(RouteTestKit.Match(table, "/k/5/a").Leaf.Parameters["x"], Is.EqualTo(5L).And.TypeOf<long>());
        Assert.That(RouteTestKit.Match(table, "/k/5/b").Leaf.Parameters["y"], Is.EqualTo("5").And.TypeOf<string>());
    }

    [Test]
    public void A_precedence_reason_between_keys_that_read_the_same_names_what_defines_the_value()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/k/{x:min(1):length(1)}", typeof(HomePage), name: "long")
            .Route("/k/{y:length(1):min(1)}", typeof(OtherPage), name: "string"));

        var loser = table.Explain("/k/5").Candidates.Single(c => c.RouteName == "string");

        Assert.That(
            loser.PrecedenceReason,
            Is.EqualTo(
                "At segment 2 (\"5\") the selected route /k/{x:min(1):length(1)} has the constraint 'length(1):min(1)' with its "
                + "value defined by 'System.Int64', which sorts before the same constraints with the value defined by 'System.String'."));
    }

    // Built-in refinements of one value type convert the same text the same way, but a custom refinement may convert it
    // its own way: two refinement-only lists led by different custom refinements must stay separate edges, so each
    // route converts with its own leading part.
    [Test]
    public void Custom_refinement_only_lists_led_by_different_parts_convert_with_their_own_part()
    {
        var registry = new ConstraintRegistry();
        registry.Register("up", _ => new CaseConverting(upper: true));
        registry.Register("low", _ => new CaseConverting(upper: false));
        var table = RouteTestKit.Compile(
            map => map
                .Route("/c/{x:up:low}/a", typeof(HomePage), name: "a")
                .Route("/c/{y:low:up}/b", typeof(OtherPage), name: "b"),
            registry);

        Assert.That(RouteTestKit.Match(table, "/c/Ab/a").Leaf.Parameters["x"], Is.EqualTo("AB"));
        Assert.That(RouteTestKit.Match(table, "/c/Ab/b").Leaf.Parameters["y"], Is.EqualTo("ab"));
    }

    // Whitespace inside an argument is passed to the factory as written (a regex or a custom constraint may care), so it is
    // part of the key: these two lists stay separate edges (a known limitation).
    [Test]
    public void Whitespace_inside_an_argument_keeps_lists_apart()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/w/{x:int:min(1)}", typeof(HomePage), name: "first")
            .Route("/w/{y:int:min( 1 )}", typeof(OtherPage), name: "second"));

        Assert.That(table.Routes, Has.Count.EqualTo(2));
    }

    sealed class CaseConverting(bool upper) : IRefinementConstraint
    {
        public Type ValueType => typeof(string);

        public bool TryMatch(string rawValue, out object? convertedValue)
        {
            convertedValue = upper ? rawValue.ToUpperInvariant() : rawValue.ToLowerInvariant();

            return true;
        }

        public bool TryFormat(object value, out string text)
        {
            text = value.ToString() ?? string.Empty;

            return true;
        }
    }
}
