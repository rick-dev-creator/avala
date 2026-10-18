using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Channels;
using Avala.Sdk;
using Microsoft.Extensions.Logging;

namespace Avala.Runtime.Diagnostics;

internal sealed class LogFile : ILoggerProvider, ISupportExternalScope, IAsyncDisposable
{
    public const long Limit = 4 * 1024 * 1024;

    public const int Kept = 10;

    private const string TemplateKey = "{OriginalFormat}";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonWriterOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly Channel<Entry> entries = Channel.CreateUnbounded<Entry>(new UnboundedChannelOptions { SingleReader = true });
    private readonly string folder;
    private readonly TimeProvider clock;
    private readonly LogRedaction redaction;
    private readonly long limit;
    private readonly Task writing;
    private string current = string.Empty;
    private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();

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

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => Volatile.Write(ref scopes, scopeProvider);

    public void Write(LogLevel level, string category, string message, Option<Exception> exception) =>
        entries.Writer.TryWrite(Entered(new Said(level, category, message, message, [], exception)));

    public void WriteNow(LogLevel level, string category, string message, Option<Exception> exception)
    {
        var entry = Entered(new Said(level, category, message, message, [], exception));

        LastWords.Append(Volatile.Read(ref current) is { Length: > 0 } open ? open : Path.Combine(folder, Name(entry.At, 0)), Utf8.GetBytes(entry.Text));
    }

    public void Dispose() => entries.Writer.TryComplete();

    public async ValueTask DisposeAsync()
    {
        entries.Writer.TryComplete();
        await writing;
    }

    private void Write(Said said) => entries.Writer.TryWrite(Entered(said));

    private Entry Entered(Said said)
    {
        var at = clock.GetLocalNow();
        using var buffer = new MemoryStream();

        using (var json = new Utf8JsonWriter(buffer, Compact))
        {
            json.WriteStartObject();
            json.WriteString("@t", at);
            json.WriteString("@l", said.Level.ToString());
            json.WriteString("@mt", said.Template);
            json.WriteString("@m", said.Message);
            var thrown = said.Exception.Match(failure => failure.ToString(), () => string.Empty);

            if (thrown.Length > 0)
            {
                json.WriteString("@x", thrown);
            }

            json.WriteString("SourceContext", said.Category);

            foreach (var (name, value) in said.Properties.DistinctBy(property => property.Key))
            {
                json.WriteString(name, Rendered(value));
            }

            json.WriteEndObject();
        }

        return new Entry(at, redaction.Apply(Utf8.GetString(buffer.ToArray())) + "\n");
    }

    private List<KeyValuePair<string, object?>> Scoped(IEnumerable<KeyValuePair<string, object?>> own)
    {
        var properties = own.Where(property => property.Key != TemplateKey).ToList();
        Volatile.Read(ref scopes).ForEachScope(
            (scope, collected) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                {
                    collected.AddRange(pairs.Where(pair => pair.Key != TemplateKey));
                }
            },
            properties);

        return properties;
    }

    private static string Rendered(object? value) => value switch
    {
        null => string.Empty,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

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

    private sealed record Said(LogLevel Level, string Category, string Template, string Message, IReadOnlyList<KeyValuePair<string, object?>> Properties, Option<Exception> Exception);

    private sealed class FileLogger(LogFile file, string category) : ILogger
    {
        private readonly LogLevel least = category.StartsWith("Microsoft.", StringComparison.Ordinal) || category.StartsWith("System.", StringComparison.Ordinal)
            ? LogLevel.Warning
            : LogLevel.Information;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => Volatile.Read(ref file.scopes).Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel >= least && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                var own = state as IEnumerable<KeyValuePair<string, object?>> ?? [];
                var message = formatter(state, exception);
                var template = own.FirstOrDefault(property => property.Key == TemplateKey).Value as string ?? message;
                file.Write(new Said(logLevel, category, template, message, file.Scoped(own), exception.ToOption()));
            }
        }
    }
}
