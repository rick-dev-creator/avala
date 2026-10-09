using System.Globalization;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Jobs.Delivery;

internal sealed class MergeStrategy(IWorkspaceChanges changes) : IApprovalStrategy
{
    public const string Merge = "merge";

    private const int SubjectLength = 72;

    public string Name => Merge;

    public async ValueTask<Result<ApprovalDelivery, JobRejection>> DeliverAsync(ApprovalRequest request, CancellationToken cancellationToken) =>
        (await changes.MergeAsync(request.Workspace.Id, MessageOf(request), cancellationToken))
            .Map(merged => new ApprovalDelivery(Merge, merged.BaseBranch, merged.Commit))
            .MapError(Rejection);

    public static string MessageOf(ApprovalRequest request)
    {
        var instruction = request.Instruction.Trim();
        var firstLine = instruction.Split('\n', 2)[0].Trim();
        var subject = firstLine.Length <= SubjectLength ? firstLine : $"{firstLine[..(SubjectLength - 3)].TrimEnd()}...";
        var body = subject == instruction ? string.Empty : $"{instruction}\n\n";
        var attempts = request.Attempts == 1 ? "1 attempt" : string.Create(CultureInfo.InvariantCulture, $"{request.Attempts} attempts");

        return $"{subject}\n\n{body}Squashed by Avala from the checkpoints of {attempts}.\n\nAvala-Job: {request.Job.Value}";
    }

    private static JobRejection Rejection(WorkspaceFailure failure) => failure switch
    {
        WorkspaceFailure.NoBaseBranch => JobRejection.NoBaseBranch,
        WorkspaceFailure.MergeConflict => JobRejection.MergeConflict,
        WorkspaceFailure.BaseCheckoutDirty => JobRejection.BaseCheckoutDirty,
        WorkspaceFailure.BaseMoved => JobRejection.BaseMoved,
        WorkspaceFailure.UnknownWorkspace => JobRejection.WorkspaceUnavailable,
        _ => JobRejection.DeliveryFailed,
    };
}
