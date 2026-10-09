using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Avala.Sdk;
using Microsoft.Extensions.Logging;

namespace Avala.Runtime.Diagnostics;

internal sealed class LogFile : ILoggerProvider, IAsyncDisposable
{
    public const long Limit = 4 * 1024 * 1024;

    public const int Kept = 10;

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Channel<Entry> entries = Channel.CreateUnbounded<Entry>(new UnboundedChannelOptions { SingleReader = true });
    private readonly string folder;
    private readonly TimeProvider clock;
    private readonly LogRedaction redaction;
    private readonly long limit;
    private readonly Task writing;
    private string current = string.Empty;

    public LogFile(AvalaPaths paths, TimeProvider clock)
        : this(paths.Logs, clock, LogRedaction.FromEnvironment(), Limit)
    {
    }

    internal LogFile(string folder, TimeProvider clock, LogRedaction redaction, long limit)
    {
        this.folder = folder;
        this.clock = clock;
        this.redaction = redaction;
        this.limit = limit;
        writing = Task.Run(WriteAsync);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Write(LogLevel level, string category, string message, Exception? exception) =>
        entries.Writer.TryWrite(Entered(level, category, message, exception));

    public void WriteNow(LogLevel level, string category, string message, Exception? exception)
    {
        var entry = Entered(level, category, message, exception);

        try
        {
            Directory.CreateDirectory(folder);
            var path = Volatile.Read(ref current) is { Length: > 0 } open ? open : Path.Combine(folder, Name(entry.At, 0));
            using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            file.Write(Utf8.GetBytes(entry.Text));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Dispose() => entries.Writer.TryComplete();

    public async ValueTask DisposeAsync()
    {
        entries.Writer.TryComplete();
        await writing;
    }

    private Entry Entered(LogLevel level, string category, string message, Exception? exception)
    {
        var at = clock.GetLocalNow();
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{at:yyyy-MM-dd HH:mm:ss.fff zzz} {level} {category}: {message}{(exception is null ? string.Empty : Environment.NewLine + exception)}{Environment.NewLine}");

        return new Entry(at, redaction.Apply(text));
    }

    private async Task WriteAsync()
    {
        FileStream? file = null;

        try
        {
            await foreach (var entry in entries.Reader.ReadAllAsync())
            {
                file = await AppendAsync(file, entry);
            }
        }
        finally
        {
            await CloseAsync(file);
        }
    }

    private async Task<FileStream?> AppendAsync(FileStream? file, Entry entry)
    {
        var bytes = Utf8.GetBytes(entry.Text);

        try
        {
            if (file is null || !Path.GetFileName(file.Name).StartsWith(Day(entry.At), StringComparison.Ordinal) || file.Length + bytes.Length > limit)
            {
                await CloseAsync(file);
                file = Open(entry.At, bytes.Length);
            }

            await file.WriteAsync(bytes);
            await file.FlushAsync();

            return file;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return file;
        }
    }

    private static ValueTask CloseAsync(FileStream? file) => file?.DisposeAsync() ?? ValueTask.CompletedTask;

    private FileStream Open(DateTimeOffset at, int next)
    {
        Directory.CreateDirectory(folder);
        var latest = Directory.GetFiles(folder, $"{Day(at)}*.log")
            .Select(file => int.TryParse(Path.GetFileNameWithoutExtension(file).AsSpan(Day(at).Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : 0)
            .DefaultIfEmpty(0)
            .Max();
        var path = Path.Combine(folder, Name(at, new FileInfo(Path.Combine(folder, Name(at, latest))) is { Exists: true } full && full.Length + next > limit ? latest + 1 : latest));
        var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 4096, useAsync: true);
        Volatile.Write(ref current, path);
        Prune();

        return file;
    }

    private void Prune()
    {
        foreach (var old in Directory.GetFiles(folder, "avala-*.log").OrderByDescending(Path.GetFileName, StringComparer.Ordinal).Skip(Kept))
        {
            File.Delete(old);
        }
    }

    private static string Day(DateTimeOffset at) => string.Create(CultureInfo.InvariantCulture, $"avala-{at:yyyyMMdd}-");

    private static string Name(DateTimeOffset at, int index) => string.Create(CultureInfo.InvariantCulture, $"{Day(at)}{index:000}.log");

    private sealed record Entry(DateTimeOffset At, string Text);

    private sealed class FileLogger(LogFile file, string category) : ILogger
    {
        private readonly LogLevel least = category.StartsWith("Microsoft.", StringComparison.Ordinal) || category.StartsWith("System.", StringComparison.Ordinal)
            ? LogLevel.Warning
            : LogLevel.Information;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= least && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                file.Write(logLevel, category, formatter(state, exception), exception);
            }
        }
    }
}
