using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Transcripts.Contracts;
using Avala.Transcripts.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Transcripts.Tests.Storage;

public sealed class SqliteTranscriptLogTests : IAsyncDisposable
{
    private static readonly SessionId Session = SessionId.New();
    private static readonly TurnId Turn = TurnId.New();
    private static readonly DateTimeOffset At = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly TemporaryFolder data = new();
    private readonly JobId job = JobId.New();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryKindOfFactComesBackInTheOrderItWasKeptAfterARestartAsync()
    {
        ITranscriptFact[] facts =
        [
            new AttemptBegan(1),
            new AgentActed(new ItemStarted(Session, Turn, new ItemId("tool"), ItemKind.Command, "dotnet test") { Input = "dotnet test" }),
            new AgentActed(new FormRequested(Session, Turn, new ItemId("form"), new AgentForm(FormPurpose.Question, "Which database?", "context", [new FormField("db", "Database", "Which?", FieldKind.SingleChoice, [new FormOption("PostgreSQL", "relational", true)])]))),
            new AgentActed(new FormAnswered(Session, Turn, new ItemId("form"), new FormAnswer(new ItemId("form"), [new FieldAnswer("db") { Chosen = ["PostgreSQL"] }]))),
            new AgentActed(new PlanUpdated(Session, Turn, [new PlanStep("Write", PlanStepStatus.Done)])),
            new AgentActed(new UsageReported(Session, Turn, new TokenUsage(1, 2, 3, 4, 5), new Cost(0.25m, "USD"))),
            new AgentActed(new TurnCompleted(Session, Turn, TurnOutcome.Finished)),
            new PermissionRuled(new PolicyDecision(Session, Turn, new ItemId("tool"), job, ItemKind.Command, "dotnet test", PolicyAnswer.Allow, Option<PolicyRule>.None, DecisionDelivery.Answered, At)),
            new FormRuled(new FormDecision(Session, Turn, new ItemId("form"), job, new AgentForm(FormPurpose.Question, "Which database?", string.Empty, []), Autonomy.Supervised, Option<FormAnswer>.None, [], DecisionDelivery.LeftToHuman, At)),
        ];
        var run = await KeptAsync(facts);

        var recalled = await EarlierAsync();

        Assert.Equal(facts.Length, recalled.Count);
        Assert.All(recalled, kept => Assert.Equal((run, At), (kept.Run, kept.At)));
        Assert.Equal(facts.Select(Describe), recalled.Select(kept => Describe(kept.Fact)));
    }

    [Fact]
    public async Task StreamedTextComesBackWholeAsync()
    {
        var item = new ItemId("message");
        await KeptAsync([.. "Hello there, team".Split(' ').Select(word => new AgentActed(new ItemProgressed(Session, Turn, item, word + " ")))]);

        var recalled = await EarlierAsync();

        Assert.Equal("Hello there, team ", string.Concat(recalled.Select(kept => ((ItemProgressed)((AgentActed)kept.Fact).Event).Text)));
    }

    [Fact]
    public async Task OnlyTheLatestSnapshotOfACanvasIsKeptWhereTheCanvasFirstAppearedAsync()
    {
        var canvas = new CanvasId(Turn, new ItemId("canvas"));
        await KeptAsync(
        [
            new CanvasDrawn(new CanvasSnapshot(canvas, Session, "Flow", "image/svg+xml", "<svg>1</svg>", CanvasStatus.Streaming, true)),
            new AttemptBegan(2),
            new CanvasDrawn(new CanvasSnapshot(canvas, Session, "Flow", "image/svg+xml", "<svg>2</svg>", CanvasStatus.Completed, true)),
        ]);

        var recalled = await EarlierAsync();

        Assert.Equal(["canvas <svg>2</svg> Completed", "attempt 2"], recalled.Select(kept => Describe(kept.Fact)));
    }

