using System.Collections.Immutable;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Permissions.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Verification.Contracts;
using Avala.Workbench.Board;
using Avala.Workbench.Following;
using Avala.Workbench.Timeline;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests;

internal static class Pages
{
    public static ProviderInfo Simulator { get; } = new("simulator", "Simulator");

    public static UsageSummary Used(decimal dollars, params UsageLimit[] limits) =>
        new(new TokenUsage(100, 20, 30, 4, 5), [new Cost(dollars, "USD")], 0, new TurnTally(1, 0, 0, TimeSpan.FromSeconds(3)), limits);

    public static JobSummary Summary(string instruction, JobStatus status, string? connection = null, JobId? parent = null) =>
        new(JobId.New(), "/repositories/shop", instruction, DateTimeOffset.UnixEpoch, status, connection is null ? Option<ConnectionName>.None : new ConnectionName(connection), Option<Autonomy>.None, Option<WorkspaceId>.None)
        {
            Parent = parent is { } found ? found : Option<JobId>.None,
        };

    public static JobBoard Board(params JobSummary[] jobs)
    {
        var board = new JobBoard();
        board.Publish(jobs.ToImmutableDictionary(job => job.Job, job => new BoardJob(job, Transcript.Empty)));

        return board;
    }

    public static async Task<SessionId> OpenAsync(this SessionBook sessions, string connection, JobId job, string account = "work@example.com")
    {
        var session = SessionId.New();
        await sessions.HandleAsync(
            new SessionOpened(session, Simulator, "/worktrees/1", new ConnectionName(connection)) { Account = new AgentAccount(account, account) },
            CancellationToken.None);
        await sessions.HandleAsync(new JobSessionStarted(job, session), CancellationToken.None);

        return session;
    }
}

internal sealed class FakeConnections(params string[] names) : IConnections
{
    public ConnectionCatalog Catalog { get; set; } = new ConnectionCatalog(
        ConnectionFileStatus.Applied,
        Option<ConnectionError>.None,
        [.. names.Select(name => new DeclaredConnection(new ConnectionName(name), "simulator", "login"))],
        names.Length > 0 ? new ConnectionName(names[0]) : Option<ConnectionName>.None)
    {
        DefaultMode = DefaultMode.Fixed,
    };

    public List<Option<ConnectionName>> Changes { get; } = [];

    public Option<ConnectionError> Refusal { get; set; }

    public FakeConnections Automatic()
    {
        Catalog = Catalog with { DefaultMode = DefaultMode.Auto };

        return this;
    }

    public ValueTask<ConnectionCatalog> CatalogAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Catalog);

    public ValueTask<Result<ConnectionInfo, ConnectionError>> CheckAsync(Option<ConnectionName> connection, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<Result<ConnectionCatalog, ConnectionError>> ChangeDefaultAsync(Option<ConnectionName> connection, CancellationToken cancellationToken)
    {
        Changes.Add(connection);

        if (Refusal.IsSome)
        {
            return ValueTask.FromResult(Refusal.Match(Result<ConnectionCatalog, ConnectionError>.Failure, () => throw new InvalidOperationException()));
        }

        Catalog = Catalog with
        {
            File = ConnectionFileStatus.Applied,
            Error = Option<ConnectionError>.None,
            Default = connection.IsSome ? connection : Catalog.Connections.Select(declared => Option<ConnectionName>.Some(declared.Name)).FirstOrDefault(),
            DefaultMode = connection.IsSome ? DefaultMode.Fixed : DefaultMode.Auto,
        };

        return ValueTask.FromResult(Result<ConnectionCatalog, ConnectionError>.Success(Catalog));
    }
}

internal sealed class FakePreview : IConnectionPreview
{
    public Result<ConnectionPreview, JobRejection> Answer { get; set; } = new ConnectionPreview(ConnectionRoute.Fallback, Option<ConnectionName>.None);

    public List<string> Asked { get; } = [];

    public Dictionary<string, TaskCompletionSource<Result<ConnectionPreview, JobRejection>>> Pending { get; } = new(StringComparer.Ordinal);

