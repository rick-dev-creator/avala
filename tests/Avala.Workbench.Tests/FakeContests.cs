using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Budgets.Contracts;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.ModelChoices;
using Avala.Workbench.NewJob;
using Avala.Workbench.Submitting;

namespace Avala.Workbench.Tests;

internal sealed class FakeContests : IContests
{
    public List<ContestRequest> Requests { get; } = [];

    public Result<BestOf, ContestRejection> Declared { get; set; } = BestOf.Default;

    public Option<ContestRejection> Refusal { get; set; }

    public List<ContestRecord> Contests { get; } = [];

    public static ContestRecord Contest(ContestStatus status, params JobId[] candidates) =>
        new(
            ContestId.New(),
            "/repositories/shop",
            "Fix JPY rounding in invoice totals",
            [.. candidates.Select((job, ordinal) => new CandidateRecord(job, ordinal + 1, Option<ConnectionName>.None, CandidateOutcome.Running, Option<DateTimeOffset>.None))],
            status,
            DateTimeOffset.UnixEpoch);

    public ValueTask<Result<ContestRecord, ContestRejection>> StartAsync(ContestRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        return ValueTask.FromResult(Refusal.Match(
            Result<ContestRecord, ContestRejection>.Failure,
            () => Result<ContestRecord, ContestRejection>.Success(Contest(ContestStatus.Running, [.. Enumerable.Range(0, request.Candidates).Select(_ => JobId.New())]))));
    }

    public ValueTask<Result<BestOf, ContestRejection>> BestOfAsync(string repository, CancellationToken cancellationToken) => ValueTask.FromResult(Declared);

    public Option<ContestRecord> Of(ContestId contest) => Contests.FirstOrDefault(found => found.Contest == contest).ToOption();

    public Option<ContestRecord> OfJob(JobId job) => Contests.FirstOrDefault(found => found.Candidates.Any(candidate => candidate.Job == job)).ToOption();

    public IReadOnlyList<ContestRecord> All() => Contests;
}

internal sealed class CappedBudgets(params Cost[] caps) : IRepositoryBudgets
{
    public ValueTask<RepositoryBudget> OfRepositoryAsync(string repository, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new RepositoryBudget(BudgetFileStatus.Applied, Option<BudgetError>.None, new BudgetCaps(caps, Option<long>.None, Option<double>.None), [], Option<Workspaces.Contracts.FileOrigin>.None));
}

internal static class Launching
{
    public static JobLaunch Launch(IJobs jobs, IConnections connections, IConnectionPreview preview, Permissions.Contracts.IRepositoryPolicies policies, FakeContests? contests = null) =>
        new(new Submissions(jobs, contests ?? new FakeContests()), connections, preview, policies);

    public static JobOptionsViewModel Options(FakeContests? contests = null, params Cost[] caps) =>
        new(new ModelPickerViewModel(), new ContestReadings(contests ?? new FakeContests(), new CappedBudgets(caps)));
}
