using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Answering;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Permissions.Links;
using Avala.Permissions.Policies;
using Avala.Sdk;
using Avala.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Permissions.Tests.Answering;

public sealed class DeniedRequestsTests
{
    private const string Issue = "mcp__github__create_issue";

    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly GovernanceBook book = new(new Tests.Governance.InMemoryGovernance());
    private readonly RecordingBus bus = new();
    private readonly SessionId session = SessionId.New();
    private readonly JobId job = JobId.New();
    private readonly SessionGovernor governor;
    private readonly DeniedRequests denied;

    public DeniedRequestsTests()
    {
        var clock = new FakeTimeProvider(Now);
        governor = new SessionGovernor(book, new SessionTerms(new NoPolicyFiles(), [], new SymbolicLinks()), new PermissionResponder(new AnsweringAgents(), new RequestPlaces(new SymbolicLinks(), new FixedReadableFolders([])), clock, []), bus);
        denied = new DeniedRequests(book, new AnswerLedger(book, bus, clock, new RecordedRounds()));
        var autonomous = PermissionPolicy.With([]) with { Declared = Autonomy.Autonomous };
        book.Keep(book.Of(session).OpenedIn(Path.GetTempPath(), autonomous, new SessionPolicy(session, PolicyFileStatus.Applied, Option<PolicyError>.None, [], CommittedFiles.Origin())).WorkingOn(job));
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AllowingARequestTheAutonomousPolicyDeniedKeepsAJobRuleForItsSubjectAndRecordsTheAnswerSoTheRetryIsAllowedAsync()
    {
        await RequestAsync("issue", ItemKind.Mcp, Issue);

        var answer = Outcomes.Succeeds(await denied.AllowAsync(session, new ItemId("issue"), Cancellation));
        await RequestAsync("again", ItemKind.Mcp, Issue);

        var rule = Outcomes.Present(answer.Rule);
        Assert.Equal(("always mcp__github__create_issue in this job", PolicyAnswer.Allow, RuleOrigin.Job), (rule.Name, rule.Answer, rule.Origin));
        Assert.Equal([rule], book.JobRulesOf(job));
        Assert.Contains(new PermissionAnswered(answer), bus.Published);
        var retried = Assert.IsType<PermissionDecided>(bus.Published[^1]).Decision;
        Assert.Equal((PolicyAnswer.Allow, Option<PolicyRule>.Some(rule)), (retried.Answer, retried.Rule));
    }

    [Fact]
    public async Task ARequestAGuardDeniedOrOneThatWasNotDeniedCannotBeAllowedAfterwardsAsync()
    {
        await RequestAsync("escape", ItemKind.FileEdit, Path.Combine("..", "escape.txt"));
        await RequestAsync("build", ItemKind.Command, "dotnet build");

        Assert.Equal(PolicyError.ScopeNotOffered, Outcomes.FailsWith(await denied.AllowAsync(session, new ItemId("escape"), Cancellation)));
        Assert.Equal(PolicyError.NotAwaitingAnswer, Outcomes.FailsWith(await denied.AllowAsync(session, new ItemId("build"), Cancellation)));
        Assert.Empty(book.JobRulesOf(job));
        Assert.Empty(book.AnswersOfJob(job));
    }

    private Task RequestAsync(string item, ItemKind kind, string target) =>
        governor.HandleAsync(new AgentActivity(new PermissionRequested(session, TurnId.New(), new ItemId(item), "Request", kind, target)), Cancellation).AsTask();
}
