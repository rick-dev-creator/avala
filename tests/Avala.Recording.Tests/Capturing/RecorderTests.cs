using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Recording.Capturing;
using Avala.Recording.Recordings;
using Avala.Recording.Storage;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Recording.Tests.Capturing;

public sealed class RecorderTests
{
    private static readonly HarnessTool Canvas = new("canvas", "Draw a canvas", "{}", ToolSurface.Canvas);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithRecordingOffASessionIsTheProvidersOwnAndNothingIsRecordedAsync()
    {
        var store = new MemoryStore();
        var provider = new ScriptedAgentProvider(ScriptedAgentProvider.Reply);

        var session = Outcomes.Succeeds(await Recorder(RecordingSettings.Off, store).Decorate(provider).StartAsync(Options("."), Cancellation));

        Assert.Same(Assert.Single(provider.Sessions), session);
        Assert.Empty(store.Recordings);
    }

    [Fact]
    public async Task ARecordedSessionKeepsItsHeaderTheHarnessInputsAndItsEventsInOrderWithTheContentOfEveryEditAsync()
    {
        using var folder = new TemporaryFolder();
        var notes = Path.Combine(folder.Path, "docs", "notes.md");
        Directory.CreateDirectory(Path.GetDirectoryName(notes)!);
        await File.WriteAllTextAsync(notes, "# Notes\n", Cancellation);
        var store = new MemoryStore();
        var provider = new ScriptedAgentProvider((session, turn) => Edit(session, turn, notes))
        {
            Account = () => new AgentAccount("account-1", "Team account"),
        };
        var recorded = Outcomes.Succeeds(await Recorder(new RecordingSettings(true, []), store).Decorate(provider).StartAsync(Options(folder.Path), Cancellation));

        var turn = Outcomes.Succeeds(await recorded.SendAsync(new UserTurn("Write the notes"), Cancellation));
        var events = await ReadTurnAsync(recorded);
        Outcomes.Succeeds(await recorded.RespondAsync(new PermissionDecision(new ItemId("edit"), PermissionAnswer.Allow) { Message = "Go ahead" }, Cancellation));
        Outcomes.Succeeds(await recorded.ReturnAsync(new ToolResult(new ItemId("propose"), "Accepted."), Cancellation));
        await recorded.DisposeAsync();

        var recording = store.Of(recorded.Id);
        Assert.Equal(
            (provider.Info, provider.Capabilities, Option<AgentAccount>.Some(new AgentAccount("account-1", "Team account")), PermissionMode.AskEveryTime),
            (recording.Header.Provider, recording.Header.Capabilities, recording.Header.Account, recording.Header.Options.Permissions));
        Assert.Equal(Edit(recorded.Id, turn, notes), events);
        Assert.Equal(
            [
                "sent Write the notes",
                "TurnStarted", "ItemStarted", "PermissionRequested", "PermissionResolved",
                "file edit docs/notes.md # Notes\n",
                "ItemCompleted", "TurnCompleted",
                "responded edit Allow Go ahead",
                "returned propose Accepted.",
                "stopped",
            ],
            recording.Entries.Select(entry => Describe(entry.Fact)));
        Assert.Equal(recording.Entries.Select(entry => entry.At).Order(), recording.Entries.Select(entry => entry.At));
    }

    [Theory]
    [InlineData("notes.md")]
    [InlineData("no\0tes.md")]
    public async Task AnEditOfAFileThatCannotBeReadIsRecordedWithoutContentEvenFromARelativeWorkingDirectoryAsync(string path)
    {
        var store = new MemoryStore();
        var provider = new ScriptedAgentProvider((session, turn) => Edit(session, turn, path));
        await using var recorded = Outcomes.Succeeds(await Recorder(new RecordingSettings(true, []), store).Decorate(provider).StartAsync(Options("missing"), Cancellation));

        var turn = Outcomes.Succeeds(await recorded.SendAsync(new UserTurn("Write the notes"), Cancellation));

        Assert.Equal(Edit(recorded.Id, turn, path), await ReadTurnAsync(recorded));
        Assert.DoesNotContain(store.Of(recorded.Id).Entries, entry => entry.Fact is FileCaptured);
    }

    [Theory]
    [InlineData(ItemOutcome.Succeeded, new[] { "file run notes.md # Changed\n" })]
    [InlineData(ItemOutcome.Failed, new string[0])]
    public async Task ASucceededCommandRecordsTheContentOfEveryFileItChangedAndNamesAndAFailedOneNothingAsync(ItemOutcome outcome, string[] captured)
    {
        var work = HostPaths.Rooted("/work");
        var notes = Path.Combine(work, "notes.md");
        var other = Path.Combine(work, "other.md");
        var files = new SteppedFiles(
            [Stamps((notes, 8)), Stamps((notes, 10), (other, 3))],
            new Dictionary<string, string> { [notes] = "# Changed\n", [other] = "new" });
        var store = new MemoryStore();
        var provider = new ScriptedAgentProvider((session, turn) =>
        [
            new TurnStarted(session, turn),
            new ItemStarted(session, turn, new ItemId("run"), ItemKind.Command, "Run sed") { Input = "sed -i s/a/b/ notes.md" },
            new ItemCompleted(session, turn, new ItemId("run"), outcome),
            new TurnCompleted(session, turn, TurnOutcome.Finished),
        ]);
        await using var recorded = Outcomes.Succeeds(await Recorder(new RecordingSettings(true, []), store, files).Decorate(provider).StartAsync(Options(work), Cancellation));

        Outcomes.Succeeds(await recorded.SendAsync(new UserTurn("Fix the notes"), Cancellation));
        await ReadTurnAsync(recorded);

        Assert.Equal(captured, store.Of(recorded.Id).Entries.Where(entry => entry.Fact is FileCaptured).Select(entry => Describe(entry.Fact)));
    }

