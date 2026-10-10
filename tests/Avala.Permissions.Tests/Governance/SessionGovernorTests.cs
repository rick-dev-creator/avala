using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
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

namespace Avala.Permissions.Tests.Governance;

public sealed class SessionGovernorTests
{
    private static readonly PolicyRule AllowEverything =
        new(RuleOrigin.Repository, "everything", Option<ItemKind>.None, Option<string>.None, RuleScope.Anywhere, PolicyAnswer.Allow);

    private static readonly PermissionRequest Migration = new(ItemKind.Command, "dotnet ef database update", InsideWorkspace: false);

    private readonly InMemoryGovernance store = new();
    private readonly GovernanceBook book;

    private readonly RecordingBus bus = new();
    private readonly SessionId session = SessionId.New();

    public SessionGovernorTests() => book = new GovernanceBook(store);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARepositoryPolicyGovernsTheSessionItIsFoundInAsync()
    {
        await OpenAsync(PermissionPolicy.With([AllowEverything]) with { Declared = Autonomy.Autonomous, Strategy = FormStrategy.BestJudgment });

        var governed = book.Of(session);
        Assert.Equal("/worktrees/1", Outcomes.Present(governed.WorkingDirectory));
        Assert.Equal(PolicyAnswer.Allow, governed.Policy.Decide(Migration).Answer);
        var loaded = Assert.IsType<PolicyLoaded>(Assert.Single(bus.Published)).Policy;
        Assert.Equal(
            (session, PolicyFileStatus.Applied, Option<PolicyError>.None, CommittedFiles.Origin(), Autonomy.Autonomous, FormStrategy.BestJudgment),
            (loaded.Session, loaded.File, loaded.Error, loaded.Origin, loaded.Autonomy, loaded.Strategy));
        Assert.Equal(governed.Policy.Rules, loaded.Rules);
        Assert.Contains(AllowEverything, governed.Policy.Rules);
    }

    [Fact]
    public async Task ASessionWithoutAPolicyFileIsGovernedByTheBuiltInPolicyAsync()
    {
        await OpenAsync(Option<PermissionPolicy>.None);

        Assert.Equal(PermissionPolicy.BuiltIn, book.Of(session).Policy);
        var loaded = Assert.IsType<PolicyLoaded>(Assert.Single(bus.Published)).Policy;
        Assert.Equal((PolicyFileStatus.Absent, Autonomy.Supervised), (loaded.File, loaded.Autonomy));
    }

    [Fact]
    public async Task AnInvalidPolicyFileIsReportedAndFallsBackToTheBuiltInPolicyNeverToAllowAsync()
    {
        await OpenAsync(PolicyError.UnknownAnswer);

        var policy = Assert.IsType<PolicyLoaded>(Assert.Single(bus.Published)).Policy;
        Assert.Equal((PolicyFileStatus.Rejected, PolicyError.UnknownAnswer), (policy.File, Outcomes.Present(policy.Error)));
        Assert.Equal(PermissionPolicy.BuiltIn.Rules, policy.Rules);
        Assert.Equal(PolicyAnswer.Ask, book.Of(session).Policy.Decide(Migration).Answer);
    }

    [Fact]
    public async Task ARequestThatFollowsTheOpeningOfItsSessionIsDecidedByThePolicyItsFileLoadedAsync()
    {
        var governor = await OpenAsync(PermissionPolicy.With([AllowEverything]));

        await governor.HandleAsync(
            new AgentActivity(new PermissionRequested(session, TurnId.New(), new ItemId("migrate"), "Run", ItemKind.Command, "dotnet ef database update")),
            Cancellation);

        var decision = Assert.IsType<PermissionDecided>(bus.Published[^1]).Decision;
        Assert.Equal((PolicyAnswer.Allow, Option<PolicyRule>.Some(AllowEverything)), (decision.Answer, decision.Rule));
    }

