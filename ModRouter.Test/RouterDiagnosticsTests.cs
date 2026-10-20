using System.Reflection;
using ModRouter.Diagnostics;
using ModRouter.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ModRouter.Test;

/// <summary>
/// The navigation history: immutable records in a ring buffer of <see cref="DiagnosticsOptions.HistorySize"/> (0, the
/// default, records and allocates nothing), validated at startup, raising <see cref="RouterDiagnostics.Changed"/> with each
/// subscriber isolated (a throwing one is logged as 1801 and skipped; a throwing logger changes nothing either).
/// </summary>
[TestFixture]
public sealed class RouterDiagnosticsTests
{
    static RouterDiagnostics Create(int historySize, ILogger<RouterDiagnostics>? logger = null)
    {
        var options = new ModRouterOptions();
        options.Diagnostics.HistorySize = historySize;

        return new RouterDiagnostics(options, logger);
    }

    static NavigationRecord Record(long id, NavigationRecordOutcome outcome = NavigationRecordOutcome.Committed) => new()
    {
        NavigationId = id,
        Target = $"http://localhost/customers/{id}",
        Trigger = NavigationTrigger.Link,
        CommittedAs = outcome == NavigationRecordOutcome.Committed ? NavigationTrigger.Link : null,
        StartedAt = DateTimeOffset.UnixEpoch.AddSeconds(id),
        Duration = TimeSpan.FromMilliseconds(id),
        Outcome = outcome,
        Stages = [new StageRecord("Recognize", TimeSpan.FromMilliseconds(1), StageOutcome.Continued)],
    };

    [Test]
    public void History_is_off_by_default_and_then_records_and_allocates_nothing()
    {
        Assume.That(TelemetryModeSetup.Current, Is.Not.EqualTo(TelemetryMode.History), "the history test mode starts every option at 50");
        var options = new ModRouterOptions();
        var diagnostics = new RouterDiagnostics(options);
        var raised = 0;
        diagnostics.Changed += () => raised++;

        diagnostics.Record(Record(1));

        Assert.That(options.Diagnostics.HistorySize, Is.Zero);
        Assert.That(diagnostics.IsEnabled, Is.False);
        Assert.That(diagnostics.History, Is.Empty);
        Assert.That(diagnostics.HasBuffer, Is.False, "no ring buffer is allocated while the history is off");
        Assert.That(raised, Is.Zero);
    }

    [Test]
    public void The_history_keeps_the_last_HistorySize_records_oldest_first()
    {
        var diagnostics = Create(3);

        for (var id = 1; id <= 5; id++)
        {
            diagnostics.Record(Record(id));
        }

        Assert.That(diagnostics.History.Select(r => r.NavigationId), Is.EqualTo(new long[] { 3, 4, 5 }));
    }

    [Test]
    public void A_history_of_one_keeps_only_the_latest_record()
    {
        var diagnostics = Create(1);

        diagnostics.Record(Record(1));
        diagnostics.Record(Record(2, NavigationRecordOutcome.Superseded));

        Assert.That(diagnostics.History.Single().NavigationId, Is.EqualTo(2));
        Assert.That(diagnostics.History.Single().Outcome, Is.EqualTo(NavigationRecordOutcome.Superseded));
    }

    [Test]
    public void History_is_a_snapshot_that_later_records_do_not_change()
    {
        var diagnostics = Create(2);
        diagnostics.Record(Record(1));
        var before = diagnostics.History;

        diagnostics.Record(Record(2));
        diagnostics.Record(Record(3));

        Assert.That(before.Select(r => r.NavigationId), Is.EqualTo(new long[] { 1 }));
        Assert.That(diagnostics.History.Select(r => r.NavigationId), Is.EqualTo(new long[] { 2, 3 }));
    }

    [Test]
    public void Rapid_records_from_many_threads_are_all_counted_and_the_buffer_keeps_exactly_its_size()
    {
        var diagnostics = Create(50);
        var raised = 0;
        diagnostics.Changed += () => Interlocked.Increment(ref raised);

        Parallel.For(1, 1001, id => diagnostics.Record(Record(id)));

        Assert.That(raised, Is.EqualTo(1000));
        Assert.That(diagnostics.History, Has.Count.EqualTo(50));
        Assert.That(diagnostics.History.Select(r => r.NavigationId).Distinct().Count(), Is.EqualTo(50));
    }

    [Test]
    public void A_throwing_subscriber_is_logged_as_1801_and_the_others_still_run()
    {
        using var capture = new LogCapture();
        var diagnostics = Create(5, capture.CreateLogger<RouterDiagnostics>());
        var reached = 0;
        diagnostics.Changed += () => reached++;
        diagnostics.Changed += () => throw new InvalidOperationException("subscriber");
        diagnostics.Changed += () => reached++;

        Assert.DoesNotThrow(() => diagnostics.Record(Record(1)));

        Assert.That(reached, Is.EqualTo(2));
        var entry = capture.WithId(1801).Single();
        Assert.That(entry.Level, Is.EqualTo(LogLevel.Error));
        Assert.That(entry.EventId.Name, Is.EqualTo("DiagnosticsSubscriberThrew"));
        Assert.That(entry.Exception?.Message, Is.EqualTo("subscriber"));
        Assert.That(diagnostics.History.Single().NavigationId, Is.EqualTo(1));
    }