    public static ConnectionPreview ByCapacity(string chosen, ChoiceReason reason, params (string Connection, double Used, bool Available)[] compared) =>
        new(ConnectionRoute.Capacity, new ConnectionName(chosen))
        {
            Choice = new ConnectionChoice(
                new ConnectionName(chosen),
                reason,
                [.. compared.Select(candidate => new CandidateCapacity(
                    new ConnectionName(candidate.Connection),
                    candidate.Used,
                    candidate.Used > 0 ? new UsageLimit("5h", candidate.Used, Option<DateTimeOffset>.None) : Option<UsageLimit>.None,
                    0.9,
                    candidate.Available))],
                DateTimeOffset.UnixEpoch),
        };

    public ValueTask<Result<ConnectionPreview, JobRejection>> PreviewAsync(string repository, CancellationToken cancellationToken)
    {
        Asked.Add(repository);

        return Pending.TryGetValue(repository, out var pending) ? new ValueTask<Result<ConnectionPreview, JobRejection>>(pending.Task) : ValueTask.FromResult(Answer);
    }
}

internal sealed class FakeUsage : IUsage
{
    public List<ConnectionUsage> Connections { get; } = [];

    public Dictionary<JobId, UsageSummary> Jobs { get; } = [];

    public IReadOnlyList<ProviderUsage> ByProvider() => [];

    public IReadOnlyList<AccountUsage> ByAccount() => [];

    public IReadOnlyList<ConnectionUsage> ByConnection() => Connections;

    public Option<UsageSummary> OfSession(SessionId session) => Option<UsageSummary>.None;

    public Option<UsageSummary> OfJob(JobId job) => Jobs.TryGetValue(job, out var usage) ? usage : Option<UsageSummary>.None;
}

internal sealed class FakeUsageHistory : IUsageHistory
{
    public List<(DateTimeOffset From, DateTimeOffset To)> Within { get; } = [];

    public List<(DateOnly First, DateOnly Last)> Daily { get; } = [];

    public ValueTask<UsagePeriod> WithinAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        Within.Add((from, to));

        return ValueTask.FromResult(new UsagePeriod(from, to, Pages.Used(7m), [], []));
    }

    public Func<DateOnly, UsageSummary> Day { get; init; } = _ => Pages.Used(2m) with { UnpricedReports = 3 };

    public ValueTask<IReadOnlyList<UsagePeriod>> DailyAsync(DateOnly first, DateOnly last, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        Daily.Add((first, last));

        return ValueTask.FromResult<IReadOnlyList<UsagePeriod>>(
        [
            .. Enumerable.Range(0, last.DayNumber - first.DayNumber + 1).Select(first.AddDays).Select(day =>
                new UsagePeriod(Midnight(day, zone), Midnight(day.AddDays(1), zone), Day(day), [], [])),
        ]);
    }

    private static DateTimeOffset Midnight(DateOnly day, TimeZoneInfo zone) =>
        new(day.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(day.ToDateTime(TimeOnly.MinValue)));
}

internal sealed class FakeBudgets : IBudgets
{
    public Dictionary<SessionId, BudgetCaps> Caps { get; } = [];

    public Dictionary<JobId, BudgetCarve> Carves { get; } = [];

    public List<BudgetIntervention> Interventions { get; } = [];

    public Option<SessionBudget> BudgetOf(SessionId session) =>
        Caps.TryGetValue(session, out var caps)
            ? new SessionBudget(session, BudgetFileStatus.Applied, Option<BudgetError>.None, caps, Option<FileOrigin>.None)
            : Option<SessionBudget>.None;

    public IReadOnlyList<BudgetIntervention> OfJob(JobId job) => [.. Interventions.Where(intervention => intervention.Hold.Job == job)];

    public Option<BudgetCarve> CarveOf(JobId child) => Carves.TryGetValue(child, out var carve) ? carve : Option<BudgetCarve>.None;

    public ValueTask<MachineBudget> MachineAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
}

internal sealed class FakeSupervision : ISupervision
{
    public SupervisionSettings Settings { get; set; } = new(TimeSpan.FromMinutes(15), SettingsFileStatus.Absent, Option<SupervisionError>.None);

    public List<SupervisionIntervention> Interventions { get; } = [];

    public List<TimeSpan> Changes { get; } = [];

