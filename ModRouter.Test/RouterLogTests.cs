using System.Reflection;
using System.Text.RegularExpressions;
using ModRouter.Diagnostics;
using ModRouter.Navigation;
using Microsoft.Extensions.Logging;

namespace ModRouter.Test;

/// <summary>
/// Every router log event is a <c>[LoggerMessage]</c> method of <see cref="RouterLog"/> with a stable id inside its area's
/// range, a fixed level and named placeholders; the <c>### Log events</c> table in <c>docs/observability.md</c> is compared with the code.
/// </summary>
[TestFixture]
public sealed class RouterLogTests
{
    /// <summary>The areas and their id ranges (inclusive), as the observability page states them.</summary>
    static readonly Dictionary<string, (int From, int To)> Areas = new(StringComparer.Ordinal)
    {
        ["Lifecycle"] = (1000, 1099),
        ["Stages"] = (1100, 1199),
        ["Guards and loaders"] = (1200, 1299),
        ["Redirects"] = (1300, 1399),
        ["Configuration and markup"] = (1400, 1499),
        ["Re-check and authentication"] = (1500, 1599),
        ["OnNavigateAsync and compatibility"] = (1600, 1699),
        ["Effects and JavaScript"] = (1700, 1799),
        ["Subscribers and diagnostics"] = (1800, 1899),
    };

    /// <summary>One event: what the code must declare and how to fire it once.</summary>
    public sealed record Event(int Id, string Name, LogLevel Level, string[] Placeholders, string Area, Action<ILogger> Fire)
    {
        public override string ToString() => $"{Id} {Name}";
    }

    static readonly InvalidOperationException Boom = new("boom");

