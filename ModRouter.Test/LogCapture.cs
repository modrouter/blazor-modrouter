using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ModRouter.Test;

/// <summary>One log call as a logger received it: the structured state is kept as the generator produced it.</summary>
internal sealed record CapturedLog(string Category, EventId EventId, LogLevel Level, IReadOnlyList<KeyValuePair<string, object?>> State, Exception? Exception, string Message)
{
    /// <summary>The named placeholders, in template order, without <c>{OriginalFormat}</c>.</summary>
    public IReadOnlyList<string> Placeholders => [.. State.Select(pair => pair.Key).Where(key => key != "{OriginalFormat}")];

    public object? this[string placeholder] => State.Single(pair => pair.Key == placeholder).Value;
}

/// <summary>
/// An <see cref="ILoggerFactory"/> whose loggers record every call at every level (thread-safe: the router logs from
/// renderer and continuation threads). <see cref="Throwing"/> makes every logger throw instead, after recording.
/// </summary>
internal sealed class LogCapture : ILoggerFactory
{
    readonly ConcurrentQueue<CapturedLog> _entries = new();

    public bool Throwing { get; init; }

    public IReadOnlyList<CapturedLog> Entries => [.. _entries];

    public IReadOnlyList<CapturedLog> WithId(int id) => [.. _entries.Where(entry => entry.EventId.Id == id)];

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    sealed class Logger(LogCapture owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var pairs = state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
            owner._entries.Enqueue(new CapturedLog(category, eventId, logLevel, [.. pairs], exception, formatter(state, exception)));

            if (owner.Throwing)
            {
                throw new InvalidOperationException("The logger threw.");
            }
        }
    }
}