    [Fact]
    public async Task TheCurrentRunsFactsAreNotEarlierRunsAndTheAttemptsMarkedSurviveTheRestartAsync()
    {
        await KeptAsync([new AttemptBegan(1), new AttemptBegan(2)]);
        var restarted = Log();
        restarted.Keep(job, At, new AttemptBegan(3));
        await restarted.DisposeAsync();
        var third = Log();

        var (earlier, attempts, other) = (await third.EarlierRunsAsync(job, Cancellation), await third.AttemptsAsync(job, Cancellation), await third.AttemptsAsync(JobId.New(), Cancellation));
        third.Keep(job, At, new AttemptBegan(4));
        var current = await third.EarlierRunsAsync(job, Cancellation);
        await third.DisposeAsync();

        Assert.Equal((3, 3, 0, 3), (earlier.Count, attempts, other, current.Count));
        Assert.Equal(2, earlier.Select(kept => kept.Run).Distinct().Count());
    }

    [Fact]
    public async Task ReleasingAJobDropsWhatItsConversationShowedBeforeAndKeepsTheStartOfItsAttemptsAndOtherJobsAsync()
    {
        var other = JobId.New();
        var canvas = new CanvasId(Turn, new ItemId("canvas"));
        var log = Log();
        log.Keep(job, At, new AttemptBegan(1));
        log.Keep(job, At, new AgentActed(new ItemProgressed(Session, Turn, new ItemId("message"), "Hello ")));
        log.Keep(job, At, new CanvasDrawn(new CanvasSnapshot(canvas, Session, "Flow", "image/svg+xml", "<svg>1</svg>", CanvasStatus.Streaming, true)));
        log.Keep(other, At, new AgentActed(new TurnStarted(Session, Turn)));
        log.Release(job);
        log.Keep(job, At, new AgentActed(new ItemProgressed(Session, Turn, new ItemId("message"), "again")));
        log.Keep(job, At, new CanvasDrawn(new CanvasSnapshot(canvas, Session, "Flow", "image/svg+xml", "<svg>2</svg>", CanvasStatus.Completed, true)));
        await log.DisposeAsync();
        var restarted = Log();

        var (released, untouched) = (await restarted.EarlierRunsAsync(job, Cancellation), await restarted.EarlierRunsAsync(other, Cancellation));
        await restarted.DisposeAsync();

        Assert.Equal(["attempt 1", $"{new ItemProgressed(Session, Turn, new ItemId("message"), "again")}", "canvas <svg>2</svg> Completed"], released.Select(kept => Describe(kept.Fact)));
        Assert.Single(untouched);
    }

    public async ValueTask DisposeAsync() => await data.DisposeAsync();

    private SqliteTranscriptLog Log() => new(new AvalaPaths(data.Path), NullLogger<SqliteTranscriptLog>.Instance);

    private async Task<Guid> KeptAsync(IEnumerable<ITranscriptFact> facts)
    {
        var log = Log();

        foreach (var fact in facts)
        {
            log.Keep(job, At, fact);
        }

        await log.DisposeAsync();

        return log.Run;
    }

    private async Task<IReadOnlyList<KeptFact>> EarlierAsync()
    {
        var log = Log();
        var recalled = await log.EarlierRunsAsync(job, Cancellation);
        await log.DisposeAsync();

        return recalled;
    }

    private static string Describe(ITranscriptFact fact) => fact switch
    {
        AttemptBegan began => $"attempt {began.Attempt}",
        CanvasDrawn drawn => $"canvas {drawn.Snapshot.Content} {drawn.Snapshot.Status}",
        AgentActed { Event: ItemStarted started } => $"{started} {started.Input}",
        AgentActed { Event: FormRequested requested } => $"{requested.Session} {requested.Form.Title} {string.Join(",", requested.Form.Fields.SelectMany(field => field.Options).Select(option => $"{option.Label}:{option.Recommended}"))}",
        AgentActed { Event: FormAnswered answered } => $"{answered.Item} {string.Join(",", answered.Answer.Fields.SelectMany(field => field.Chosen))}",
        AgentActed { Event: PlanUpdated plan } => $"{plan.Turn} {string.Join(",", plan.Steps)}",
        AgentActed acted => acted.Event.ToString()!,
        PermissionRuled ruled => $"{ruled.Decision.Item} {ruled.Decision.Job} {ruled.Decision.Delivery} {ruled.Decision.At}",
        FormRuled ruled => $"{ruled.Decision.Form.Title} {ruled.Decision.Delivery} {ruled.Decision.Answer.IsSome}",
        _ => fact.ToString()!,
    };
}
