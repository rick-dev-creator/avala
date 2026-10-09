using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Testing;

namespace Avala.Agents.Tests.Conformance;

public sealed class AgentConformanceTests
{
    private static CancellationToken Deadline => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AWellBehavedProviderConformsAsync()
    {
        var provider = new ScriptedAgentProvider(ScriptedAgentProvider.Reply);

        Assert.Empty(await AgentConformance.CheckTurnAsync(provider, Deadline));
    }

    [Fact]
    public async Task ReportsAnItemLeftOpenAsync()
    {
        var provider = new ScriptedAgentProvider((session, turn) =>
        [
            new TurnStarted(session, turn),
            new ItemStarted(session, turn, new ItemId("build"), ItemKind.Command, "Build"),
            new TurnCompleted(session, turn, TurnOutcome.Finished),
        ]);

        Assert.Equal(["item build was left open"], await AgentConformance.CheckTurnAsync(provider, Deadline));
    }

    [Fact]
    public async Task ReportsEventsForUnknownItemsAsync()
    {
        var provider = new ScriptedAgentProvider((session, turn) =>
        [
            new TurnStarted(session, turn),
            new ItemCompleted(session, turn, new ItemId("ghost"), ItemOutcome.Succeeded),
            new TurnCompleted(session, turn, TurnOutcome.Finished),
        ]);

        Assert.Equal(["ItemCompleted was rejected: UnknownItem"], await AgentConformance.CheckTurnAsync(provider, Deadline));
    }

    [Fact]
    public async Task ReportsAMissingTurnStartAsync()
    {
        var provider = new ScriptedAgentProvider((session, turn) =>
        [
            new ItemStarted(session, turn, new ItemId("reply"), ItemKind.Message, "Reply"),
            new TurnCompleted(session, turn, TurnOutcome.Finished),
        ]);

        Assert.Equal(
            ["ItemStarted arrived before TurnStarted", "TurnCompleted arrived before TurnStarted"],
            await AgentConformance.CheckTurnAsync(provider, Deadline));
    }

    [Fact]
    public async Task ReportsAPermissionRequestThatNamesNoTargetAsync()
    {
        var provider = new ScriptedAgentProvider((session, turn) => AskingPermission(session, turn, ItemKind.Command, " "));

        Assert.Equal(["the permission for migrate names no target"], await AgentConformance.CheckTurnAsync(provider, Deadline));
    }

    [Fact]
    public async Task ReportsAPermissionRequestOfAnotherKindThanItsItemAsync()
    {
        var provider = new ScriptedAgentProvider((session, turn) => AskingPermission(session, turn, ItemKind.FileEdit, "dotnet ef database update"));

        Assert.Equal(
            ["the permission for migrate is for a FileEdit but its item is a Command"],
            await AgentConformance.CheckTurnAsync(provider, Deadline));
    }

    [Theory]
    [InlineData(PermissionMode.AskEveryTime, "the FileEdit edit went ahead without asking permission|the Command test went ahead without asking permission")]
    [InlineData(PermissionMode.AllowEdits, "")]
    public async Task ReportsActionsThatDoNotAskPermissionOnlyWhenAskedToAskEveryTimeAsync(PermissionMode mode, string expected)
    {
        var provider = new ScriptedAgentProvider((session, turn) =>
        [
            new TurnStarted(session, turn),
            new ItemStarted(session, turn, new ItemId("edit"), ItemKind.FileEdit, "Edit GREETING.md"),
            new ItemProgressed(session, turn, new ItemId("edit"), "# Hello"),
            new ItemCompleted(session, turn, new ItemId("edit"), ItemOutcome.Succeeded),
            new ItemStarted(session, turn, new ItemId("test"), ItemKind.Command, "dotnet test"),
            new ItemCompleted(session, turn, new ItemId("test"), ItemOutcome.Succeeded),
            new TurnCompleted(session, turn, TurnOutcome.Finished),
        ]);

        var violations = await AgentConformance.CheckTurnAsync(provider, new SessionOptions(".", mode), new UserTurn("conformance"), Deadline);

        Assert.Equal(expected, string.Join('|', violations));
    }

    [Fact]
    public async Task AnActionThatAsksBeforeItGoesAheadConformsWhenAskedToAskEveryTimeAsync()
    {
        var provider = new ScriptedAgentProvider((session, turn) => AskingPermission(session, turn, ItemKind.Command, "dotnet ef database update"));

        Assert.Empty(await AgentConformance.CheckTurnAsync(
            provider,
            new SessionOptions(".", PermissionMode.AskEveryTime),
            new UserTurn("conformance"),
            Deadline));
    }

