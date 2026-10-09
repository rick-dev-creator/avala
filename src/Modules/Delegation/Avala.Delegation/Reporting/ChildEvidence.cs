using Avala.Agents.Contracts.Connections;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Delegation.Reporting;

internal sealed class ChildEvidence(IWorkspaceChanges changes, IVerifications verifications, IJobCatalog catalog, ChildSpending spending)
{
    public const int LongestSummary = 4_000;

    public async Task<ChildReport> GatherAsync(ChildReport report, Option<string> summary, CancellationToken cancellationToken)
    {
        var workspace = await WorkspaceOfAsync(report.Child, cancellationToken);
        var diff = await workspace.Match(
            found => changes.DiffAsync(found, cancellationToken).AsTask(),
            () => Task.FromResult(Result<WorkspaceDiff, WorkspaceFailure>.Failure(WorkspaceFailure.UnknownWorkspace)));

        return spending.Of(report with
        {
            Summary = summary.Map(text => text.Length <= LongestSummary ? text : $"{text[..LongestSummary]}[...]"),
            Files = diff.Match(found => found.Files, _ => []),
            Verification = verifications.OfJob(report.Child) is [.., var last] ? last : Option<VerificationReport>.None,
        });
    }

    public async Task<IReadOnlyList<string>> ConflictsAsync(JobId child, CancellationToken cancellationToken) =>
        await (await WorkspaceOfAsync(child, cancellationToken)).Match(
            async found => (await changes.ConflictsAsync(found, cancellationToken)).Match(files => files, _ => []),
            () => Task.FromResult<IReadOnlyList<string>>([]));

    public async Task<JobStatus> StatusOfAsync(JobId child, JobStatus fallback, CancellationToken cancellationToken) =>
        (await catalog.HistoryAsync(child, cancellationToken)).Match(history => history.Summary.Status, () => fallback);

    public async Task<Option<ConnectionName>> ConnectionOfAsync(JobId child, CancellationToken cancellationToken) =>
        (await catalog.HistoryAsync(child, cancellationToken)).Bind(history => history.Summary.Connection);

    private async Task<Option<WorkspaceId>> WorkspaceOfAsync(JobId child, CancellationToken cancellationToken) =>
        (await catalog.HistoryAsync(child, cancellationToken)).Bind(history => history.Summary.Workspace);
}
