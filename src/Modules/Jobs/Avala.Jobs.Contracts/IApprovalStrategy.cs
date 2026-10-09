using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Workspaces.Contracts;

namespace Avala.Jobs.Contracts;

public interface IApprovalStrategy
{
    string Name { get; }

    ValueTask<Result<ApprovalDelivery, JobRejection>> DeliverAsync(ApprovalRequest request, CancellationToken cancellationToken);
}

public sealed record ApprovalRequest(JobId Job, string Instruction, int Attempts, WorkspaceInfo Workspace);

public sealed record ApprovalDelivery(string Strategy, string Branch, Option<string> Commit);

public sealed record JobApproval(JobId Job, ApprovalDelivery Delivery);

public sealed record JobApproved(JobApproval Approval) : IIntegrationEvent;
