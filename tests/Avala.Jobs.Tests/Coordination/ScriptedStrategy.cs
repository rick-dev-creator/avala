using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Jobs.Tests.Coordination;

internal sealed class ScriptedStrategy(string name, Result<ApprovalDelivery, JobRejection> outcome) : IApprovalStrategy
{
    private readonly List<ApprovalRequest> requests = [];

    public string Name => name;

    public IReadOnlyList<ApprovalRequest> Requests => requests;

    public ValueTask<Result<ApprovalDelivery, JobRejection>> DeliverAsync(ApprovalRequest request, CancellationToken cancellationToken)
    {
        requests.Add(request);

        return ValueTask.FromResult(outcome);
    }
}
