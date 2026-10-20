namespace ModRouter.Core.Loading;

/// <summary>
/// Raises a <c>Changed</c> event one subscriber at a time, each in its own try/catch: a subscriber bug must neither
/// corrupt the value or the stream (the raise happens after the state is set) nor stop the other subscribers or the
/// enumeration. Core has no logger, so the exception is swallowed; the components marshal these events onto the
/// renderer and surface their own failures there.
/// </summary>
internal static class ChangeNotifier
{
    public static void Raise(Action? handlers)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action)handler)();
            }
            catch
            {
                // A throwing subscriber is its own bug; nothing here can report it and the others must still run.
            }
        }
    }
}

/// <summary>Where a <see cref="DeferredValue{T}"/> is in its life.</summary>
public enum DeferredStatus
{
    /// <summary>The task has not completed.</summary>
    Pending,
    /// <summary>The task completed with a value.</summary>
    Succeeded,
    /// <summary>The task threw; the exception is in <see cref="DeferredValue{T}.Error"/>.</summary>
    Faulted,
    /// <summary>The task was canceled.</summary>
    Canceled,
}

/// <summary>
/// A value that arrives after the navigation committed (a loader's deferred task). Its failure is contained here:
/// it never fails the navigation. <see cref="Changed"/> may be raised on any thread; components marshal it.
/// </summary>
public sealed class DeferredValue<T>
{
    int _status;
    T? _value;
    Exception? _error;

    /// <summary>Starts observing <paramref name="task"/> immediately; the constructor does not wait for it.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="task"/> is null.</exception>
    public DeferredValue(Task<T> task)
    {
        ArgumentNullException.ThrowIfNull(task);

        Task = task;
        _ = ObserveAsync();
    }

    /// <summary>
    /// The task this value observes, for a consumer that wants to await it directly. Awaiting a faulted or canceled
    /// task throws as usual; reading <see cref="Status"/> and <see cref="Error"/> does not.
    /// </summary>
    public Task<T> Task { get; }

    /// <summary>
    /// Where the task is; safe to read from any thread. It changes to its final value just before <see cref="Changed"/>
    /// is raised.
    /// </summary>
    public DeferredStatus Status => (DeferredStatus)Volatile.Read(ref _status);

    /// <summary>The value once <see cref="Status"/> is <see cref="DeferredStatus.Succeeded"/>.</summary>
    public T? Value => _value;

    /// <summary>The exception once <see cref="Status"/> is <see cref="DeferredStatus.Faulted"/>.</summary>
    public Exception? Error => _error;

    /// <summary>Raised once, when the task completes.</summary>
    public event Action? Changed;

    async Task ObserveAsync()
    {
        try
        {
            _value = await Task.ConfigureAwait(false);
            Volatile.Write(ref _status, (int)DeferredStatus.Succeeded);
        }
        catch (OperationCanceledException) when (Task.IsCanceled)
        {
            Volatile.Write(ref _status, (int)DeferredStatus.Canceled);
        }
        catch (Exception exception)
        {
            _error = exception;
            Volatile.Write(ref _status, (int)DeferredStatus.Faulted);
        }

        ChangeNotifier.Raise(Changed);
    }
}

/// <summary>Implemented by <see cref="RouteStream{T}"/> so the router can start any stream without knowing its item type.</summary>
internal interface IRouteStream
{
    void Start(CancellationToken cancellationToken);
}

/// <summary>
/// A list that grows as an <see cref="IAsyncEnumerable{T}"/> yields items. The router starts enumerating when the
/// navigation commits (never while it is only prepared) and stops when the level is discarded.
/// <see cref="Changed"/> may be raised on any thread.
/// </summary>
public sealed class RouteStream<T> : IRouteStream
{
    readonly IAsyncEnumerable<T> _source;
    readonly List<T> _items = [];
    readonly object _gate = new();
    T[]? _snapshot = [];
    bool _started;
    bool _isComplete;
    Exception? _error;

    /// <summary>
    /// Wraps <paramref name="source"/>; nothing is enumerated until the router starts the stream when the navigation
    /// commits.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    public RouteStream(IAsyncEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
    }

    /// <summary>The items received so far (a snapshot).</summary>
    public IReadOnlyList<T> Items
    {
        get
        {
            lock (_gate)
            {
                return _snapshot ??= [.. _items];
            }
        }
    }

    /// <summary>True once the source has been enumerated to its end.</summary>
    public bool IsComplete
    {
        get
        {
            lock (_gate)
            {
                return _isComplete;
            }
        }
    }

    /// <summary>The exception that ended the enumeration, if any.</summary>
    public Exception? Error
    {
        get
        {
            lock (_gate)
            {
                return _error;
            }
        }
    }

    /// <summary>
    /// Raised after each item is added and once more when the enumeration ends, completed or faulted (not when the
    /// level is discarded and the enumeration is canceled). Raised on whichever thread the source resumes on, one
    /// subscriber at a time; a subscriber that throws is ignored and the others still run. A late subscriber gets no
    /// replay: read <see cref="Items"/>, <see cref="IsComplete"/> and <see cref="Error"/>.
    /// </summary>
    public event Action? Changed;

    void IRouteStream.Start(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_started)
            {
                return;
            }

            _started = true;
        }

        _ = PumpAsync(cancellationToken);
    }

    async Task PumpAsync(CancellationToken cancellationToken)
    {
        // Start is called inside the navigation commit (on the renderer); a source whose items are ready at once must
        // not be enumerated to the end right there.
        await Task.Yield();

        try
        {
            await foreach (var item in _source.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                lock (_gate)
                {
                    _items.Add(item);
                    _snapshot = null;
                }

                ChangeNotifier.Raise(Changed);
            }

            lock (_gate)
            {
                _isComplete = true;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                _error = exception;
            }
        }

        ChangeNotifier.Raise(Changed);
    }
}
