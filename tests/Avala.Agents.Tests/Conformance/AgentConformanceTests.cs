using Avala.Agents.Contracts.Connections;
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

    [Theory]
    [InlineData(TurnOutcome.Failed)]
    [InlineData(TurnOutcome.Interrupted)]
    public async Task ReportsATurnThatEndsWithoutFinishingAsync(TurnOutcome outcome)
    {
        var provider = new ScriptedAgentProvider((session, turn) => [new TurnStarted(session, turn), new TurnCompleted(session, turn, outcome)]);

        Assert.Equal([$"the turn ended {outcome}"], await AgentConformance.CheckTurnAsync(provider, Deadline));
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
    public async Task ReportsACallOfAToolTheSessionWasNotGivenAsync()
    {
        var provider = new ScriptedAgentProvider(Calling);

        Assert.Equal(["the tool propose_follow_up was called although the session was not given it"], await AgentConformance.CheckTurnAsync(provider, Deadline));
    }

    [Theory]
    [InlineData(true, "a result for a call that is not pending was accepted")]
    [InlineData(false, "no call of the tool propose_follow_up was made")]
    public async Task AProviderThatAcceptsToolsMustAcceptOnlyResultsOfPendingCallsAndReportThemAsync(bool calls, string expected)
    {
        var provider = new ScriptedAgentProvider(calls ? Calling : ScriptedAgentProvider.Reply)
        {
            Capabilities = Declared with { AcceptsTools = true },
        };

        var violations = await AgentConformance.CheckHarnessToolAsync(provider, Options, new UserTurn("conformance"), Deadline);

        Assert.Equal(expected, string.Join('|', violations));
        Assert.Equal([AgentConformance.ExecutedTool], Assert.Single(provider.Sessions).Options.Tools);
    }

    [Theory]
    [InlineData(false, "fewer than two calls of the tool propose_follow_up were pending at once")]
    [InlineData(true, "the result of the call second was not reported")]
    public async Task AProviderThatAcceptsToolsMustHoldTwoCallsAtOnceAndReportEachResultAsync(bool twice, string expected)
    {
        var provider = new ScriptedAgentProvider(twice ? CallingTwiceReportingOne : Calling)
        {
            Capabilities = Declared with { AcceptsTools = true },
        };

        var violations = await AgentConformance.CheckParallelToolCallsAsync(
            provider,
            Options,
            AgentConformance.ExecutedTool,
            new UserTurn("conformance"),
            Deadline);

        Assert.Equal(expected, string.Join('|', violations));
    }

    [Fact]
    public async Task ReportsTwoConnectionsThatShareAnAccountAResumeTokenOrAConversationAsync()
    {
        var provider = new ScriptedAgentProvider(IssuingAToken)
        {
            Capabilities = Declared with { CanResume = true },
            Account = () => new AgentAccount("shared", "Shared"),
        };

        var violations = await AgentConformance.CheckConnectionsAsync(
            provider,
            Options with { Connection = new ConnectionEnvironment { ConfigurationDirectory = "/logins/work" } },
            Options with { Connection = new ConnectionEnvironment { ConfigurationDirectory = "/logins/personal" } },
            new UserTurn("conformance"),
            Deadline);

        Assert.Equal(
            [
                "the two connections share the account shared",
                "the two connections share the resume token conversation-1",
                "a resume token of one connection was accepted on another",
            ],
            violations);
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

    [Fact]
    public async Task ReportsAFormFromAProviderThatDoesNotDeclareAsksQuestionsAsync()
    {
        var provider = new ScriptedAgentProvider((session, turn) => Asking(session, turn, Question));

        Assert.Equal(
            ["the form question was asked although the provider does not declare AsksQuestions"],
            await AgentConformance.CheckTurnAsync(provider, Deadline));
    }

    [Fact]
    public async Task ReportsAMalformedFormAsync()
    {
        var provider = new ScriptedAgentProvider((session, turn) => Asking(session, turn, Question with { Fields = [] }))
        {
            Capabilities = Declared with { AsksQuestions = true },
        };

        Assert.Equal(
            ["FormRequested was rejected: MalformedForm", "FormAnswered was rejected: NoPendingForm", "ItemCompleted was rejected: UnknownItem"],
            await AgentConformance.CheckTurnAsync(provider, Deadline));
    }

    [Theory]
    [InlineData(true, "an answer to a form that is not open was accepted|an answer to a form that was already answered was accepted")]
    [InlineData(false, "no form was asked although the provider declares AsksQuestions")]
    public async Task AProviderThatAsksQuestionsMustAskAndRefuseAnswersToFormsThatAreNotOpenAsync(bool asks, string expected)
    {
        var provider = new ScriptedAgentProvider(asks ? (session, turn) => Asking(session, turn, Question) : ScriptedAgentProvider.Reply)
        {
            Capabilities = Declared with { AsksQuestions = true },
        };

        var violations = await AgentConformance.CheckFormsAsync(provider, Options, new UserTurn("conformance"), Deadline);

        Assert.Equal(expected, string.Join('|', violations));
    }

    [Theory]
    [InlineData(true, "the denied item went ahead: ItemCompleted")]
    [InlineData(false, "no permission was requested to deny")]
    public async Task ReportsADeniedActionThatGoesAheadAsync(bool asks, string expected)
    {
        var provider = new ScriptedAgentProvider(asks ? (session, turn) => AskingPermission(session, turn, ItemKind.Command, "dotnet ef") : ScriptedAgentProvider.Reply);

        var violations = await AgentConformance.CheckDenialAsync(provider, Options, new UserTurn("conformance"), Deadline);

        Assert.Equal(expected, string.Join('|', violations));
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "no process was started through the session's launcher")]
    public async Task AProviderMustStartItsProcessesThroughTheSessionsLauncherAsync(bool launches, string expected)
    {
        var provider = new ScriptedAgentProvider(ScriptedAgentProvider.Reply)
        {
            Launching = options =>
            {
                if (launches)
                {
                    _ = options.Processes.Start(Workloads.Command("work", "0"));
                }
            },
        };

        var violations = await AgentConformance.CheckProcessesAsync(provider, Options, new UserTurn("conformance"), Deadline);

        Assert.Equal(expected, string.Join('|', violations));
    }

    private static readonly AgentForm Question = new(
        FormPurpose.Question,
        "Choose a database",
        "The service needs storage.",
        [new FormField("database", "Database", "Which database?", FieldKind.SingleChoice, [new FormOption("SQLite", "A file.")])]);

    private static IEnumerable<IAgentEvent> Asking(SessionId session, TurnId turn, AgentForm form)
    {
        var item = new ItemId("question");

        return
        [
            new TurnStarted(session, turn),
            new FormRequested(session, turn, item, form),
            new FormAnswered(session, turn, item, new FormAnswer(item, [new FieldAnswer("database") { Chosen = ["SQLite"] }])),
            new ItemCompleted(session, turn, item, ItemOutcome.Succeeded),
            new TurnCompleted(session, turn, TurnOutcome.Finished),
        ];
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

    private static IEnumerable<IAgentEvent> Calling(SessionId session, TurnId turn)
    {
        var item = new ItemId("propose");

        return
        [
            new TurnStarted(session, turn),
            new ToolCalled(session, turn, item, AgentConformance.ExecutedTool.Name, """{ "instruction": "Document it" }"""),
            new ToolReturned(session, turn, item, AgentConformance.KitResult(item)),
            new ItemCompleted(session, turn, item, ItemOutcome.Succeeded),
            new TurnCompleted(session, turn, TurnOutcome.Finished),
        ];
    }

    private static IEnumerable<IAgentEvent> CallingTwiceReportingOne(SessionId session, TurnId turn)
    {
        var first = new ItemId("first");
        var second = new ItemId("second");

        return
        [
            new TurnStarted(session, turn),
            new ToolCalled(session, turn, first, AgentConformance.ExecutedTool.Name, """{ "instruction": "Document it" }"""),
            new ToolCalled(session, turn, second, AgentConformance.ExecutedTool.Name, """{ "instruction": "Test it" }"""),
            new ToolReturned(session, turn, first, AgentConformance.KitResult(first)),
            new ItemCompleted(session, turn, first, ItemOutcome.Succeeded),
            new ItemCompleted(session, turn, second, ItemOutcome.Failed),
            new TurnCompleted(session, turn, TurnOutcome.Finished),
        ];
    }

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
