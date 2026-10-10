using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Agents.Tests.Conformance;

public sealed class CapabilityConformanceTests
{
    private static readonly SessionOptions Options = new(".", PermissionMode.AllowAll);

    private static readonly UserTurn Instruction = new("conformance");

    private static CancellationToken Deadline => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "usage was reported although the provider does not declare ReportsUsage")]
    public async Task UsageComesOnlyFromAProviderThatReportsUsageAsync(bool declared, string expected)
    {
        var provider = Reporting(new UsageReported(default, default, new TokenUsage(10, 5, 0, 0, 0), Option<Cost>.None), declared ? Everything : Everything.Without<ReportsUsage>());

        Assert.Equal(expected, string.Join('|', await AgentConformance.CheckTurnAsync(provider, Deadline)));
    }

    [Theory]
    [InlineData("USD", true, "")]
    [InlineData("EUR", true, "a cost was reported in EUR although the provider declares USD")]
    [InlineData("USD", false, "a cost was reported although the provider does not declare ReportsCost")]
    public async Task ACostComesOnlyFromAProviderThatReportsCostAndInItsCurrencyAsync(string currency, bool declared, string expected)
    {
        var provider = Reporting(
            new UsageReported(default, default, new TokenUsage(10, 5, 0, 0, 0), new Cost(0.01m, currency)),
            declared ? Everything : Everything.Without<ReportsCost>());

        Assert.Equal(expected, string.Join('|', await AgentConformance.CheckTurnAsync(provider, Deadline)));
    }

    [Theory]
    [InlineData("5h", true, "")]
    [InlineData("1h", true, "a limit was reported for the window 1h, which the provider does not declare")]
    [InlineData("5h", false, "a limit was reported although the provider does not declare ReportsLimits")]
    public async Task ALimitComesOnlyFromAProviderThatReportsLimitsAndInADeclaredWindowAsync(string window, bool declared, string expected)
    {
        var provider = Reporting(
            new LimitReported(default, default, new UsageLimit(window, 0.4, Option<DateTimeOffset>.None)),
            declared ? Everything : Everything.Without<ReportsLimits>());

        Assert.Equal(expected, string.Join('|', await AgentConformance.CheckTurnAsync(provider, Deadline)));
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "the reasoning thinking was exposed although the provider does not declare ExposesReasoning")]
    public async Task ReasoningIsExposedOnlyByAProviderThatDeclaresItAsync(bool declared, string expected)
    {
        var provider = new ScriptedAgentProvider(Saying(ItemKind.Reasoning, "thinking", "Considering the schema."))
        {
            Capabilities = declared ? Everything : Everything.Without<ExposesReasoning>(),
        };

        Assert.Equal(expected, string.Join('|', await AgentConformance.CheckTurnAsync(provider, Deadline)));
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "the message reply streamed in parts although the provider does not declare StreamsPartialOutput")]
    public async Task AMessageStreamsInPartsOnlyFromAProviderThatDeclaresItAsync(bool declared, string expected)
    {
        var provider = new ScriptedAgentProvider(Saying(ItemKind.Message, "reply", "Done", " now."))
        {
            Capabilities = declared ? Everything : Everything.Without<StreamsPartialOutput>(),
        };

        Assert.Equal(expected, string.Join('|', await AgentConformance.CheckTurnAsync(provider, Deadline)));
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "no usage was reported although the provider declares ReportsUsage|no cost was reported although the provider declares ReportsCost|no limit was reported although the provider declares ReportsLimits")]
    public async Task AProviderReportsTheUsageCostAndLimitsItDeclaresAsync(bool reports, string expected)
    {
        var provider = new ScriptedAgentProvider(reports ? ReportingAll : ScriptedAgentProvider.Reply) { Capabilities = Everything };

        Assert.Equal(expected, string.Join('|', await CapabilityConformance.CheckReportsAsync(provider, Options, Instruction, Deadline)));
    }

    [Fact]
    public async Task AProviderThatDeclaresNoReportsOwesNoneAsync()
    {
        var provider = new ScriptedAgentProvider(ScriptedAgentProvider.Reply)
        {
            Capabilities = Everything.Without<ReportsUsage>().Without<ReportsCost>().Without<ReportsLimits>(),
        };

        Assert.Empty(await CapabilityConformance.CheckReportsAsync(provider, Options, Instruction, Deadline));
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "the interrupted turn ended Finished")]
    public async Task AnInterruptibleProviderEndsTheTurnItIsAskedToInterruptAsInterruptedAsync(bool honors, string expected)
    {
        var provider = new ScriptedAgentProvider(
            (session, turn) => [new TurnStarted(session, turn), new TurnCompleted(session, turn, honors ? TurnOutcome.Interrupted : TurnOutcome.Finished)],
            canInterrupt: true);

        Assert.Equal(expected, string.Join('|', await CapabilityConformance.CheckInterruptAsync(provider, Options, Instruction, Deadline)));
    }

    [Fact]
    public async Task AProviderThatIsNotInterruptibleIsNeverInterruptedAndRunsTheTurnCheckAsync()
    {
        var provider = new ScriptedAgentProvider(ScriptedAgentProvider.Reply);

        Assert.Empty(await CapabilityConformance.CheckInterruptAsync(provider, Options, Instruction, Deadline));
        Assert.Equal(0, Assert.Single(provider.Sessions).Interruptions);
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "the message mid-turn was not queued into the running turn")]
    public async Task AProviderThatAcceptsMessagesMidTurnQueuesOneIntoTheRunningTurnAsync(bool queues, string expected)
    {
        var provider = new ScriptedAgentProvider((session, turn) => [new TurnStarted(session, turn)])
        {
            Capabilities = Everything.With(new AcceptsMessagesMidTurn()),
            MidTurn = (session, turn, text) =>
            [
                .. queues ? [new MessageQueued(session, turn, text)] : Array.Empty<IAgentEvent>(),
                new TurnCompleted(session, turn, TurnOutcome.Finished),
            ],
        };

        Assert.Equal(expected, string.Join('|', await CapabilityConformance.CheckMidTurnAsync(provider, Options, Instruction, Deadline)));
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "a message was queued into the turn although the provider does not declare AcceptsMessagesMidTurn")]
    public async Task AMessageIsQueuedIntoATurnOnlyByAProviderThatAcceptsMessagesMidTurnAsync(bool declared, string expected)
    {
        var provider = Reporting(
            new MessageQueued(default, default, "Keep the alias"),
            declared ? Everything.With(new AcceptsMessagesMidTurn()) : Everything);

        Assert.Equal(expected, string.Join('|', await AgentConformance.CheckTurnAsync(provider, Deadline)));
    }

    [Fact]
    public async Task AProviderThatAcceptsNoMessageMidTurnIsNeverSentOneAndRunsTheTurnCheckAsync()
    {
        var provider = new ScriptedAgentProvider(ScriptedAgentProvider.Reply);

        Assert.Empty(await CapabilityConformance.CheckMidTurnAsync(provider, Options, Instruction, Deadline));
        Assert.DoesNotContain(Assert.Single(provider.Sessions).Turns, turn => turn.MidTurn);
    }

    [Theory]
    [InlineData("large", "high", "")]
    [InlineData("small", "high", "the model small was reported although large was chosen")]
    [InlineData("large-2026", "high", "")]
    [InlineData("large", "low", "the effort low was reported although high was chosen")]
    [InlineData("", "", "no model was reported although the provider declares OffersModels")]
    public async Task AProviderThatOffersModelsRunsWithTheChosenOneAndReportsItAsync(string model, string effort, string expected)
    {
        var provider = new ScriptedAgentProvider((session, turn) =>
        [
            new TurnStarted(session, turn),
            .. model.Length == 0 ? Array.Empty<IAgentEvent>() : [new ModelReported(session, turn, model) { Effort = effort }],
            new TurnCompleted(session, turn, TurnOutcome.Finished),
        ])
        {
            Capabilities = Everything.With(new OffersModels(["small", "large"], ["low", "high"])),
        };

        Assert.Equal(expected, string.Join('|', await CapabilityConformance.CheckModelChoiceAsync(provider, Options, Instruction, Deadline)));
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "the model large was reported although the provider does not declare OffersModels")]
    public async Task AModelIsReportedOnlyByAProviderThatOffersModelsAsync(bool declared, string expected)
    {
        var provider = Reporting(
            new ModelReported(default, default, "large"),
            declared ? Everything.With(new OffersModels(["large"], [])) : Everything);

        Assert.Equal(expected, string.Join('|', await AgentConformance.CheckTurnAsync(provider, Deadline)));
    }

    [Fact]
    public async Task AProviderThatOffersNoModelsIsGivenNoChoiceAndRunsTheTurnCheckAsync()
    {
        var provider = new ScriptedAgentProvider(ScriptedAgentProvider.Reply);

        Assert.Empty(await CapabilityConformance.CheckModelChoiceAsync(provider, Options, Instruction, Deadline));
        Assert.True(Assert.Single(provider.Sessions).Options.Model.IsDefault);
    }

    private static CapabilitySet Everything => ScriptedAgentProvider.Declared;

    private static ScriptedAgentProvider Reporting(IAgentEvent report, CapabilitySet declared) =>
        new((session, turn) => [new TurnStarted(session, turn), Readdressed(report, session, turn), new TurnCompleted(session, turn, TurnOutcome.Finished)])
        {
            Capabilities = declared,
        };

    private static IEnumerable<IAgentEvent> ReportingAll(SessionId session, TurnId turn) =>
    [
        new TurnStarted(session, turn),
        new UsageReported(session, turn, new TokenUsage(10, 5, 0, 0, 0), new Cost(0.01m, "USD")),
        new LimitReported(session, turn, new UsageLimit("5h", 0.4, Option<DateTimeOffset>.None)),
        new TurnCompleted(session, turn, TurnOutcome.Finished),
    ];

    private static Func<SessionId, TurnId, IEnumerable<IAgentEvent>> Saying(ItemKind kind, string item, params string[] parts) => (session, turn) =>
    [
        new TurnStarted(session, turn),
        new ItemStarted(session, turn, new ItemId(item), kind, item),
        .. parts.Select(part => new ItemProgressed(session, turn, new ItemId(item), part)),
        new ItemCompleted(session, turn, new ItemId(item), ItemOutcome.Succeeded),
        new TurnCompleted(session, turn, TurnOutcome.Finished),
    ];

    private static IAgentEvent Readdressed(IAgentEvent report, SessionId session, TurnId turn) => report switch
    {
        UsageReported usage => usage with { Session = session, Turn = turn },
        LimitReported limit => limit with { Session = session, Turn = turn },
        MessageQueued queued => queued with { Session = session, Turn = turn },
        ModelReported model => model with { Session = session, Turn = turn },
        _ => report,
    };
}
