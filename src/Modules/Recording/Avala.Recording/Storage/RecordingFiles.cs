using System.Globalization;
using System.Threading.Channels;
using Avala.Agents.Contracts.Sessions;
using Avala.Recording.Capturing;
using Avala.Recording.Recordings;
using Avala.Sdk;
using Microsoft.Extensions.Logging;

namespace Avala.Recording.Storage;

internal sealed partial class RecordingFiles : IRecordingStore, IAsyncDisposable
{
    public const string FolderName = "recordings";

    private readonly Channel<INote> notes = Channel.CreateUnbounded<INote>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Dictionary<SessionId, Open> open = [];
    private readonly string folder;
    private readonly ILogger<RecordingFiles> logger;
    private readonly Task consumer;

    public RecordingFiles(AvalaPaths paths, ILogger<RecordingFiles> logger)
    {
        folder = paths.Folder(FolderName);
        this.logger = logger;
        consumer = Task.Run(ConsumeAsync);
    }

    public static string FileName(SessionId session, DateTimeOffset recordedAt) =>
        string.Create(CultureInfo.InvariantCulture, $"{recordedAt.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}-{session.Value:N}.json");

    public void Begin(SessionId session, RecordingHeader header, RecordingSettings settings) =>
        notes.Writer.TryWrite(new Began(session, SessionRecording.Begin(header, settings)));

    public void Note(SessionId session, TimeSpan at, IRecordedFact fact) =>
        notes.Writer.TryWrite(new Appended(session, at, fact));

    public async ValueTask DisposeAsync()
    {
        notes.Writer.TryComplete();
        await consumer;
    }

    private async Task ConsumeAsync()
    {
        await foreach (var note in notes.Reader.ReadAllAsync())
        {
            switch (note)
            {
                case Began began:
                    open[began.Session] = new Open(Path.Combine(folder, FileName(began.Session, began.Recording.Header.RecordedAt)), began.Recording);
                    break;
                case Appended appended when open.TryGetValue(appended.Session, out var current):
                    await AppendAsync(appended, current with { Recording = current.Recording.Add(appended.At, appended.Fact) });
                    break;
            }
        }

        foreach (var unsettled in open.Values)
        {
            await SaveAsync(unsettled);
        }
    }

    private async Task AppendAsync(Appended appended, Open next)
    {
        open[appended.Session] = next;

        if (SessionRecording.Settles(appended.Fact))
        {
            await SaveAsync(next);
        }

        if (next.Recording.Ended)
        {
            open.Remove(appended.Session);
        }
    }

    private async Task SaveAsync(Open recording)
    {
        var written = $"{recording.Path}.tmp";

        try
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(written, RecordingFormat.Write(recording.Recording), CancellationToken.None);
            File.Move(written, recording.Path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            LogNotSaved(recording.Path, exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The session recording {Path} could not be saved")]
    private partial void LogNotSaved(string path, Exception exception);

    private interface INote;

    private sealed record Began(SessionId Session, SessionRecording Recording) : INote;

    private sealed record Appended(SessionId Session, TimeSpan At, IRecordedFact Fact) : INote;

    private sealed record Open(string Path, SessionRecording Recording);
}
