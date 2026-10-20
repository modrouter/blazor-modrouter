using ModRouter.Core.Compilation;

namespace ModRouter.Test;

/// <summary>
/// A guard against allocation regressions on the match path, not a performance contract: bytes allocated by one
/// <c>Match</c> on the current thread after warmup, with headroom. The budgets come from the slice 4d benchmarks.
/// </summary>
[TestFixture]
public sealed class MatchAllocationTests
{
    const int WarmUps = 2000;
    const int Runs = 500;

    static readonly CompiledRouteTable Table = RouteTestKit.Compile(map => map
        .Route("/", typeof(HomePage), name: "home")
        .Route("/s1", typeof(CustomersPage), name: "s1", children: section => section
            .Index(typeof(CustomerList))
            .Route("new", typeof(OtherPage))
            .Route("by/{slug}", typeof(OtherPage))
            .Route("{id:int}", typeof(CustomerDetails), children: detail => detail.Route("edit", typeof(OtherPage)))));

    // Measured after slice 4d (Debug and Release alike): 472, 776, 224 and 816 bytes. Each budget is the measured figure
    // plus up to 10% (static has only 8 bytes of headroom, held at the spike target), and never above the spike target
    // (static 480, parameter 900, miss 260, encoded 1,000). Each path must still hit or miss as intended, or a regression
    // that made it miss early would pass with fewer bytes.
    [TestCase("/s1/new", true, 480)]
    [TestCase("/s1/42", true, 850)]
    [TestCase("/s1/nope/x/y", false, 245)]
    [TestCase("/s1/by/hello%20w%C3%B6rld", true, 900)]
    public void A_match_stays_within_its_allocation_budget(string path, bool matches, long budget)
    {
        Assert.That(Table.Match(path).IsMatch, Is.EqualTo(matches), $"'{path}' must {(matches ? "match" : "miss")}");

        for (var i = 0; i < WarmUps; i++)
        {
            Table.Match(path);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var i = 0; i < Runs; i++)
        {
            Table.Match(path);
        }

        var perMatch = (GC.GetAllocatedBytesForCurrentThread() - before) / Runs;

        Assert.That(perMatch, Is.LessThanOrEqualTo(budget), $"'{path}' allocated {perMatch} bytes per match");
    }

    [Test]
    public void A_nested_chain_still_merges_ancestor_parameters_and_keeps_each_levels_own()
    {
        var table = RouteTestKit.Compile(map => map
            .Route("/o/{org}", typeof(HomePage), name: "org", children: org => org
                .Route("p/{project:int}", typeof(OtherPage), name: "project")));

        var chain = RouteTestKit.Match(table, "/o/acme/p/7");

        Assert.That(chain.Matches[0].Parameters.Values.Keys, Is.EquivalentTo(new[] { "org" }));
        Assert.That(chain.Matches[1].Parameters.Values.Keys, Is.EquivalentTo(new[] { "project" }));
        Assert.That(chain.Matches[1].InheritedParameters.Values.Keys, Is.EquivalentTo(new[] { "org", "project" }));
        Assert.That(chain.Matches[1].InheritedParameters["project"], Is.EqualTo(7));
        Assert.That(chain.Matches[0].InheritedParameters["org"], Is.EqualTo("acme"));
    }
}