    public static IEnumerable<Event> Events()
    {
        yield return new(1000, "NavigationRequested", LogLevel.Debug, ["NavigationId", "Target", "Trigger"], "Lifecycle", l => RouterLog.NavigationRequested(l, 7, "customers/1", NavigationTrigger.Link));
        yield return new(1001, "NavigationCommitted", LogLevel.Debug, ["NavigationId"], "Lifecycle", l => RouterLog.NavigationCommitted(l, 7));
        yield return new(1002, "NavigationSuperseded", LogLevel.Debug, ["NavigationId"], "Lifecycle", l => RouterLog.NavigationSuperseded(l, 7));
        yield return new(1003, "NavigationCanceled", LogLevel.Debug, ["NavigationId"], "Lifecycle", l => RouterLog.NavigationCanceled(l, 7));
        yield return new(1004, "NavigationFailed", LogLevel.Error, ["NavigationId", "Target"], "Lifecycle", l => RouterLog.NavigationFailed(l, Boom, 7, "customers/1"));
        yield return new(1005, "FallbackNavigationFailed", LogLevel.Error, ["Location"], "Lifecycle", l => RouterLog.FallbackNavigationFailed(l, Boom, "http://localhost/x"));
        yield return new(1006, "LocationChangedWithoutPreparedNavigation", LogLevel.Warning, ["Location"], "Lifecycle", l => RouterLog.LocationChangedWithoutPreparedNavigation(l, "http://localhost/x"));
        yield return new(1007, "UnparseableLocation", LogLevel.Warning, ["Location"], "Lifecycle", l => RouterLog.UnparseableLocation(l, "http://[bad"));
        yield return new(1100, "NavigationCanceledByStage", LogLevel.Debug, ["NavigationId", "Stage"], "Stages", l => RouterLog.NavigationCanceledByStage(l, 7, "GuardStage"));
        yield return new(1101, "ParameterBindingFailed", LogLevel.Warning, ["NavigationId", "Path", "Error"], "Stages", l => RouterLog.ParameterBindingFailed(l, 7, "/customers/1", "bad"));
        yield return new(1102, "AliasRenderedInPlace", LogLevel.Debug, ["Path", "Template"], "Stages", l => RouterLog.AliasRenderedInPlace(l, "/people", "/people/{id:int}"));
        yield return new(1200, "GuardFailed", LogLevel.Error, ["NavigationId", "Message", "Level"], "Guards and loaders", l => RouterLog.GuardFailed(l, Boom, 7, "The guard threw", 1));
        yield return new(1201, "LoaderFailed", LogLevel.Error, ["NavigationId", "Level"], "Guards and loaders", l => RouterLog.LoaderFailed(l, Boom, 7, 1));
        yield return new(1300, "RedirectLoop", LogLevel.Error, ["NavigationId", "Path"], "Redirects", l => RouterLog.RedirectLoop(l, 7, "/a -> /b -> /a"));
        yield return new(1400, "MarkupRoutesCompileFailed", LogLevel.Error, [], "Configuration and markup", l => RouterLog.MarkupRoutesCompileFailed(l, Boom));
        yield return new(1401, "MarkupEntryParametersChanged", LogLevel.Debug, ["Registered", "Current"], "Configuration and markup", l => RouterLog.MarkupEntryParametersChanged(l, "a", "b"));
        yield return new(1410, "OutletNotRendered", LogLevel.Warning, ["RouteName", "Template", "Component", "Depth"], "Configuration and markup", l => RouterLog.OutletNotRendered(l, "none", "/none", "App.Pages.Parent", 1));
        yield return new(1500,"AuthorizationRechecked", LogLevel.Debug, ["Path", "After", "Before"], "Re-check and authentication", l => RouterLog.AuthorizationRechecked(l, "/admin", "Allow", "NotAuthorized@0"));
        yield return new(1600, "OnNavigateCanceled", LogLevel.Debug, ["NavigationId"], "OnNavigateAsync and compatibility", l => RouterLog.OnNavigateCanceled(l, 7));
        yield return new(1601, "OnNavigateThrew", LogLevel.Error, ["NavigationId"], "OnNavigateAsync and compatibility", l => RouterLog.OnNavigateThrew(l, Boom, 7));
        yield return new(1602, "CompatAssembliesChanged", LogLevel.Warning, ["Before", "After"], "OnNavigateAsync and compatibility", l => RouterLog.CompatAssembliesChanged(l, "A", "A, B"));
        yield return new(1700, "EffectsFailed", LogLevel.Warning, [], "Effects and JavaScript", l => RouterLog.EffectsFailed(l, Boom));
        yield return new(1701, "JsModuleLoadFailed", LogLevel.Warning, [], "Effects and JavaScript", l => RouterLog.JsModuleLoadFailed(l, Boom));
        yield return new(1800, "StateSubscriberThrew", LogLevel.Error, [], "Subscribers and diagnostics", l => RouterLog.StateSubscriberThrew(l, Boom));
        yield return new(1801, "DiagnosticsSubscriberThrew", LogLevel.Error, [], "Subscribers and diagnostics", l => RouterLog.DiagnosticsSubscriberThrew(l, Boom));
    }

