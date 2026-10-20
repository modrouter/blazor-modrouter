using System.Reflection;
using System.Text.RegularExpressions;

namespace ModRouter.Test;

/// <summary>
/// The compatibility matrix in <c>docs/compatibility.md</c> is only as honest as its tests: every row marked <c>supported</c> names at least one
/// test method of this assembly (<c>Fixture.Method</c>), every named test exists, and a <c>not handled</c> row names none.
/// </summary>
[TestFixture]
public sealed class CompatibilityMatrixTests
{
    static readonly string[] Statuses = ["supported", "difference", "not handled"];

    sealed record Row(string Behavior, string Status, IReadOnlyList<string> Tests);

    static IReadOnlyList<Row> Rows()
    {
        var lines = RouterDocs.ReadLines("docs/compatibility.md");
        var start = Array.FindIndex(lines, line => line.StartsWith("### Compatibility matrix", StringComparison.Ordinal));
        Assert.That(start, Is.GreaterThanOrEqualTo(0), "docs/compatibility.md has a '### Compatibility matrix' heading");

        var header = Array.FindIndex(lines, start, line => line.StartsWith("| Behavior", StringComparison.Ordinal));
        Assert.That(header, Is.GreaterThan(start), "the matrix starts with a '| Behavior | Status | Test |' header");

        var rows = new List<Row>();

        for (var i = header + 2; i < lines.Length && lines[i].StartsWith('|'); i++)
        {
            var cells = lines[i].Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            Assert.That(cells, Has.Length.EqualTo(3), $"row {i + 1}: three cells");

            var tests = Regex.Matches(cells[2], @"`([A-Za-z0-9_]+\.[A-Za-z0-9_]+)`").Select(m => m.Groups[1].Value).ToList();
            rows.Add(new Row(cells[0], cells[1], tests));
        }

        return rows;
    }

    static string StatusOf(Row row) => Statuses.FirstOrDefault(s => row.Status.StartsWith(s, StringComparison.Ordinal)) ?? row.Status;

    static bool TestExists(string reference)
    {
        var (fixture, method) = reference.Split('.') is [var f, var m] ? (f, m) : (reference, string.Empty);
        var type = typeof(CompatibilityMatrixTests).Assembly.GetTypes().SingleOrDefault(t => t.Name == fixture && t.IsDefined(typeof(TestFixtureAttribute), inherit: false));

        return type?.GetMethod(method, BindingFlags.Public | BindingFlags.Instance) is { } found
            && (found.IsDefined(typeof(TestAttribute), inherit: false) || found.IsDefined(typeof(TestCaseAttribute), inherit: false) || found.IsDefined(typeof(TestCaseSourceAttribute), inherit: false));
    }

    [Test]
    public void The_matrix_has_one_row_per_behavior_with_a_known_status()
    {
        var rows = Rows();

        Assert.That(rows, Has.Count.GreaterThanOrEqualTo(25), "the matrix covers the Router, RouteView and AuthorizeRouteView parameters, templates, constraints, OnNavigateAsync, assemblies, authorization, Navigating, NotFound, OnNotFound, layouts and query binding");
        Assert.That(rows.Select(StatusOf), Is.All.AnyOf(Statuses), string.Join(", ", rows.Where(r => !Statuses.Contains(StatusOf(r))).Select(r => $"{r.Behavior}: '{r.Status}'")));
        Assert.That(rows.Select(r => r.Behavior).Distinct().ToList(), Has.Count.EqualTo(rows.Count), "no behavior twice");
        Assert.That(rows.Count(r => StatusOf(r) == "supported"), Is.GreaterThanOrEqualTo(15));
        Assert.That(rows.Count(r => StatusOf(r) == "difference"), Is.GreaterThanOrEqualTo(5), "the deliberate differences are listed as such");
        Assert.That(rows.Count(r => StatusOf(r) == "not handled"), Is.GreaterThanOrEqualTo(1), "OnNotFound is documented as not handled");
    }

    [Test]
    public void Every_supported_row_names_at_least_one_test()
    {
        var untested = Rows().Where(r => StatusOf(r) == "supported" && r.Tests.Count == 0).Select(r => r.Behavior).ToList();

        Assert.That(untested, Is.Empty, "a row is marked supported only if a test names it: " + string.Join("; ", untested));
    }

    [Test]
    public void Every_named_test_exists_in_this_assembly()
    {
        var missing = Rows().SelectMany(r => r.Tests).Distinct().Where(t => !TestExists(t)).ToList();

        Assert.That(missing, Is.Empty, "docs/compatibility.md names a test that does not exist (renamed?): " + string.Join(", ", missing));
        Assert.That(TestExists("CompatibilityMatrixTests.Every_named_test_exists_in_this_assembly"), Is.True, "the lookup itself works");
        Assert.That(TestExists("CompatibilityMatrixTests.No_such_test"), Is.False);
    }

    [Test]
    public void A_not_handled_row_names_no_test()
    {
        var contradictory = Rows().Where(r => StatusOf(r) == "not handled" && r.Tests.Count > 0).Select(r => r.Behavior).ToList();

        Assert.That(contradictory, Is.Empty, string.Join("; ", contradictory));
    }
}