    [Fact]
    public async Task ReportsATurnThatNeverEndsAsync()
    {
        var provider = new ScriptedAgentProvider((session, turn) => [new TurnStarted(session, turn)]);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Deadline);

        var check = AgentConformance.CheckTurnAsync(provider, deadline.Token);
        await deadline.CancelAsync();

        Assert.Equal(["the turn did not complete before the deadline"], await check);
    }

    [Fact]
    public async Task ReportsAResumeTokenFromAProviderThatDoesNotDeclareCanResumeAsync()
    {
        var provider = new ScriptedAgentProvider(IssuingAToken);

        Assert.Equal(
            ["a resume token was issued although the provider does not declare CanResume"],
            await AgentConformance.CheckTurnAsync(provider, Deadline));
    }

    [Theory]
    [InlineData(true, false, "")]
    [InlineData(false, false, "no resume token was issued although the provider declares CanResume")]
    [InlineData(true, true, "the resume token was not accepted: CannotResume")]
    public async Task AProviderThatDeclaresCanResumeMustIssueATokenAndAcceptItAsync(bool issues, bool rejects, string expected)
    {
        var provider = new ScriptedAgentProvider(issues ? IssuingAToken : ScriptedAgentProvider.Reply)
        {
            Capabilities = Declared with { CanResume = true },
            RejectsResume = rejects,
        };

        var violations = await AgentConformance.CheckResumeAsync(provider, Options, new UserTurn("conformance"), Deadline);

        Assert.Equal(expected, string.Join('|', violations));
    }

    [Fact]
    public async Task ReportsACanvasDrawnWithoutTheCanvasToolAsync()
    {
        var provider = new ScriptedAgentProvider(Drawing);

        Assert.Equal(["the canvas diagram was drawn without the canvas tool"], await AgentConformance.CheckTurnAsync(provider, Deadline));
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "no canvas was drawn through the canvas tool")]
    public async Task AProviderThatAcceptsToolsMustReportACallOfTheCanvasToolAsACanvasAsync(bool draws, string expected)
    {
        var provider = new ScriptedAgentProvider(draws ? Drawing : ScriptedAgentProvider.Reply)
        {
            Capabilities = Declared with { AcceptsTools = true },
        };

        var violations = await AgentConformance.CheckCanvasToolAsync(provider, Options, new UserTurn("conformance"), Deadline);

        Assert.Equal(expected, string.Join('|', violations));
        Assert.Equal([AgentConformance.CanvasTool], Assert.Single(provider.Sessions).Options.Tools);
    }

    [Fact]
    public async Task ReportsAnAccountThatChangesDuringTheSessionAsync()
    {
        var reads = 0;
        var provider = new ScriptedAgentProvider(ScriptedAgentProvider.Reply)
        {
            Account = () => new AgentAccount($"account-{Interlocked.Increment(ref reads)}", "Account"),
        };

        Assert.Equal(["the account changed during the session"], await AgentConformance.CheckTurnAsync(provider, Deadline));
    }

    private static readonly SessionOptions Options = new(".", PermissionMode.AllowAll);

    private static AgentCapabilities Declared { get; } = new ScriptedAgentProvider(ScriptedAgentProvider.Reply).Capabilities;

    private static IEnumerable<IAgentEvent> IssuingAToken(SessionId session, TurnId turn) =>
    [
        new TurnStarted(session, turn),
        new ResumeTokenIssued(session, turn, new ResumeToken("conversation-1")),
        new TurnCompleted(session, turn, TurnOutcome.Finished),
    ];

    private static IEnumerable<IAgentEvent> Drawing(SessionId session, TurnId turn) =>
    [
        new TurnStarted(session, turn),
        new CanvasStarted(session, turn, new ItemId("diagram"), "Architecture", "text/vnd.mermaid"),
        new ItemProgressed(session, turn, new ItemId("diagram"), "flowchart LR\n"),
        new ItemCompleted(session, turn, new ItemId("diagram"), ItemOutcome.Succeeded),
        new TurnCompleted(session, turn, TurnOutcome.Finished),
    ];

    private static IEnumerable<IAgentEvent> AskingPermission(SessionId session, TurnId turn, ItemKind kind, string target)
    {
        var item = new ItemId("migrate");

        return
        [
            new TurnStarted(session, turn),
            new ItemStarted(session, turn, item, ItemKind.Command, "dotnet ef database update"),
            new PermissionRequested(session, turn, item, "Run dotnet ef database update", kind, target),
            new PermissionResolved(session, turn, item, PermissionAnswer.Allow),
            new ItemCompleted(session, turn, item, ItemOutcome.Succeeded),
            new TurnCompleted(session, turn, TurnOutcome.Finished),
        ];
    }
}