    [Fact]
    public async Task ThePolicyTheAutonomyAndEveryDecisionAreStoredAsTheGovernorKeepsThemAsync()
    {
        var job = JobId.New();
        var governor = await OpenAsync(PermissionPolicy.With([AllowEverything]));
        await governor.HandleAsync(new JobSessionStarted(job, session), Cancellation);

        await governor.HandleAsync(
            new AgentActivity(new PermissionRequested(session, TurnId.New(), new ItemId("migrate"), "Run", ItemKind.Command, "dotnet ef database update")),
            Cancellation);

        Assert.Equal(
            [book.PolicyOf(session).Match<object>(policy => policy, () => string.Empty), book.AutonomyOf(session).Match<object>(autonomy => autonomy, () => string.Empty), book.OfJob(job)[0]],
            store.Recorded);
    }

    [Theory]
    [InlineData(Autonomy.Autonomous, null, Autonomy.Autonomous, false)]
    [InlineData(Autonomy.Autonomous, Autonomy.Supervised, Autonomy.Supervised, false)]
    [InlineData(Autonomy.Autonomous, Autonomy.Autonomous, Autonomy.Autonomous, false)]
    [InlineData(Autonomy.Supervised, Autonomy.Autonomous, Autonomy.Supervised, true)]
    [InlineData(Autonomy.Supervised, null, Autonomy.Supervised, false)]
    public async Task AJobMayTightenTheAutonomyOfItsRepositoryButNeverLoosenItAsync(
        Autonomy declared,
        Autonomy? requested,
        Autonomy effective,
        bool refused)
    {
        var job = JobId.New();
        var governor = await OpenAsync(PermissionPolicy.With([]) with { Declared = declared });

        await governor.HandleAsync(new JobSessionStarted(job, session) { Autonomy = requested.ToOption() }, Cancellation);

        var applied = new SessionAutonomy(session, job, declared, requested.ToOption(), effective, refused);
        Assert.Equal(new AutonomyApplied(applied), bus.Published[^1]);
        Assert.Equal(Option<SessionAutonomy>.Some(applied), book.AutonomyOf(session));
        Assert.Equal(effective, book.Of(session).Policy.Autonomy);
    }

    [Theory]
    [InlineData(JobStatus.Approved, true)]
    [InlineData(JobStatus.Discarded, true)]
    [InlineData(JobStatus.Failed, true)]
    [InlineData(JobStatus.AwaitingReview, false)]
    [InlineData(JobStatus.NeedsHelp, false)]
    public async Task AJobLosesItsRulesWhenItEndsAndTheEndIsStoredAsync(JobStatus status, bool ends)
    {
        var (job, other) = (JobId.New(), JobId.New());
        var rule = Remembering.ForJob(ItemKind.Command, "dotnet ef database update", PolicyAnswer.Allow);
        book.Remember(job, rule);
        book.Remember(other, rule);
        var governor = await OpenAsync(PermissionPolicy.BuiltIn);

        await governor.HandleAsync(new JobProgressed(job, status), Cancellation);

        Assert.Equal(ends ? [] : [rule], book.JobRulesOf(job));
        Assert.Equal([rule], book.JobRulesOf(other));
        Assert.Equal(ends ? [new EndedJob(job, status)] : [], store.Recorded.OfType<EndedJob>());
    }

    private Task<SessionGovernor> OpenAsync(PermissionPolicy policy) => OpenAsync(Option<PermissionPolicy>.Some(policy));

    private async Task<SessionGovernor> OpenAsync(Result<Option<PermissionPolicy>, PolicyError> file)
    {
        var governor = new SessionGovernor(book, new FixedPolicyFiles(file), new PermissionResponder(new AnsweringAgents(), new SymbolicLinks(), TimeProvider.System), bus);

        await governor.HandleAsync(new SessionOpened(session, new ProviderInfo("agent", "Agent"), "/worktrees/1", new ConnectionName("agent")), Cancellation);

        return governor;
    }

    private sealed class FixedPolicyFiles(Result<Option<PermissionPolicy>, PolicyError> file) : IPolicyFiles
    {
        public ValueTask<PolicyFile> ReadAsync(string workingDirectory, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PolicyFile(CommittedFiles.Origin(), file));
    }
}
