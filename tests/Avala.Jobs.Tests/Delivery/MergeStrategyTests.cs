using Avala.Jobs.Contracts;
using Avala.Jobs.Delivery;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Jobs.Tests.Delivery;

public sealed class MergeStrategyTests
{
    private static readonly WorkspaceInfo Workspace = new(WorkspaceId.New(), "/worktrees/1", "avala/1", new string('0', 40)) { BaseBranch = "main" };

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task MergingSquashesTheJobUnderAMessageDerivedFromItsInstructionAsync()
    {
        var job = JobId.New();
        var changes = new MergingChanges(new MergedWork(Workspace.Id, "main", "abc123", "/repos/shop"));

        var delivery = Outcomes.Succeeds(await new MergeStrategy(changes).DeliverAsync(new ApprovalRequest(job, "Add GitHub login", 2, Workspace), Cancellation));

        Assert.Equal(new ApprovalDelivery("merge", "main", "abc123"), delivery);
        Assert.Equal(
            [(Workspace.Id, $"Add GitHub login\n\nSquashed by Avala from the checkpoints of 2 attempts.\n\nAvala-Job: {job.Value}")],
            changes.Merges);
    }

    [Fact]
    public void ALongOrSeveralLineInstructionGetsAShortSubjectAndKeepsItsWholeTextInTheBody()
    {
        var job = JobId.New();
        var instruction = $"{new string('x', 80)}\nThen update the docs.";

        var message = MergeStrategy.MessageOf(new ApprovalRequest(job, instruction, 1, Workspace));

        Assert.Equal($"{new string('x', 69)}...\n\n{instruction}\n\nSquashed by Avala from the checkpoints of 1 attempt.\n\nAvala-Job: {job.Value}", message);
    }

    [Theory]
    [InlineData("NoBaseBranch", "NoBaseBranch")]
    [InlineData("MergeConflict", "MergeConflict")]
    [InlineData("BaseCheckoutDirty", "BaseCheckoutDirty")]
    [InlineData("BaseMoved", "BaseMoved")]
    [InlineData("UnknownWorkspace", "WorkspaceUnavailable")]
    [InlineData("GitFailed", "DeliveryFailed")]
    public async Task AMergeTheWorkspaceRefusesIsRejectedNamingTheProblemAsync(string failure, string rejection)
    {
        var changes = new MergingChanges(Enum.Parse<WorkspaceFailure>(failure));

        var refused = await new MergeStrategy(changes).DeliverAsync(new ApprovalRequest(JobId.New(), "Add GitHub login", 1, Workspace), Cancellation);

        Assert.Equal(Enum.Parse<JobRejection>(rejection), Outcomes.FailsWith(refused));
    }

    private sealed class MergingChanges(Result<MergedWork, WorkspaceFailure> outcome) : IWorkspaceChanges
    {
        private readonly List<(WorkspaceId Workspace, string Message)> merges = [];

        public IReadOnlyList<(WorkspaceId Workspace, string Message)> Merges => merges;

        public ValueTask<Result<WorkspaceDiff, WorkspaceFailure>> DiffAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<WorkspaceDiff, WorkspaceFailure>.Failure(WorkspaceFailure.GitFailed));

        public ValueTask<Result<FileDiff, WorkspaceFailure>> FileDiffAsync(WorkspaceId workspace, string path, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<FileDiff, WorkspaceFailure>.Failure(WorkspaceFailure.GitFailed));

        public ValueTask<Result<IReadOnlyList<string>, WorkspaceFailure>> ConflictsAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<IReadOnlyList<string>, WorkspaceFailure>.Failure(WorkspaceFailure.GitFailed));

        public ValueTask<Result<MergedWork, WorkspaceFailure>> MergeAsync(WorkspaceId workspace, string message, CancellationToken cancellationToken)
        {
            merges.Add((workspace, message));

            return ValueTask.FromResult(outcome);
        }
    }
}
