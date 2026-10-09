using System.Text.Json;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Recording.Recordings;
using Avala.Recording.Storage;
using Avala.Sdk;

namespace Avala.Recording.Tests.Storage;

public sealed class RecordingFormatTests
{
    private const string Folder = "/home/ana/worktrees/job-1";

    private static readonly SessionId Session = SessionId.New();

    private static readonly TurnId First = TurnId.New();

    private static readonly TurnId Second = TurnId.New();

    [Fact]
    public void TheHeaderNamesTheFormatItsVersionTheProviderAndTheSessionWithoutItsWorkingDirectoryOrItsConnection()
    {
        var root = Written(Recording([]));

        Assert.Equal(("avala-recording", 1), (root.GetProperty("format").GetString(), root.GetProperty("version").GetInt32()));
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 8, 30, 0, TimeSpan.Zero), root.GetProperty("recordedAt").GetDateTimeOffset());
        Assert.Equal(("claude-code", "Claude Code"), (root.GetProperty("provider").GetProperty("id").GetString(), root.GetProperty("provider").GetProperty("name").GetString()));
        Assert.True(root.GetProperty("capabilities").GetProperty("canResume").GetBoolean());
        Assert.False(root.GetProperty("capabilities").GetProperty("asksQuestions").GetBoolean());
        Assert.Equal(("account-1", "[redacted]"), (root.GetProperty("account").GetProperty("id").GetString(), root.GetProperty("account").GetProperty("label").GetString()));
        var options = root.GetProperty("options");
        Assert.Equal(("askEveryTime", true), (options.GetProperty("permissions").GetString(), options.GetProperty("resumed").GetBoolean()));
        Assert.Equal(("canvas", "canvas"), (options.GetProperty("tools")[0].GetProperty("name").GetString(), options.GetProperty("tools")[0].GetProperty("surface").GetString()));
        Assert.DoesNotContain(Folder, root.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("resume-1", root.GetRawText(), StringComparison.Ordinal);
        Assert.All(
            ["/home/ana/.logins/work", "sk-ant-1", "large"],
            secret => Assert.DoesNotContain(secret, root.GetRawText(), StringComparison.Ordinal));
    }

    [Fact]
    public void EntriesKeepTheirRelativeTimeTheirTurnNumberAndTheirRefusalsWithEveryTextRedacted()
    {
        var entries = Written(Recording(
        [
            (0, new Sent(1, new UserTurn("Use ana@example.com"))),
            (5, new Observed(new TurnStarted(Session, First))),
            (9, new Observed(new PermissionRequested(Session, First, new ItemId("edit"), "Edit notes.md", ItemKind.FileEdit, $"{Folder}/notes.md"))),
            (12, new Responded(2, new PermissionDecision(new ItemId("edit"), PermissionAnswer.Deny) { Message = "Not now" })),
            (13, new Refused(2, AgentError.NoPendingPermission)),
            (20, new FileCaptured(new ItemId("edit"), "notes.md", "# Notes\n")),
            (21, new Observed(new UsageReported(Session, First, new TokenUsage(10, 2, 1, 0, 3), new Cost(0.5m, "USD")))),
            (30, new Observed(new TurnStarted(Session, Second))),
            (31, new Interrupted(3)),
            (40, new StreamEnded(Crashed: true)),
        ])).GetProperty("entries");

        Assert.Equal([0, 5, 9, 12, 20, 21, 30, 31, 40], entries.EnumerateArray().Select(entry => entry.GetProperty("at").GetInt64()));
        Assert.Equal("Use [redacted]", entries[0].GetProperty("send").GetProperty("text").GetString());
        Assert.Equal(("turnStarted", 1), Event(entries[1]));
        Assert.Equal(("permissionRequested", 1), Event(entries[2]));
        Assert.Equal(
            ("fileEdit", "${workingDirectory}/notes.md"),
            (entries[2].GetProperty("event").GetProperty("kind").GetString(), entries[2].GetProperty("event").GetProperty("target").GetString()));
        var respond = entries[3].GetProperty("respond");
        Assert.Equal(
            ("edit", "deny", "Not now", "noPendingPermission"),
            (respond.GetProperty("item").GetString(), respond.GetProperty("answer").GetString(), respond.GetProperty("message").GetString(), entries[3].GetProperty("refused").GetString()));
        Assert.Equal(
            ("edit", "notes.md", "# Notes\n"),
            (entries[4].GetProperty("file").GetProperty("item").GetString(), entries[4].GetProperty("file").GetProperty("path").GetString(), entries[4].GetProperty("file").GetProperty("content").GetString()));
        Assert.Equal((10, 3, 0.5m, "USD"), Usage(entries[5].GetProperty("event")));
        Assert.Equal(("turnStarted", 2), Event(entries[6]));
        Assert.Equal(JsonValueKind.Object, entries[7].GetProperty("interrupt").ValueKind);
        Assert.True(entries[8].GetProperty("end").GetProperty("crashed").GetBoolean());
    }

    [Fact]
    public void AToolCallItsResultAndTheResultTheHarnessReturnedAreRecordedWithTheirTextRedacted()
    {
        var result = new ToolResult(new ItemId("propose"), "Queued for ana@example.com") { IsError = true };
        var entries = Written(Recording(
        [
            (5, new Observed(new ToolCalled(Session, First, new ItemId("propose"), "propose_follow_up", """{ "instruction": "Mail ana@example.com" }"""))),
            (6, new Returned(1, result)),
            (7, new Observed(new ToolReturned(Session, First, new ItemId("propose"), result))),
        ])).GetProperty("entries");

        var called = entries[0].GetProperty("event");
        Assert.Equal(
            ("toolCalled", "propose", "propose_follow_up", """{ "instruction": "Mail [redacted]" }"""),
            (called.GetProperty("type").GetString(), called.GetProperty("item").GetString(), called.GetProperty("tool").GetString(), called.GetProperty("input").GetString()));
        Assert.All(
            [entries[1].GetProperty("return"), entries[2].GetProperty("event").GetProperty("result")],
            written => Assert.Equal(
                ("propose", "Queued for [redacted]", true),
                (written.GetProperty("item").GetString(), written.GetProperty("content").GetString(), written.GetProperty("isError").GetBoolean())));
    }

    private static SessionRecording Recording(IReadOnlyList<(int At, IRecordedFact Fact)> facts) =>
        facts.Aggregate(
            SessionRecording.Begin(
                new RecordingHeader(
                    new DateTimeOffset(2026, 10, 9, 8, 30, 0, TimeSpan.Zero),
                    new ProviderInfo("claude-code", "Claude Code"),
                    new AgentCapabilities(true, true, true, CanResume: true, true, true, true, true, AsksQuestions: false),
                    new AgentAccount("account-1", "ana@example.com"),
                    new SessionOptions(Folder, PermissionMode.AskEveryTime)
                    {
                        Resume = new ResumeToken("resume-1"),
                        Tools = [new HarnessTool("canvas", "Draw", "{}", ToolSurface.Canvas)],
                        Connection = new ConnectionEnvironment
                        {
                            ConfigurationDirectory = "/home/ana/.logins/work",
                            ApiKey = new Secret("sk-ant-1"),
                            Settings = new Dictionary<string, string> { ["model"] = "large" },
                        },
                    }),
                new RecordingSettings(true, ["ana@example.com"])),
            (recording, fact) => recording.Add(TimeSpan.FromMilliseconds(fact.At), fact.Fact));

    private static JsonElement Written(SessionRecording recording) => JsonDocument.Parse(RecordingFormat.Write(recording)).RootElement;

    private static (string?, int) Event(JsonElement entry) =>
        (entry.GetProperty("event").GetProperty("type").GetString(), entry.GetProperty("event").GetProperty("turn").GetInt32());

    private static (long, long, decimal, string?) Usage(JsonElement usage) =>
        (usage.GetProperty("tokens").GetProperty("input").GetInt64(),
            usage.GetProperty("tokens").GetProperty("reasoning").GetInt64(),
            usage.GetProperty("cost").GetProperty("amount").GetDecimal(),
            usage.GetProperty("cost").GetProperty("currency").GetString());
}
