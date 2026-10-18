using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Avala.Workbench.Tests.Following;

internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message)> entries = new();

    public IReadOnlyList<(LogLevel Level, string Message)> Entries => [.. entries];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        entries.Enqueue((logLevel, formatter(state, exception)));
}
