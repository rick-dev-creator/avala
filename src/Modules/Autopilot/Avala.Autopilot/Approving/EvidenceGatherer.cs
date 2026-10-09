using Avala.Autopilot.Evidence;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Autopilot.Approving;

internal sealed class EvidenceGatherer(IVerifications verifications, IPermissionAudit audit, IJobCatalog catalog, JobWork work)
{
    public async Task<JobEvidence> GatherAsync(JobId job, CancellationToken cancellationToken) =>
        await (await catalog.HistoryAsync(job, cancellationToken)).Match(
            history => FromAsync(job, history, cancellationToken),
            () => Task.FromResult(JobEvidence.None with { Verifications = verifications.OfJob(job) }));

    private async Task<JobEvidence> FromAsync(JobId job, JobHistory history, CancellationToken cancellationToken)
    {
        var workspace = await work.WorkspaceAsync(history.Summary.Workspace, cancellationToken);
        var rules = await workspace.Match(
            found => work.RulesAsync(found.Path, cancellationToken),
            () => Task.FromResult(Option<AutopilotRules>.None));
        var changed = await workspace.Match(
            found => work.ChangedFilesAsync(found.Id, cancellationToken),
            () => Task.FromResult(Option<IReadOnlyList<string>>.None));

        return new JobEvidence(rules, verifications.OfJob(job), history.Attempts, changed)
        {
            Decisions = audit.OfJob(job),
            Answers = audit.AnswersOfJob(job),
            Forms = audit.FormsOfJob(job),
            RuleOrigins = [.. history.Sessions.SelectMany(session => audit.PolicyOf(session.Session).Bind(policy => policy.Origin).Match<FileOrigin[]>(origin => [origin], () => []))],
            Connection = history.Summary.Connection,
        };
    }
}
