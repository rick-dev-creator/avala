using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Verification.Discovering;

internal sealed class ScoutOffer(IWorkspaces workspaces, IJobCatalog catalog) : IHarnessToolGate
{
    public async ValueTask<bool> OffersAsync(HarnessTool tool, string workingDirectory, CancellationToken cancellationToken) =>
        tool.Name != ChecksProposal.Tool
        || await (await workspaces.FindAtAsync(workingDirectory, cancellationToken)).Match(
            async workspace => (await catalog.ListAsync(cancellationToken)).Any(job => job.Companion
                && job.Workspace == Option<WorkspaceId>.Some(workspace.Id)
                && job.Instruction.StartsWith(ScoutBrief.Opening, StringComparison.Ordinal)),
            _ => Task.FromResult(false));
}