    [Test]
    public void A_throwing_subscriber_with_a_throwing_logger_still_does_not_throw()
    {
        using var capture = new LogCapture { Throwing = true };
        var diagnostics = Create(5, capture.CreateLogger<RouterDiagnostics>());
        var reached = false;
        diagnostics.Changed += () => throw new InvalidOperationException("subscriber");
        diagnostics.Changed += () => reached = true;

        Assert.DoesNotThrow(() => diagnostics.Record(Record(1)));
        Assert.That(reached, Is.True);
        Assert.That(capture.WithId(1801), Has.Count.EqualTo(1), "the logger was called (and threw)");
    }

    [Test]
    public void A_subscriber_that_unsubscribes_while_notified_does_not_disturb_the_others()
    {
        var diagnostics = Create(5);
        var calls = new List<string>();
        Action? first = null;
        first = () =>
        {
            calls.Add("first");
            diagnostics.Changed -= first;
        };
        diagnostics.Changed += first;
        diagnostics.Changed += () => calls.Add("second");

        diagnostics.Record(Record(1));
        diagnostics.Record(Record(2));

        Assert.That(calls, Is.EqualTo(new[] { "first", "second", "second" }));
    }

    // ---- the records -------------------------------------------------------------------------------------------

    static readonly Type[] ValueTypes =
    [
        typeof(string), typeof(long), typeof(int), typeof(TimeSpan), typeof(DateTimeOffset), typeof(NavigationTrigger), typeof(NavigationTrigger?),
        typeof(NavigationRecordOutcome), typeof(StageOutcome), typeof(IReadOnlyList<StageRecord>),
    ];

    [TestCase(typeof(NavigationRecord))]
    [TestCase(typeof(StageRecord))]
    public void A_record_holds_only_strings_numbers_enums_and_stage_records_and_cannot_be_changed(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.Name != "EqualityContract").ToList();

        Assert.That(properties, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            foreach (var property in properties)
            {
                Assert.That(ValueTypes, Does.Contain(property.PropertyType), $"{type.Name}.{property.Name} is {property.PropertyType.Name}: no component, state or context references");

                var isInitOnly = property.SetMethod?.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit") == true;
                Assert.That(property.SetMethod is null || isInitOnly, Is.True, $"{type.Name}.{property.Name} has a setter after construction");
            }
        });
    }

    [Test]
    public void A_records_stage_list_cannot_be_changed_through_a_cast()
    {
        var stages = new List<StageRecord> { new("Recognize", TimeSpan.Zero, StageOutcome.Continued) };
        var record = Record(1) with { Stages = stages };

        stages.Add(new StageRecord("Guard", TimeSpan.Zero, StageOutcome.Continued));

        Assert.That(record.Stages, Has.Count.EqualTo(1), "the record copies the list it is given");
        Assert.That(record.Stages, Is.Not.InstanceOf<List<StageRecord>>().And.Not.InstanceOf<StageRecord[]>());
    }

    // ---- validation and registration ---------------------------------------------------------------------------

    [TestCase(-1)]
    [TestCase(DiagnosticsOptions.MaxHistorySize + 1)]
    [TestCase(int.MaxValue)]
    public void AddModRouter_rejects_a_history_size_outside_zero_to_the_maximum_with_a_clear_message(int size)
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<ArgumentException>(() => services.AddModRouter(configure: o => o.Diagnostics.HistorySize = size));

        Assert.That(exception!.Message, Does.StartWith($"ModRouterOptions.Diagnostics.HistorySize must be between 0 and {DiagnosticsOptions.MaxHistorySize} (it is {size})."));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(DiagnosticsOptions.MaxHistorySize)]
    public void AddModRouter_accepts_a_history_size_from_zero_to_the_maximum_and_registers_a_scoped_RouterDiagnostics(int size)
    {
        var services = new ServiceCollection();
        services.AddModRouter(configure: o => o.Diagnostics.HistorySize = size);
        using var provider = services.BuildServiceProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var diagnostics = first.ServiceProvider.GetRequiredService<RouterDiagnostics>();

        Assert.That(diagnostics.HistorySize, Is.EqualTo(size));
        Assert.That(first.ServiceProvider.GetRequiredService<RouterDiagnostics>(), Is.SameAs(diagnostics));
        Assert.That(second.ServiceProvider.GetRequiredService<RouterDiagnostics>(), Is.Not.SameAs(diagnostics));
    }
}
