using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Permissions.Answering;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Permissions.Policies;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Permissions.Tests.Governance;

public sealed class SessionGovernorTests
{
    private static readonly PolicyRule AllowEverything =
        new(RuleOrigin.Repository, "everything", Option<ItemKind>.None, Option<string>.None, RuleScope.Anywhere, PolicyAnswer.Allow);

    private static readonly PermissionRequest Migration = new(ItemKind.Command, "dotnet ef database update", InsideWorkspace: false);

    private readonly GovernanceBook book = new();
    private readonly RecordingBus bus = new();
    private readonly SessionId session = SessionId.New();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARepositoryPolicyGovernsTheSessionItIsFoundInAsync()
    {
        await OpenAsync(Option<IReadOnlyList<PolicyRule>>.Some([AllowEverything]));

        var governed = book.Of(session);
        Assert.Equal("/worktrees/1", Outcomes.Present(governed.WorkingDirectory));
        Assert.Equal(PolicyAnswer.Allow, governed.Policy.Decide(Migration).Answer);
        Assert.Equal(
            new PolicyLoaded(new SessionPolicy(session, PolicyFileStatus.Applied, Option<PolicyError>.None, governed.Policy.Rules, CommittedFiles.Origin())),
            Assert.Single(bus.Published));
        Assert.Contains(AllowEverything, governed.Policy.Rules);
    }

    [Fact]
    public async Task ASessionWithoutAPolicyFileIsGovernedByTheBuiltInPolicyAsync()
    {
        await OpenAsync(Option<IReadOnlyList<PolicyRule>>.None);

        Assert.Equal(PermissionPolicy.BuiltIn, book.Of(session).Policy);
        Assert.Equal(PolicyFileStatus.Absent, Assert.IsType<PolicyLoaded>(Assert.Single(bus.Published)).Policy.File);
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
        var governor = await OpenAsync(Option<IReadOnlyList<PolicyRule>>.Some([AllowEverything]));

        await governor.HandleAsync(
            new AgentActivity(new PermissionRequested(session, TurnId.New(), new ItemId("migrate"), "Run", ItemKind.Command, "dotnet ef database update")),
            Cancellation);

        var decision = Assert.IsType<PermissionDecided>(bus.Published[^1]).Decision;
        Assert.Equal((PolicyAnswer.Allow, Option<PolicyRule>.Some(AllowEverything)), (decision.Answer, decision.Rule));
    }

    private async Task<SessionGovernor> OpenAsync(Result<Option<IReadOnlyList<PolicyRule>>, PolicyError> file)
    {
        var governor = new SessionGovernor(book, new FixedPolicyFiles(file), new PermissionResponder(new SilentAgents(), TimeProvider.System), bus);

        await governor.HandleAsync(new SessionOpened(session, new ProviderInfo("agent", "Agent"), "/worktrees/1"), Cancellation);

        return governor;
    }

    private sealed class SilentAgents : IAgents
    {
        public ValueTask<Result<ItemId, AgentError>> RespondAsync(SessionId session, PermissionDecision decision, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<ItemId, AgentError>.Success(decision.Item));

        public ValueTask<Result<SessionId, AgentError>> OpenAsync(AgentRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<SessionId, AgentError>.Failure(AgentError.Unsupported));

        public ValueTask<Result<AgentTurn, AgentError>> SendAsync(SessionId session, string message, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<AgentTurn, AgentError>.Failure(AgentError.Unsupported));

        public ValueTask<Result<TurnId, AgentError>> InterruptAsync(SessionId session, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<TurnId, AgentError>.Failure(AgentError.Unsupported));

        public ValueTask<Result<SessionId, AgentError>> StopAsync(SessionId session, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<SessionId, AgentError>.Failure(AgentError.Unsupported));
    }

    private sealed class FixedPolicyFiles(Result<Option<IReadOnlyList<PolicyRule>>, PolicyError> file) : IPolicyFiles
    {
        public ValueTask<PolicyFile> ReadAsync(string workingDirectory, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new PolicyFile(CommittedFiles.Origin(), file));
    }
}
