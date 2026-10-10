using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Budgets.Enforcement;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Budgets.Tests.Enforcement;

internal sealed class Budgeted
{
    public static readonly ProviderInfo Provider = new("agent", "Agent");

    public static readonly ConnectionName Connection = new("work");

    private readonly BudgetEnforcer enforcer;
    private readonly Usage usage = new();

    public Budgeted(Option<JobRejection> rejection = default)
    {
        Jobs = new HoldingJobs(rejection);
        Book = new BudgetBook(new FixedMachine(), Store);
        enforcer = new BudgetEnforcer(Book, usage, [Resources], new BudgetActions(Book, Jobs, Bus, Clock));
    }

    public MeasuredResources Resources { get; } = new();

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero));

    public RecordingBus Bus { get; } = new();

    public InMemoryInterventions Store { get; } = new();

    public BudgetBook Book { get; }

    public HoldingJobs Jobs { get; }

    public List<ConnectionName> ReadFor { get; } = [];

    public JobId Job { get; } = JobId.New();

    public SessionId Session { get; } = SessionId.New();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async Task OpenAsync(Result<Option<BudgetCaps>, BudgetError> file) => await OpenAsync(Session, file);

    public async Task OpenAsync(SessionId session, Result<Option<BudgetCaps>, BudgetError> file)
    {
        await new BudgetLoader(Book, new FixedFiles(file, ReadFor), Bus)
            .HandleAsync(new SessionOpened(session, Provider, "/worktrees/1", Connection), Cancellation);
        await enforcer.HandleAsync(Bus.Published.OfType<BudgetLoaded>().Last(), Cancellation);
    }

    public async Task RunningAsync(Result<Option<BudgetCaps>, BudgetError> file) => await RunningAsync(Job, Session, file);

    public async Task RunningAsync(JobId job, SessionId session, Result<Option<BudgetCaps>, BudgetError> file)
    {
        await OpenAsync(session, file);
        await TiedAsync(job, session);
    }

    public async Task TiedAsync() => await TiedAsync(Job, Session);

    public async Task TiedAsync(JobId job, SessionId session)
    {
        await ProgressAsync(job, JobStatus.Running);
        await enforcer.HandleAsync(new JobSessionStarted(job, session), Cancellation);
    }

    public async Task SubmittedAsync(JobId parent, JobId child) =>
        await enforcer.HandleAsync(new JobSubmitted(child) { Parent = parent }, Cancellation);

    public async Task ProgressAsync(JobStatus status) => await ProgressAsync(Job, status);

    public async Task ProgressAsync(JobId job, JobStatus status) => await enforcer.HandleAsync(new JobProgressed(job, status), Cancellation);

    public async Task SpendAsync(TokenUsage tokens, params Cost[] costs) => await SpendAsync(Job, tokens, costs);

    public async Task SpendAsync(JobId job, TokenUsage tokens, params Cost[] costs)
    {
        usage.Jobs[job] = new UsageSummary(tokens, costs, 0, default, []);
        await enforcer.HandleAsync(new UsageRecorded(SessionId.New(), job), Cancellation);
    }

    public async Task ReachAsync(UsageLimit limit)
    {
        usage.Limits = [limit];
        await enforcer.HandleAsync(new UsageRecorded(Session, Job), Cancellation);
    }

    public async Task MeasureAsync(long memoryBytes)
    {
        Resources.Memory = memoryBytes;
        await enforcer.HandleAsync(new ResourcesSampled(new ResourceSample(Clock.GetUtcNow(), [], [], 0)), Cancellation);
    }

    internal sealed class MeasuredResources : IResources
    {
        public long Memory { get; set; }

        public Option<ResourceSample> Latest => Option<ResourceSample>.None;

        public ResourceUsage Global() => OfJob(default);

        public ResourceUsage OfJob(JobId job) => new(1, Memory, TimeSpan.Zero, 0, [], 0);

        public ResourceUsage OfSession(SessionId session) => OfJob(default);

        public IReadOnlyList<ConnectionResources> ByConnection() => [];

        public IReadOnlyList<ProviderResources> ByProvider() => [];

        public IReadOnlyList<PortLease> Leases() => [];

        public IReadOnlyList<PortConflict> Conflicts() => [];

        public ValueTask<ResourceSettings> SettingsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    internal sealed class FixedMachine : Avala.Budgets.Admission.IMachineBudgetFile
    {
        public MachineBudget Budget { get; set; } = new(Option<int>.None, BudgetFileStatus.Absent, Option<BudgetError>.None);

        public ValueTask<MachineBudget> LoadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Budget);
    }

    internal sealed class HoldingJobs(Option<JobRejection> rejection) : IJobs
    {
        private readonly List<(JobId Job, HoldReason Reason)> holds = [];

        public IReadOnlyList<(JobId Job, HoldReason Reason)> Holds => holds;

        public ValueTask<Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<JobId, JobRejection>.Failure(JobRejection.InvalidRequest));

        public ValueTask<Result<JobHold, JobRejection>> HoldAsync(JobId job, HoldReason reason, CancellationToken cancellationToken)
        {
            holds.Add((job, reason));

            return ValueTask.FromResult(rejection.Match(
                Result<JobHold, JobRejection>.Failure,
                () => Result<JobHold, JobRejection>.Success(new JobHold(job, SessionId.New(), reason, SessionHalt.Interrupted))));
        }

        public ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, string message, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<JobContinuation, JobRejection>.Failure(JobRejection.NotHeld));

        public ValueTask<Result<JobContinuation, JobRejection>> ContinueOnAsync(
            JobId job,
            Avala.Agents.Contracts.Connections.ConnectionName connection,
            string message,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<JobContinuation, JobRejection>.Failure(JobRejection.NotHeld));

        public ValueTask<Result<JobId, JobRejection>> DiscardAsync(JobId job, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<JobId, JobRejection>.Failure(JobRejection.NotDiscardable));

        public ValueTask<Result<JobSteered, JobRejection>> SteerAsync(JobId job, string message, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<JobSteered, JobRejection>.Failure(JobRejection.NotSteerable));

        public ValueTask<Result<JobApproval, JobRejection>> ApproveAsync(JobId job, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<JobApproval, JobRejection>.Failure(JobRejection.NotAwaitingReview));

        public ValueTask<Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, string feedback, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<JobContinuation, JobRejection>.Failure(JobRejection.NotAwaitingReview));

        public ValueTask<Result<JobContinuation, JobRejection>> ResumeAsync(JobId job, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<JobContinuation, JobRejection>.Failure(JobRejection.NotDeferred));

        public ValueTask<Result<JobContinuation, JobRejection>> HandOffAsync(JobId job, JobHandoff handoff, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<JobContinuation, JobRejection>.Failure(JobRejection.NotHeld));
    }

    internal sealed class Usage : IUsage
    {
        public Dictionary<JobId, UsageSummary> Jobs { get; } = [];

        public IReadOnlyList<UsageLimit> Limits { get; set; } = [];

        public IReadOnlyList<ProviderUsage> ByProvider() =>
            [new ProviderUsage(Provider, new UsageSummary(default, [], 0, default, [new UsageLimit("5h", 1, Option<DateTimeOffset>.None)]))];

        public IReadOnlyList<AccountUsage> ByAccount() => [];

        public IReadOnlyList<ConnectionUsage> ByConnection() =>
        [
            new ConnectionUsage(new ConnectionName("other"), Provider, new UsageSummary(default, [], 0, default, [new UsageLimit("5h", 1, Option<DateTimeOffset>.None)])),
            new ConnectionUsage(Connection, Provider, new UsageSummary(default, [], 0, default, Limits)),
        ];

        public Option<UsageSummary> OfSession(SessionId session) => Option<UsageSummary>.None;

        public Option<UsageSummary> OfJob(JobId job) => Jobs.TryGetValue(job, out var spent) ? spent : Option<UsageSummary>.None;
    }

    private sealed class FixedFiles(Result<Option<BudgetCaps>, BudgetError> file, List<ConnectionName> readFor) : IBudgetFiles
    {
        public ValueTask<BudgetFile> ReadAsync(string workingDirectory, ConnectionName connection, CancellationToken cancellationToken)
        {
            readFor.Add(connection);

            return ValueTask.FromResult(new BudgetFile(CommittedFiles.Origin(), file));
        }

        public ValueTask<BudgetFile> ReadCurrentAsync(string repository, ConnectionName connection, CancellationToken cancellationToken) =>
            ReadAsync(repository, connection, cancellationToken);
    }
}
