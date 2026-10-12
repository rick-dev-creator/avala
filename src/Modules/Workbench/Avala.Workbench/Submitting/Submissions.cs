using Avala.Budgets.Contracts;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Submitting;

internal sealed class Submissions(IJobs jobs, IContests contests, ScratchFolders scratch)
{
    public ValueTask<Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken) =>
        jobs.SubmitAsync(request, cancellationToken);

    public ValueTask<Result<ContestRecord, ContestRejection>> StartAsync(ContestRequest request, CancellationToken cancellationToken) =>
        contests.StartAsync(request, cancellationToken);

    public ValueTask<Result<string, WorkspaceFailure>> ScratchAsync(string instruction, CancellationToken cancellationToken) =>
        scratch.CreateAsync(instruction, cancellationToken);
}

internal sealed class ContestReadings(IContests contests, IRepositoryBudgets budgets)
{
    public async ValueTask<Result<BestOf, ContestRejection>> BestOfAsync(string repository, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(repository) ? BestOf.Default : await contests.BestOfAsync(repository.Trim(), cancellationToken);

    public async ValueTask<BudgetCaps> CapsAsync(string repository, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(repository)
            ? new BudgetCaps([], Option<long>.None, Option<double>.None)
            : (await budgets.OfRepositoryAsync(repository.Trim(), cancellationToken)).Caps;
}
