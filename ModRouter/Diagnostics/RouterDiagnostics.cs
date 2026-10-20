using Microsoft.Extensions.Logging;

namespace ModRouter.Diagnostics;

/// <summary>
/// The navigations of one application scope, newest last, kept for the debug panel and tools: a ring buffer of
/// <see cref="DiagnosticsOptions.HistorySize"/> immutable <see cref="NavigationRecord"/>s that the coordinator fills when a
/// navigation ends. With a size of 0 (the default) nothing is recorded, allocated or raised. <see cref="Changed"/> runs each
/// subscriber in isolation: one that throws is logged (event 1801) and skipped.
/// </summary>
public sealed class RouterDiagnostics
{
    readonly object _gate = new();
    readonly ILogger? _logger;
    NavigationRecord[]? _buffer;
    int _next;
    int _count;

    /// <summary>
    /// Creates the history for one application scope, sized from <paramref name="options"/>. <paramref name="logger"/>
    /// is optional and only used to report a throwing <see cref="Changed"/> subscriber.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    /// <exception cref="ArgumentException">
    ///   <c>options.Diagnostics.HistorySize</c> is outside 0 to <see cref="DiagnosticsOptions.MaxHistorySize"/>.
    /// </exception>
    public RouterDiagnostics(ModRouterOptions options, ILogger<RouterDiagnostics>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Diagnostics.Validate();

        HistorySize = options.Diagnostics.HistorySize;
        _logger = logger is null ? null : RouterLog.Guard(logger);
    }

    /// <summary>How many records are kept (fixed for the lifetime of the scope).</summary>
    public int HistorySize { get; }

    /// <summary>True when <see cref="HistorySize"/> is above 0.</summary>
    public bool IsEnabled => HistorySize > 0;

    /// <summary>The kept records, oldest first: a snapshot that later navigations do not change.</summary>
    public IReadOnlyList<NavigationRecord> History
    {
        get
        {
            lock (_gate)
            {
                if (_buffer is null || _count == 0)
                {
                    return [];
                }

                var snapshot = new NavigationRecord[_count];
                var start = (_next - _count + _buffer.Length) % _buffer.Length;

                for (var i = 0; i < _count; i++)
                {
                    snapshot[i] = _buffer[(start + i) % _buffer.Length];
                }

                return Array.AsReadOnly(snapshot);
            }
        }
    }

    /// <summary>Raised after a record was added (on the thread that ended the navigation).</summary>
    public event Action? Changed;

    /// <summary>True once the ring buffer exists (only after the first record with the history on).</summary>
    internal bool HasBuffer => _buffer is not null;

    /// <summary>Adds an ended navigation, evicting the oldest when full, then raises <see cref="Changed"/>. Ignored while the history is off.</summary>
    internal void Record(NavigationRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (!IsEnabled)
        {
            return;
        }

        lock (_gate)
        {
            _buffer ??= new NavigationRecord[HistorySize];
            _buffer[_next] = record;
            _next = (_next + 1) % _buffer.Length;
            _count = Math.Min(_count + 1, _buffer.Length);
        }

        Raise();
    }

    void Raise()
    {
        if (Changed is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch (Exception exception)
            {
                if (_logger is not null)
                {
                    RouterLog.DiagnosticsSubscriberThrew(_logger, exception);
                }
            }
        }
    }
}