    public ValueTask<SupervisionSettings> SettingsAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Settings);

    public ValueTask<Result<SupervisionSettings, SupervisionError>> ChangeSilenceAsync(TimeSpan silence, CancellationToken cancellationToken)
    {
        Changes.Add(silence);

        if (silence <= TimeSpan.Zero || silence > TimeSpan.FromDays(1))
        {
            return ValueTask.FromResult(Result<SupervisionSettings, SupervisionError>.Failure(SupervisionError.InvalidSilence));
        }

        Settings = new SupervisionSettings(silence, SettingsFileStatus.Applied, Option<SupervisionError>.None);

        return ValueTask.FromResult(Result<SupervisionSettings, SupervisionError>.Success(Settings));
    }

    public IReadOnlyList<SupervisionIntervention> OfJob(JobId job) => [.. Interventions.Where(intervention => intervention.Hold.Job == job)];
}

internal sealed class TreeCatalog : IJobCatalog
{
    public List<JobSummary> Jobs { get; } = [];

    public ValueTask<IReadOnlyList<JobSummary>> ListAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<JobSummary>>(Jobs);

    public ValueTask<Option<JobHistory>> HistoryAsync(JobId job, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<IReadOnlyList<JobSummary>> ChildrenAsync(JobId parent, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Option<JobTree>> TreeAsync(JobId root, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Jobs.FirstOrDefault(job => job.Job == root) is { } found ? Option<JobTree>.Some(Tree(found)) : Option<JobTree>.None);

    private JobTree Tree(JobSummary job) => new(job, [.. Jobs.Where(child => child.Parent == Option<JobId>.Some(job.Job)).Select(Tree)]);
}

internal sealed class FakeDelegations : IDelegations
{
    public List<DelegationRecord> Records { get; } = [];

    public IReadOnlyList<DelegationRecord> All() => Records;

    public IReadOnlyList<DelegationRecord> OfParent(JobId parent) => [.. Records.Where(record => record.Parent == Option<JobId>.Some(parent))];

    public Option<DelegationRecord> OfChild(JobId child) =>
        Records.FirstOrDefault(record => record.Child == Option<JobId>.Some(child)) is { } found ? found : Option<DelegationRecord>.None;
}

internal sealed class FakeResources : IResources
{
    public Option<ResourceSample> Latest { get; set; }

    public ResourceUsage Usage { get; set; } = new(3, 512L * 1024 * 1024, TimeSpan.FromSeconds(4), 0.25, [24000], 2L * 1024 * 1024);

    public ResourceSettings Settings { get; set; } = new(
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(60),
        OrphanPolicy.Report,
        new PortRange(24000, 24999, 10),
        new WorktreeRetention(TimeSpan.Zero, Option<TimeSpan>.None, Option<TimeSpan>.None),
        ReconcilePolicy.Report);

    public List<PortLease> Leased { get; } = [];

    public ResourceUsage Global() => Usage;

    public ResourceUsage OfJob(JobId job) => Usage;

    public ResourceUsage OfSession(SessionId session) => Usage;

    public IReadOnlyList<ConnectionResources> ByConnection() => [];

    public IReadOnlyList<ProviderResources> ByProvider() => [];

    public IReadOnlyList<PortLease> Leases() => Leased;

    public IReadOnlyList<PortConflict> Conflicts() => [];

    public ValueTask<ResourceSettings> SettingsAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Settings);
}

internal sealed class FakeOrphans : IOrphans
{
    public List<OrphanReport> Reports { get; } = [];

    public List<JobId> Reaped { get; } = [];

    public IReadOnlyList<OrphanReport> Audit() => Reports;

    public IReadOnlyList<OrphanReport> OfJob(JobId job) => [.. Reports.Where(report => report.Job == Option<JobId>.Some(job))];

    public ValueTask<Result<IReadOnlyList<OrphanReport>, ResourceError>> ReapAsync(JobId job, CancellationToken cancellationToken)
    {
        Reaped.Add(job);
        var left = Reports.Where(report => report.Job == Option<JobId>.Some(job) && report.Disposal == OrphanDisposal.LeftRunning).ToList();

        return ValueTask.FromResult(left.Count == 0
            ? Result<IReadOnlyList<OrphanReport>, ResourceError>.Failure(ResourceError.NothingToReap)
            : Result<IReadOnlyList<OrphanReport>, ResourceError>.Success([.. left.Select(report => report with { Disposal = OrphanDisposal.Killed })]));
    }
}

internal sealed class FakeHousekeeping : IWorktreeHousekeeping
{
    public List<string> Calls { get; } = [];

    public IReadOnlyList<ReclaimedWorktree> Reclaimed() => [];

    public ValueTask<WorktreeReconciliation> ReconcileAsync(CancellationToken cancellationToken)
    {
        Calls.Add("reconcile");

        return ValueTask.FromResult(new WorktreeReconciliation(["/worktrees/stray"], []));
    }

