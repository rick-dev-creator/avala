using Avala.Agents.Contracts.Connections;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Policy;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Handoffs.Briefing;

internal sealed record BriefRequest(JobHistory History, ConnectionName From, ConnectionName To, Option<LimitReason> Why, Option<string> Feedback);

internal sealed class BriefGatherer(IWorkspaceChanges changes, IVerifications verifications, IPermissionAudit audit, JobNotes notes)
{
    public async Task<string> WriteAsync(BriefRequest request, CancellationToken cancellationToken)
    {
        var job = request.History.Summary.Job;
        var noted = await notes.OfAsync(job, cancellationToken);
        var files = await request.History.Summary.Workspace.Match(
            async workspace => (await changes.DiffAsync(workspace, cancellationToken)).Match(
                diff => Option<IReadOnlyList<FileChange>>.Some(diff.Files),
                _ => Option<IReadOnlyList<FileChange>>.None),
            () => Task.FromResult(Option<IReadOnlyList<FileChange>>.None));
        var verified = verifications.OfJob(job);

        return HandoffBrief.Compose(new BriefFacts(request.History.Summary.Instruction, request.From, request.To, request.Why)
        {
            Plan = noted.Plan,
            Files = files,
            Verification = verified.Count > 0 ? verified[^1] : Option<VerificationReport>.None,
            Feedback = request.Feedback,
            Decisions = DecisionLines.Taken(audit, job),
            Questions = DecisionLines.Open(audit, job),
            LastMessage = noted.LastMessage,
        });
    }
}
