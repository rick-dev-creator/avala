using Avala.Autopilot.Contracts;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Contests.Running;

internal sealed class FactGatherer(IRunEvidence evidence, IJobCatalog catalog, IWorkspaceChanges changes, IUsage usage)
{
    private static readonly ExceptionReason[] Judged = [ExceptionReason.VerificationNotPassed, ExceptionReason.RuleFileEdited];

    public async Task<CandidateFacts> GatherAsync(CandidateRecord candidate, CancellationToken cancellationToken)
    {
        var run = await evidence.OfJobAsync(candidate.Job, cancellationToken);
        var exceptions = run.Match(found => found.Exceptions, () => []);
        var verified = run.Bind(found => found.Summary.Verification) == Option<VerificationOutcome>.Some(VerificationOutcome.Passed);

        return new CandidateFacts(
            candidate.Job,
            candidate.Ordinal,
            candidate.Outcome == CandidateOutcome.AwaitingReview && verified,
            exceptions.Contains(ExceptionReason.RuleFileEdited),
            exceptions.Count(reason => !Judged.Contains(reason)),
            run.Match(found => found.Summary.Attempts, () => 0),
            await LinesChangedAsync(candidate.Job, cancellationToken),
            candidate.Settled.Match(at => at, () => DateTimeOffset.MaxValue))
        {
            Spent = usage.OfJob(candidate.Job).Match(found => found.Costs, () => []),
        };
    }

    private async Task<int> LinesChangedAsync(JobId job, CancellationToken cancellationToken) =>
        await (await catalog.HistoryAsync(job, cancellationToken)).Bind(history => history.Summary.Workspace).Match(
            async workspace => (await changes.DiffAsync(workspace, cancellationToken)).Match(
                diff => diff.Files.Sum(file => file.Added.Match(lines => lines, () => 0) + file.Removed.Match(lines => lines, () => 0)),
                _ => 0),
            () => Task.FromResult(0));
}
