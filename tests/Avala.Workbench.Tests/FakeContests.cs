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
    public static JobLaunch Launch(IJobs jobs, IConnections connections, IConnectionPreview preview, Permissions.Contracts.IRepositoryPolicies policies, FakeContests? contests = null, RepositoryChoice.FakeScratch? scratch = null) =>
        new(new Submissions(jobs, contests ?? new FakeContests(), RepositoryChoice.Scratching.Folders(scratch ?? new RepositoryChoice.FakeScratch())), connections, preview, policies);

    public static JobOptionsViewModel Options(FakeContests? contests = null, params Cost[] caps) =>
        new(new ModelPickerViewModel(), new ContestReadings(contests ?? new FakeContests(), new CappedBudgets(caps)));

    public static FeatureChipViewModel Feature(FakeDocumentation? documentation = null) => new(documentation ?? new FakeDocumentation());
}

internal sealed class FakeDocumentation : Documentation.Contracts.IDocumentation
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(JobId Job, Documentation.Contracts.FeatureChoice Choice)> chosen = new();

    public Documentation.Contracts.RepositoryDocs Docs { get; set; } = Documentation.Contracts.RepositoryDocs.Off;

    public IReadOnlyList<(JobId Job, Documentation.Contracts.FeatureChoice Choice)> Chosen => [.. chosen];

    public Task<Documentation.Contracts.RepositoryDocs> OfRepositoryAsync(string repository, CancellationToken cancellationToken) => Task.FromResult(Docs);

    public Documentation.Contracts.FeatureSuggestion Suggest(string instruction, IReadOnlyList<Documentation.Contracts.Feature> features) =>
        features.FirstOrDefault(feature => instruction.Contains(feature.Title, StringComparison.OrdinalIgnoreCase)) is { } named
            ? new(named, false)
            : new(new Documentation.Contracts.Feature("new-feature", instruction.Length == 0 ? "Feature" : instruction), true);

    public Task ChooseAsync(JobId job, Documentation.Contracts.FeatureChoice choice, CancellationToken cancellationToken)
    {
        chosen.Enqueue((job, choice));

        return Task.CompletedTask;
    }

    public Option<Documentation.Contracts.JobDocumentation> OfJob(JobId job) => Option<Documentation.Contracts.JobDocumentation>.None;

    public Result<Documentation.Contracts.DocsRules, Documentation.Contracts.DocumentationError> Switched { get; set; } = new Documentation.Contracts.DocsRules(true, "docs/features");

    public Task<Result<Documentation.Contracts.DocsRules, Documentation.Contracts.DocumentationError>> SwitchAsync(string repository, bool enabled, CancellationToken cancellationToken) =>
        Task.FromResult(Switched.Map(rules => rules with { Enabled = enabled }));
}

internal sealed class FakeRoles : Delegation.Contracts.IRoleSettings
{
    public Agents.Contracts.Sessions.ModelChoice Research { get; set; } = Agents.Contracts.Sessions.ModelChoice.Default;

    public ValueTask<Agents.Contracts.Sessions.ModelChoice> OfRoleAsync(string worktree, Delegation.Contracts.ChildRole role, CancellationToken cancellationToken) =>
        ValueTask.FromResult(role == Delegation.Contracts.ChildRole.Research ? Research : Agents.Contracts.Sessions.ModelChoice.Default);
}
