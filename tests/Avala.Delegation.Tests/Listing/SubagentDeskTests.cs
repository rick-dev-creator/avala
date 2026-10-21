using System.Text.Json.Nodes;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Delegation.Listing;
using Avala.Delegation.Records;
using Avala.Delegation.Tests.Delegating;
using Avala.Delegation.Tests.Records;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Transcripts.Contracts;

namespace Avala.Delegation.Tests.Listing;

public sealed class SubagentDeskTests
{
    private static readonly TurnId Turn = TurnId.New();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnOrchestratorIsAnsweredWithItsInProcessSubagentsOfEveryRunAndItsDelegatedChildrenEachWithItsKindStateAndStatusAsync()
    {
        var listing = new Listing();
        var parent = listing.Jobs.Running();
        var child = listing.Jobs.Running(parent, new ConnectionName("personal"));
        var session = SessionId.New();
        var childSession = SessionId.New();
        listing.Transcripts.Kept[parent] =
        [
            new KeptFact(Guid.NewGuid(), DateTimeOffset.UnixEpoch, new AgentActed(new ItemStarted(SessionId.New(), Turn, new ItemId("survey"), ItemKind.Subagent, "Subagent: Survey the greeting"))),
        ];
        await listing.Book.KeepAsync(new DelegationRecord(session, new ItemId("delegate"), "Migrate the database\nCarefully.", DateTimeOffset.UnixEpoch) { Parent = parent, Child = child, Connection = new ConnectionName("personal") }, Cancellation);
        listing.Usage.Opened.Add(new UsageSession(childSession, new ProviderInfo("codex", "Codex"), Option<AgentAccount>.None, new ConnectionName("personal"), DateTimeOffset.UnixEpoch) { Job = child });
        await listing.StartedAsync(parent, session);
        await listing.StartedAsync(child, childSession);
        await listing.ActedAsync(new ItemStarted(session, Turn, new ItemId("read"), ItemKind.Subagent, "Subagent: Read the changelog"));
        await listing.ActedAsync(new ItemCompleted(session, Turn, new ItemId("read"), ItemOutcome.Succeeded));
        await listing.ActedAsync(new PermissionRequested(childSession, Turn, new ItemId("migrate"), "Run the migration", ItemKind.Command, "dotnet ef database update"));

        var answer = await listing.AskAsync(session, "{}");

        Assert.Equal(
            [
                """{"name":"Survey the greeting","kind":"inProcess","state":"lost","status":"lost"}""",
                """{"name":"Read the changelog","kind":"inProcess","state":"completed","status":"completed"}""",
                $$"""{"name":"Migrate the database","kind":"crossHarness","state":"waiting","status":"awaitingPermission","job":"{{child.Value}}","connection":"personal","harness":"Codex","role":"worker"}""",
            ],
            JsonNode.Parse(answer.Content)!["subagents"]!.AsArray().Select(entry => entry!.ToJsonString()));
    }

    [Fact]
    public async Task AnInProcessSubagentWhoseItemExpiredIsListedStoppedAsThePersonSeesItAsync()
    {
        var listing = new Listing();
        var session = SessionId.New();
        await listing.StartedAsync(listing.Jobs.Running(), session);
        await listing.ActedAsync(new ItemStarted(session, Turn, new ItemId("build"), ItemKind.Subagent, "Subagent: Wait for the build"));
        await listing.ActedAsync(new ItemCompleted(session, Turn, new ItemId("build"), ItemOutcome.Expired));

        var answer = await listing.AskAsync(session, "{}");

        Assert.Equal("stopped", JsonNode.Parse(answer.Content)!["subagents"]![0]!["state"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(true, """{ "job": "mine" }""", "malformedInput")]
    [InlineData(false, "{}", "noJob")]
    public async Task ACallWithFieldsOrFromASessionThatRunsNoJobIsRefusedAsync(bool started, string input, string refusal)
    {
        var listing = new Listing();
        var session = SessionId.New();
        await (started ? listing.StartedAsync(listing.Jobs.Running(), session) : Task.CompletedTask);

        var answer = await listing.AskAsync(session, input);

        Assert.Equal((true, refusal), (answer.IsError, JsonNode.Parse(answer.Content)!["refused"]!.GetValue<string>()));
    }

    private sealed class Listing
    {
        private readonly SubagentDesk desk;

        public Listing()
        {
            Book = new DelegationBook(new InMemoryDelegations());
            desk = new SubagentDesk(Transcripts, new ChildRoster(Book, Jobs, Usage), Agents);
        }

        public KeptTranscripts Transcripts { get; } = new();

        public FakeJobs Jobs { get; } = new();

        public FixedUsage Usage { get; } = new();

        public ReturningAgents Agents { get; } = new();

        public DelegationBook Book { get; }

        public async Task StartedAsync(JobId job, SessionId session) => await desk.HandleAsync(new JobSessionStarted(job, session), Cancellation);

        public async Task ActedAsync(IAgentEvent activity) => await desk.HandleAsync(new AgentActivity(activity), Cancellation);

        public async Task<ToolResult> AskAsync(SessionId session, string input)
        {
            await ActedAsync(new ToolCalled(session, Turn, new ItemId("list"), SubagentsTool.Name, input));

            return await Agents.ResultOfAsync("list", Cancellation);
        }
    }
}
