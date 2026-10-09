using System.Text.Json;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Recording.Recordings;
using Avala.Recording.Storage;
using Avala.Sdk;
using Avala.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Recording.Tests.Storage;

public sealed class RecordingFilesTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EverySessionIsWrittenToItsOwnFileUnderTheRecordingsFolderOfTheDataFolderAsync()
    {
        using var data = new TemporaryFolder();
        var recordedAt = new DateTimeOffset(2026, 10, 9, 8, 30, 15, TimeSpan.Zero);
        var sessions = new[] { SessionId.New(), SessionId.New() };
        var turn = TurnId.New();

        await using (var files = new RecordingFiles(new AvalaPaths(data.Path), NullLogger<RecordingFiles>.Instance))
        {
            foreach (var session in sessions)
            {
                files.Begin(session, Header(recordedAt), new RecordingSettings(true, []));
                files.Note(session, TimeSpan.FromMilliseconds(3), new Observed(new TurnStarted(session, turn)));
            }

            files.Note(sessions[0], TimeSpan.FromMilliseconds(8), new Stopped());
        }

        foreach (var session in sessions)
        {
            var path = Path.Combine(data.Path, "recordings", $"20261009T083015Z-{session.Value:N}.json");
            using var written = JsonDocument.Parse(await File.ReadAllTextAsync(path, Cancellation));
            Assert.Equal(
                session == sessions[0] ? ["event", "stop"] : ["event"],
                written.RootElement.GetProperty("entries").EnumerateArray().Select(entry => entry.EnumerateObject().ElementAt(1).Name));
        }

        Assert.Equal(2, Directory.GetFiles(Path.Combine(data.Path, "recordings")).Length);
    }

    private static RecordingHeader Header(DateTimeOffset recordedAt) =>
        new(
            recordedAt,
            new ProviderInfo("scripted", "Scripted"),
            new AgentCapabilities(true, true, true, true, true, true, true, true, true),
            Option<AgentAccount>.None,
            new SessionOptions(".", PermissionMode.AskEveryTime));
}
