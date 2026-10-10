using Avala.Delegation.Contracts;
using Avala.Delegation.Policy;
using Avala.Delegation.RepositoryFiles;
using Avala.Delegation.Tests.Delegating;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Delegation.Tests.RepositoryFiles;

public sealed class DelegationRulesReaderTests
{
    private const string Worktree = "/worktrees/1";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheDelegationSectionIsReadFromTheJobFileOfTheRulesCommitAsync()
    {
        var committed = new CommittedFiles().With(Worktree, DelegationRulesReader.JobFile, """{ "delegation": { "maxDepth": 2 } }""", editedInWorktree: true);

        var rules = Outcomes.Present(Outcomes.Succeeds(await new DelegationRulesReader(committed, new FixedMachine()).OfWorktreeAsync(Worktree, Cancellation)));

        Assert.Equal(2, rules.MaxDepth);
        Assert.Equal([(Worktree, DelegationRulesReader.JobFile)], committed.Reads);
    }

    [Fact]
    public async Task ABaseCommitWithoutAJobFileDeclaresNoDelegationAsync() =>
        Assert.Equal(
            Option<DelegationRules>.None,
            Outcomes.Succeeds(await new DelegationRulesReader(new CommittedFiles().Workspace(Worktree), new FixedMachine()).OfWorktreeAsync(Worktree, Cancellation)));

    [Fact]
    public async Task AJobFileThatCannotBeReadOrIsTooLargeIsRejectedAsync()
    {
        var failing = new DelegationRulesReader(new CommittedFiles().Failing(Worktree, WorkspaceFailure.GitFailed), new FixedMachine());
        var large = new DelegationRulesReader(new CommittedFiles().With(Worktree, DelegationRulesReader.JobFile, new string(' ', DelegationRulesReader.MaximumBytes + 1)), new FixedMachine());

        Assert.Equal(DelegationError.Unreadable, Outcomes.FailsWith(await failing.OfWorktreeAsync(Worktree, Cancellation)));
        Assert.Equal(DelegationError.TooLarge, Outcomes.FailsWith(await large.OfWorktreeAsync(Worktree, Cancellation)));
    }
}
