using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
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
            new PolicyLoaded(new SessionPolicy(session, PolicyFileStatus.Applied, Option<PolicyError>.None, governed.Policy.Rules)),
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

    private async Task OpenAsync(Result<Option<IReadOnlyList<PolicyRule>>, PolicyError> file)
    {
        var governor = new SessionGovernor(book, new FixedPolicyFiles(file), bus);

        await governor.HandleAsync(new SessionOpened(session, new ProviderInfo("agent", "Agent"), "/worktrees/1"), Cancellation);
    }

    private sealed class FixedPolicyFiles(Result<Option<IReadOnlyList<PolicyRule>>, PolicyError> file) : IPolicyFiles
    {
        public ValueTask<Result<Option<IReadOnlyList<PolicyRule>>, PolicyError>> ReadAsync(string workingDirectory, CancellationToken cancellationToken) =>
            ValueTask.FromResult(file);
    }
}