    public ValueTask<WorktreeReconciliation> CleanAsync(CancellationToken cancellationToken)
    {
        Calls.Add("clean");

        return ValueTask.FromResult(new WorktreeReconciliation([], []));
    }
}

internal sealed class FakePolicies : IRepositoryPolicies
{
    public RepositoryPolicy Policy { get; set; } = Declaring(Autonomy.Autonomous);

    public List<string> Asked { get; } = [];

    public static RepositoryPolicy Declaring(Autonomy autonomy) =>
        new(PolicyFileStatus.Applied, Option<PolicyError>.None, [], Option<FileOrigin>.None) { Autonomy = autonomy };

    public ValueTask<RepositoryPolicy> OfRepositoryAsync(string repository, CancellationToken cancellationToken)
    {
        Asked.Add(repository);

        return ValueTask.FromResult(Policy);
    }
}

internal sealed class FakeOpener : IFileOpener
{
    public List<string> Opened { get; } = [];

    public Option<FileOpenError> Refusal { get; set; }

    public HashSet<string> Existing { get; } = [];

    public Dictionary<string, string> Created { get; } = [];

    public ValueTask<Result<OpenedFile, FileOpenError>> OpenAsync(string path, string template, CancellationToken cancellationToken)
    {
        Opened.Add(path);
        var created = Existing.Add(path);

        if (created)
        {
            Created[path] = template;
        }

        return ValueTask.FromResult(Refusal.Match(Result<OpenedFile, FileOpenError>.Failure, () => Result<OpenedFile, FileOpenError>.Success(new OpenedFile(path, created))));
    }
}

internal sealed class FakeRules : IRepositoryPolicies, IRepositoryBudgets, IRepositoryChecks
{
    public static FileOrigin Origin { get; } = new("0123456789abcdef0123456789abcdef01234567", false);

    public ValueTask<RepositoryPolicy> OfRepositoryAsync(string repository, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new RepositoryPolicy(
            PolicyFileStatus.Applied,
            Option<PolicyError>.None,
            [
                new PolicyRule(RuleOrigin.BuiltIn, "policy-file-goes-to-a-human", ItemKind.FileEdit, ".avala/permissions.json", RuleScope.Workspace, PolicyAnswer.Ask),
                new PolicyRule(RuleOrigin.Repository, "tests", ItemKind.Command, "dotnet test", RuleScope.Anywhere, PolicyAnswer.Allow),
            ],
            Origin)
        {
            Autonomy = Autonomy.Autonomous,
            Strategy = FormStrategy.BestJudgment,
        });

    ValueTask<RepositoryBudget> IRepositoryBudgets.OfRepositoryAsync(string repository, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new RepositoryBudget(
            BudgetFileStatus.Rejected,
            BudgetError.Malformed,
            new BudgetCaps([], Option<long>.None, Option<double>.None),
            [new ConnectionCaps(new ConnectionName("work"), new BudgetCaps([new Cost(2m, "USD")], Option<long>.None, 0.9))],
            Origin));

    ValueTask<RepositoryChecks> IRepositoryChecks.OfRepositoryAsync(string repository, CancellationToken cancellationToken) =>
        ValueTask.FromResult(new RepositoryChecks(ChecksFileStatus.Applied, [new CheckDeclared("build", "dotnet build", TimeSpan.FromSeconds(90))], Origin));
}

internal sealed class SubmittingJobs : IJobs
{
    public List<JobRequest> Requests { get; } = [];

    public Option<JobRejection> Refusal { get; set; }

    public ValueTask<Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        return ValueTask.FromResult(Refusal.Match(Result<JobId, JobRejection>.Failure, () => Result<JobId, JobRejection>.Success(JobId.New())));
    }

    public ValueTask<Result<JobHold, JobRejection>> HoldAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, string message, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<JobContinuation, JobRejection>> ContinueOnAsync(JobId job, ConnectionName connection, string message, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<Result<JobId, JobRejection>> DiscardAsync(JobId job, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<JobSteered, JobRejection>> SteerAsync(JobId job, string message, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<JobApproval, JobRejection>> ApproveAsync(JobId job, CancellationToken cancellationToken) => throw new NotSupportedException();

    public ValueTask<Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, string feedback, CancellationToken cancellationToken) => throw new NotSupportedException();
}
