using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Simulator.Playback;

namespace Avala.Simulator.Tests.Playback;

public sealed class AdaptationTests
{
    private static readonly SessionId Session = SessionId.New();

    private static readonly TurnId Turn = TurnId.New();

    private static readonly CapabilitySet Everything = CapabilitySet.Of(
        new StreamsPartialOutput(),
        new ExposesReasoning(),
        new Resumable(),
        new ReportsUsage(),
        new ReportsCost("USD"),
        new ReportsLimits(["5h"]));

    private static readonly IAgentEvent[] Played =
    [
        new ResumeTokenIssued(Session, Turn, new ResumeToken("conversation")),
        new ItemStarted(Session, Turn, new ItemId("thinking"), ItemKind.Reasoning, "Thinking"),
        new ItemProgressed(Session, Turn, new ItemId("thinking"), "Considering."),
        new ItemCompleted(Session, Turn, new ItemId("thinking"), ItemOutcome.Succeeded),
        new ItemStarted(Session, Turn, new ItemId("reply"), ItemKind.Message, "Reply"),
        new ItemProgressed(Session, Turn, new ItemId("reply"), "Hello, "),
        new ItemProgressed(Session, Turn, new ItemId("reply"), "team."),
        new ItemCompleted(Session, Turn, new ItemId("reply"), ItemOutcome.Succeeded),
        new UsageReported(Session, Turn, new TokenUsage(10, 5, 0, 0, 0), new Cost(0.01m, "USD")),
        new LimitReported(Session, Turn, new UsageLimit("5h", 0.3, Option<DateTimeOffset>.None)),
    ];

    [Fact]
    public void ASessionThatDeclaresEverythingPlaysEveryCueAsItIs() =>
        Assert.Equal(Played, Adapted(Everything));

    [Fact]
    public void ASessionLeavesOutOrReshapesWhatItDoesNotDeclare()
    {
        var bare = Everything.Without<StreamsPartialOutput>().Without<ExposesReasoning>().Without<Resumable>().Without<ReportsCost>().Without<ReportsLimits>();

        Assert.Equal(
            [
                Played[4],
                new ItemProgressed(Session, Turn, new ItemId("reply"), "Hello, team."),
                Played[7],
                new UsageReported(Session, Turn, new TokenUsage(10, 5, 0, 0, 0), Option<Cost>.None),
            ],
            Adapted(bare));
        Assert.Equal([Played[4], Played[5], Played[6], Played[7], Played[9]], Adapted(Everything.Without<ReportsUsage>().Without<ExposesReasoning>().Without<Resumable>()));
    }

    [Fact]
    public void AMessageThatNeverProgressedCompletesAloneWithoutPartialOutput() =>
        Assert.Equal(
            [Played[4], Played[7]],
            new[] { Played[4], Played[7] }.SelectMany(new Adaptation(Everything.Without<StreamsPartialOutput>()).Adapt));

    private static IReadOnlyList<IAgentEvent> Adapted(CapabilitySet declared)
    {
        var adaptation = new Adaptation(declared);

        return [.. Played.SelectMany(adaptation.Adapt)];
    }
}
