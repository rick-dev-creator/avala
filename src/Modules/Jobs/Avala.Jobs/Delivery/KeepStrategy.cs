using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Jobs.Delivery;

internal sealed class KeepStrategy : IApprovalStrategy
{
    public const string Keep = "keep";

    public string Name => Keep;

    public ValueTask<Result<ApprovalDelivery, JobRejection>> DeliverAsync(ApprovalRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Result<ApprovalDelivery, JobRejection>.Success(new ApprovalDelivery(Keep, request.Workspace.Branch, Option<string>.None)));
}