    static IReadOnlyList<(MethodInfo Method, LoggerMessageAttribute Attribute)> Declared()
        => [.. typeof(RouterLog).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => (method, attribute: method.GetCustomAttribute<LoggerMessageAttribute>()))
            .Where(pair => pair.attribute is not null)
            .Select(pair => (pair.method, pair.attribute!))];

    static IReadOnlyList<string> TemplatePlaceholders(string template)
        => [.. Regex.Matches(template, @"\{([A-Za-z]+)\}").Select(match => match.Groups[1].Value)];

    [TestCaseSource(nameof(Events))]
    public void Each_event_is_logged_once_with_its_id_name_level_and_placeholders(Event expected)
    {
        using var capture = new LogCapture();
        var logger = capture.CreateLogger("Test");

        expected.Fire(logger);

        Assert.That(capture.Entries, Has.Count.EqualTo(1));
        var entry = capture.Entries[0];
        Assert.Multiple(() =>
        {
            Assert.That(entry.EventId.Id, Is.EqualTo(expected.Id));
            Assert.That(entry.EventId.Name, Is.EqualTo(expected.Name));
            Assert.That(entry.Level, Is.EqualTo(expected.Level));
            Assert.That(entry.Placeholders, Is.EqualTo(expected.Placeholders));
            Assert.That(entry.State.Any(pair => pair.Key == "{OriginalFormat}"), Is.True, "structured, not a preformatted string");
        });
    }

    [Test]
    public void Every_event_has_a_unique_id_inside_its_areas_range_and_its_method_name_as_event_name()
    {
        var declared = Declared();
        var expected = Events().ToDictionary(e => e.Name, StringComparer.Ordinal);

        Assert.That(declared, Has.Count.GreaterThanOrEqualTo(21));
        Assert.That(declared.Select(d => d.Attribute.EventId).Distinct().Count(), Is.EqualTo(declared.Count), "the generator does not report duplicate ids");
        Assert.Multiple(() =>
        {
            foreach (var (method, attribute) in declared)
            {
                Assert.That(attribute.EventName, Is.EqualTo(method.Name), $"{method.Name}: EventName is the method name");
                Assert.That(expected.TryGetValue(method.Name, out var row), Is.True, $"{method.Name} is not in the expected event table (add it, with its area)");

                if (row is null)
                {
                    continue;
                }

                var (from, to) = Areas[row.Area];
                Assert.That(attribute.EventId, Is.InRange(from, to), $"{method.Name} ({attribute.EventId}) lies outside the {row.Area} range {from}-{to}");
                Assert.That(attribute.EventId, Is.EqualTo(row.Id), $"{method.Name}: a stable id");
                Assert.That(attribute.Level, Is.Not.EqualTo(LogLevel.Information).And.Not.EqualTo(LogLevel.Trace).And.Not.EqualTo(LogLevel.Critical), $"{method.Name}: Debug, Warning or Error only");
            }
        });
    }

    [Test]
    public void The_uniqueness_check_would_notice_a_duplicate_id()
    {
        int[] ids = [.. Declared().Select(d => d.Attribute.EventId), Declared()[0].Attribute.EventId];

        Assert.That(ids.Distinct().Count(), Is.LessThan(ids.Length));
    }

    // ---- the log events table ---------------------------------------------------------------------------------------

    sealed record ReadmeRow(int Id, string Name, string Level, IReadOnlyList<string> Placeholders, string Area);

    static IReadOnlyList<ReadmeRow> ReadmeRows()
    {
        var lines = RouterDocs.ReadLines("docs/observability.md");
        var start = Array.FindIndex(lines, line => line.StartsWith("### Log events", StringComparison.Ordinal));
        Assert.That(start, Is.GreaterThanOrEqualTo(0), "docs/observability.md has a '### Log events' heading");

        var header = Array.FindIndex(lines, start, line => line.StartsWith("| Id", StringComparison.Ordinal));
        Assert.That(header, Is.GreaterThan(start), "the table starts with a '| Id | Name | Level | Placeholders | Area |' header");

        var rows = new List<ReadmeRow>();

        for (var i = header + 2; i < lines.Length && lines[i].StartsWith('|'); i++)
        {
            var cells = lines[i].Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            Assert.That(cells, Has.Length.EqualTo(5), $"row {i + 1}: five cells");

            var placeholders = Regex.Matches(cells[3], "`([A-Za-z]+)`").Select(m => m.Groups[1].Value).ToList();
            rows.Add(new ReadmeRow(int.Parse(cells[0], System.Globalization.CultureInfo.InvariantCulture), cells[1].Trim('`'), cells[2], placeholders, cells[4]));
        }

        return rows;
    }

    [Test]
    public void The_README_log_event_table_lists_every_event_as_the_code_declares_it()
    {
        var readme = ReadmeRows().ToDictionary(r => r.Id);
        var declared = Declared();

        Assert.That(readme.Keys.Order(), Is.EqualTo(declared.Select(d => d.Attribute.EventId).Order()), "one table row per event, no extra rows");
        Assert.Multiple(() =>
        {
            foreach (var (method, attribute) in declared)
            {
                var row = readme[attribute.EventId];
                Assert.That(row.Name, Is.EqualTo(attribute.EventName), $"{attribute.EventId}: name");
                Assert.That(row.Level, Is.EqualTo(attribute.Level.ToString()), $"{attribute.EventId}: level");
                Assert.That(row.Placeholders, Is.EqualTo(TemplatePlaceholders(attribute.Message)), $"{attribute.EventId}: placeholders");
                Assert.That(Areas.ContainsKey(row.Area), Is.True, $"{attribute.EventId}: '{row.Area}' is a known area");

                if (Areas.TryGetValue(row.Area, out var range))
                {
                    Assert.That(attribute.EventId, Is.InRange(range.From, range.To), $"{attribute.EventId}: inside the table area '{row.Area}'");
                }
            }
        });
    }

    // ---- the real call sites ------------------------------------------------------------------------------------

    sealed class DelegateStage(Func<RouteNavigationContext, StageResult> run) : INavigationStage
    {
        public ValueTask<StageResult> RunAsync(RouteNavigationContext context) => ValueTask.FromResult(run(context));
    }

    [Test]
    public async Task A_navigation_logs_requested_and_committed_with_a_typed_navigation_id_under_the_coordinators_category()
    {
        using var capture = new LogCapture();
        using var p = Pipeline.Create(loggerFactory: capture);
        await p.Coordinator.NavigateInitialAsync();

        await p.GoAsync("customers/5");

        var requested = capture.WithId(1000);
        Assert.That(requested, Has.Count.EqualTo(2));
        Assert.That(requested[1]["NavigationId"], Is.TypeOf<long>().And.EqualTo(2L));
        Assert.That(requested[1]["Target"], Is.EqualTo("customers/5"));
        Assert.That(requested[1]["Trigger"], Is.EqualTo(NavigationTrigger.Link));
        Assert.That(requested[1].Category, Is.EqualTo(typeof(NavigationCoordinator).FullName));
        Assert.That(capture.WithId(1001).Select(e => e["NavigationId"]), Is.EqualTo(new object[] { 1L, 2L }));
    }

    [Test]
    public async Task A_stage_that_cancels_logs_the_stage_and_a_loader_failure_logs_its_level_and_exception()
    {
        using var capture = new LogCapture();
        var cancel = false;
        using var p = Pipeline.Create(
            Pipeline.Table(map => map.Route("/customers", typeof(CustomersPage), name: "customers", children: c => c
                .Index(typeof(CustomerList), name: "customer-list")
                .Route("{id:int}", typeof(CustomerDetails), name: "customer").Loader<CustomerLoader>())),
            stages: [new DelegateStage(_ => cancel ? StageResult.Cancel : StageResult.Continue)],
            loggerFactory: capture);
        await p.Coordinator.NavigateInitialAsync();

        cancel = true;
        await p.GoAsync("customers/5");
        cancel = false;
        p.Loaders.Set("customer", _ => throw new InvalidOperationException("no customer"));
        await p.GoAsync("customers/6");

        Assert.That(capture.WithId(1100).Single()["Stage"], Is.EqualTo("DelegateStage"));
        var loader = capture.WithId(1201).Single();
        Assert.That(loader["Level"], Is.EqualTo(1));
        Assert.That(loader.Exception?.Message, Is.EqualTo("no customer"));
        Assert.That(loader.Level, Is.EqualTo(LogLevel.Error));
    }

    [Test]
    public void A_throwing_state_subscriber_is_logged_as_1800_and_the_others_still_run()
    {
        using var capture = new LogCapture();
        var store = new RouteStateStore(capture.CreateLogger<RouteStateStore>());
        var reached = false;
        store.NavigatingChanged += () => throw new InvalidOperationException("subscriber");
        store.NavigatingChanged += () => reached = true;

        store.SetNavigating(true);

        Assert.That(reached, Is.True);
        Assert.That(capture.WithId(1800).Single().Exception?.Message, Is.EqualTo("subscriber"));
    }
}