    [Fact]
    public async Task AnInputTheProviderRefusesIsRecordedWithItsRefusalAsync()
    {
        var store = new MemoryStore();
        var recorded = Outcomes.Succeeds(await Recorder(new RecordingSettings(true, []), store)
            .Decorate(new ScriptedAgentProvider(ScriptedAgentProvider.Reply))
            .StartAsync(Options("."), Cancellation));

        Assert.Equal(AgentError.NoTurnInProgress, Outcomes.FailsWith(await recorded.InterruptAsync(Cancellation)));

        var entry = Assert.Single(store.Of(recorded.Id).Entries);
        Assert.Equal((typeof(Interrupted), Option<AgentError>.Some(AgentError.NoTurnInProgress)), (entry.Fact.GetType(), entry.Refusal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AStreamThatEndsOnItsOwnIsRecordedAsClosedOrCrashedAsync(bool crashes)
    {
        var store = new MemoryStore();
        var provider = new ScriptedAgentProvider(ScriptedAgentProvider.Reply);
        var recorded = Outcomes.Succeeds(await Recorder(new RecordingSettings(true, []), store).Decorate(provider).StartAsync(Options("."), Cancellation));
        var scripted = Assert.Single(provider.Sessions);

        if (crashes)
        {
            scripted.Crash();
            await Assert.ThrowsAsync<InvalidOperationException>(async () => await ReadTurnAsync(recorded));
        }
        else
        {
            scripted.End();
            Assert.Empty(await ReadTurnAsync(recorded));
        }

        Assert.Equal([crashes ? "ended crashed" : "ended closed"], store.Of(recorded.Id).Entries.Select(entry => Describe(entry.Fact)));
    }

    [Fact]
    public async Task StoppingASessionIsRecordedAsAStopRatherThanAnEndOfItsStreamAsync()
    {
        var store = new MemoryStore();
        var recorded = Outcomes.Succeeds(await Recorder(new RecordingSettings(true, []), store)
            .Decorate(new ScriptedAgentProvider(ScriptedAgentProvider.Reply))
            .StartAsync(Options("."), Cancellation));
        var reading = ReadTurnAsync(recorded);

        await recorded.DisposeAsync();
        await reading;

        Assert.Equal(["stopped"], store.Of(recorded.Id).Entries.Select(entry => Describe(entry.Fact)));
    }

    private static ProviderRecorder Recorder(RecordingSettings settings, MemoryStore store, IEditedFiles? files = null) =>
        new(new FixedSettings(settings), store, files ?? new EditedFiles(), TimeProvider.System);

    private static FolderStamps Stamps(params (string Path, long Length)[] files) =>
        new(files.ToDictionary(file => file.Path, file => new FileStamp(file.Length, DateTime.UnixEpoch), StringComparer.Ordinal));

    private static SessionOptions Options(string folder) => new(folder, PermissionMode.AskEveryTime) { Tools = [Canvas] };

    private static IEnumerable<IAgentEvent> Edit(SessionId session, TurnId turn, string path) =>
    [
        new TurnStarted(session, turn),
        new ItemStarted(session, turn, new ItemId("edit"), ItemKind.FileEdit, "Edit notes.md"),
        new PermissionRequested(session, turn, new ItemId("edit"), "Edit notes.md", ItemKind.FileEdit, path),
        new PermissionResolved(session, turn, new ItemId("edit"), PermissionAnswer.Allow),
        new ItemCompleted(session, turn, new ItemId("edit"), ItemOutcome.Succeeded),
        new TurnCompleted(session, turn, TurnOutcome.Finished),
    ];

    private static async Task<IReadOnlyList<IAgentEvent>> ReadTurnAsync(IAgentSession session)
    {
        var seen = new List<IAgentEvent>();

        await foreach (var agentEvent in session.Events.WithCancellation(Cancellation))
        {
            seen.Add(agentEvent);

            if (agentEvent is TurnCompleted)
            {
                break;
            }
        }

        return seen;
    }

    private static string Describe(IRecordedFact fact) => fact switch
    {
        Observed observed => observed.Event.GetType().Name,
        FileCaptured file => $"file {file.Item.Value} {file.Path} {file.Content}",
        Sent sent => $"sent {sent.Turn.Text}",
        Responded responded => $"responded {responded.Decision.Item.Value} {responded.Decision.Answer} {responded.Decision.Message.Match(message => message, () => string.Empty)}",
        Returned returned => $"returned {returned.Result.Item.Value} {returned.Result.Content}",
        StreamEnded ended => ended.Crashed ? "ended crashed" : "ended closed",
        Stopped => "stopped",
        _ => fact.GetType().Name,
    };

    private sealed class SteppedFiles(IEnumerable<FolderStamps> stamps, IReadOnlyDictionary<string, string> contents) : IEditedFiles
    {
        private readonly Queue<FolderStamps> stamps = new(stamps);

        public ValueTask<Option<string>> ReadAsync(string path, CancellationToken cancellationToken) =>
            ValueTask.FromResult(contents.TryGetValue(path, out var content) ? Option<string>.Some(content) : Option<string>.None);

        public Task<FolderStamps> StampAsync(string folder, CancellationToken cancellationToken) => Task.FromResult(stamps.Dequeue());
    }

    private sealed class FixedSettings(RecordingSettings settings) : IRecordingSettings
    {
        public ValueTask<RecordingSettings> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(settings);
    }
}
