using System.Collections.Concurrent;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Documentation.Catalog;
using Avala.Documentation.Contracts;
using Avala.Documentation.Records;
using Avala.Documentation.Writing;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Documentation.Tests.Writing;

internal sealed class Documented
{
    public const string Repository = "/repos/ledger";

    private Documented(DocsRules rules)
    {
        Rules = new FixedRules(rules);
        Book = new DocumentationBook(Store, Bus, TimeProvider.System);
        var catalog = new FeatureCatalog(Files, Files);
        Desk = new DocumentationDesk(
            new DocumentedJobs(new JobPlaces(Jobs, Jobs), Rules, catalog, Book),
            new WriterLauncher(Jobs, new FixedRoles(), catalog, Files),
            new VersionKeeper(Files, catalog, new NoEvidence(), new NoEvidence()),
            Book);
    }

    public static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public FakeJobs Jobs { get; } = new();

    public Folders Files { get; } = new();

    public MemoryStore Store { get; } = new();

    public RecordingBus Bus { get; } = new();

    public FixedRules Rules { get; }

    public DocumentationBook Book { get; }

    public DocumentationDesk Desk { get; }

    public static Documented With(bool enabled = true) => new(new DocsRules(enabled, DocsRules.DefaultFolder));

    public async Task<(JobId Job, SessionId Session)> RunningAsync(string instruction = "Handle Stripe refund webhooks", Option<JobId> parent = default)
    {
        var job = Jobs.Add(JobStatus.Running, instruction, parent);
        var session = SessionId.New();
        await Desk.HandleAsync(new JobSessionStarted(job, session), Cancellation);

        return (job, session);
    }

    public async Task PlanAsync(SessionId session, params string[] steps) =>
        await Desk.HandleAsync(new AgentActivity(new PlanUpdated(session, TurnId.New(), [.. steps.Select((step, index) => new PlanStep(step, index == 0 ? PlanStepStatus.InProgress : PlanStepStatus.Pending))])), Cancellation);

    public async Task PlanningAsync(SessionId session, params string[] steps) =>
        await Desk.HandleAsync(new AgentActivity(new PlanUpdated(session, TurnId.New(), [.. steps.Select(step => new PlanStep(step, PlanStepStatus.Pending))])), Cancellation);

    public async Task TurnEndsAsync(SessionId session) =>
        await Desk.HandleAsync(new AgentActivity(new TurnCompleted(session, TurnId.New(), TurnOutcome.Finished)), Cancellation);

    public async Task ProgressAsync(JobId job, JobStatus status)
    {
        Jobs.Move(job, status);
        await Desk.HandleAsync(new JobProgressed(job, status), Cancellation);
    }

    public JobId LastWriter => Jobs.Submitted[^1].Job;

    public Installed Docfx { get; } = new();

    public DocumentationEntry Entry()
    {
        var reads = new DocumentationReads(Rules, new FeatureCatalog(Files, Files), Files, new JobPlaces(Jobs, Jobs));

        return new DocumentationEntry(Rules, Book, reads, new SiteDesk(reads, Files, new Docfx(Docfx, new Avala.Documentation.RepositoryFiles.EmbeddedSiteTemplate()), new SiteFiller(Jobs, new FixedRoles())));
    }

    internal sealed class Installed : Sdk.Processes.IProcessRunner
    {
        public bool IsInstalled { get; set; } = true;

        public ValueTask<Result<Sdk.Processes.ProcessOutcome, Sdk.Processes.ProcessError>> RunAsync(Sdk.Processes.ProcessRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<Sdk.Processes.ProcessOutcome, Sdk.Processes.ProcessError>.Success(new Sdk.Processes.ProcessOutcome(IsInstalled ? 0 : 127, "2.78.3", string.Empty)));
    }

    public IReadOnlyList<T> Published<T>() => [.. Bus.Published.OfType<T>()];

    internal sealed class FakeJobs : IJobs, IJobCatalog, IWorkspaces
    {
        private readonly ConcurrentDictionary<JobId, JobHistory> histories = new();
        private readonly ConcurrentQueue<(JobId Job, JobRequest Request)> submitted = new();
        private readonly ConcurrentQueue<JobId> discarded = new();

        public IReadOnlyList<(JobId Job, JobRequest Request)> Submitted => [.. submitted];

        public IReadOnlyList<JobId> Discarded => [.. discarded];

        public Option<JobRejection> Rejection { get; set; }

        public static string Worktree(JobId job) => $"/worktrees/{job.Value}";

        public JobId Add(JobStatus status, string instruction, Option<JobId> parent)
        {
            var job = JobId.New();
            histories[job] = new JobHistory(
                new JobSummary(job, Repository, instruction, DateTimeOffset.UnixEpoch, status, new ConnectionName("work"), Option<Autonomy>.None, WorkspaceOf(job, status)) { Parent = parent },
                [],
                []);

            return job;
        }

        public void Move(JobId job, JobStatus status) =>
            histories[job] = histories[job] with { Summary = histories[job].Summary with { Status = status, Workspace = WorkspaceOf(job, status) } };

        private static Option<WorkspaceId> WorkspaceOf(JobId job, JobStatus status) =>
            status == JobStatus.Preparing ? Option<WorkspaceId>.None : new WorkspaceId(job.Value);

        public void Forget(JobId job) => histories.TryRemove(job, out _);

        public void Attempt(JobId job, AttemptOrigin origin, string guidance)
        {
            var attempts = histories[job].Attempts;
            histories[job] = histories[job] with
            {
                Attempts = [.. attempts, new AttemptRecord(attempts.Count + 1, origin, AttemptOutcome.Running, guidance.Length == 0 ? Option<string>.None : guidance, Option<SessionId>.None)],
            };
        }

        public ValueTask<Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken)
        {
            if (Rejection.IsSome)
            {
                return ValueTask.FromResult(Result<JobId, JobRejection>.Failure(Rejection.Match(rejection => rejection, () => default)));
            }

            var job = Add(JobStatus.Preparing, request.Instruction, request.Parent);
            submitted.Enqueue((job, request));

            return ValueTask.FromResult(Result<JobId, JobRejection>.Success(job));
        }

        public ValueTask<Result<JobId, JobRejection>> DiscardAsync(JobId job, CancellationToken cancellationToken)
        {
            discarded.Enqueue(job);
            Move(job, JobStatus.Discarded);

            return ValueTask.FromResult(Result<JobId, JobRejection>.Success(job));
        }

        public ValueTask<Option<JobHistory>> HistoryAsync(JobId job, CancellationToken cancellationToken) =>
            ValueTask.FromResult(histories.TryGetValue(job, out var history) ? history : Option<JobHistory>.None);

        public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<WorkspaceInfo, WorkspaceFailure>.Success(new WorkspaceInfo(workspace, Worktree(new JobId(workspace.Value)), "avala/job", "base")));

        public ValueTask<IReadOnlyList<JobSummary>> ListAsync(CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<JobSummary>>([.. histories.Values.Select(history => history.Summary)]);

        public ValueTask<IReadOnlyList<JobSummary>> ChildrenAsync(JobId parent, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Option<JobTree>> TreeAsync(JobId root, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<JobHold, JobRejection>> HoldAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<JobHold, JobRejection>> HoldRunningAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, string message, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<JobContinuation, JobRejection>> ContinueOnAsync(JobId job, ConnectionName connection, string message, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<JobSteered, JobRejection>> SteerAsync(JobId job, string message, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<JobApproval, JobRejection>> ApproveAsync(JobId job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, string feedback, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<JobContinuation, JobRejection>> ResumeAsync(JobId job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<JobContinuation, JobRejection>> HandOffAsync(JobId job, JobHandoff handoff, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<JobModelChosen, JobRejection>> ChooseModelAsync(JobId job, ModelChoice model, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> PrepareAsync(WorkspaceRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<CheckpointInfo, WorkspaceFailure>> CheckpointAsync(WorkspaceId workspace, string label, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<WorkspaceId, WorkspaceFailure>> RemoveAsync(WorkspaceId workspace, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAtAsync(string folder, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<WorktreeReconciliation> ReconcileAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<WorktreeReconciliation> CleanAsync(WorktreeReconciliation found, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    internal sealed class Folders : IWorkingFiles, IRepositoryIndex
    {
        private readonly ConcurrentDictionary<(string Folder, string Path), string> files = new();

        public string? this[string folder, string path] => files.TryGetValue((folder, path), out var content) ? content : null;

        public void Draft(string folder, string path, string content) => files[(folder, path)] = content;

        public ValueTask<Result<Option<string>, WorkspaceFailure>> ReadAsync(string repository, string path, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<Option<string>, WorkspaceFailure>.Success(files.TryGetValue((repository, path), out var content) ? content : Option<string>.None));

        public ValueTask<Result<string, WorkspaceFailure>> WriteAsync(string repository, string path, string content, CancellationToken cancellationToken)
        {
            Draft(repository, path, content);

            return ValueTask.FromResult(Result<string, WorkspaceFailure>.Success(path));
        }

        public ValueTask<IReadOnlyList<string>> FilesAsync(string folder, int limit, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<string>>([.. files.Keys.Where(key => key.Folder == folder).Select(key => key.Path).Order(StringComparer.Ordinal).Take(limit)]);

        public ValueTask<IReadOnlyList<CommitSummary>> CommitsAsync(string folder, int count, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<Option<string>, WorkspaceFailure>> LocateAsync(string repository, string path, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<Option<byte[]>, WorkspaceFailure>> ReadBytesAsync(string repository, string path, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    internal sealed class FixedRules(DocsRules rules) : IDocsRules
    {
        public Task<Result<DocsRules, DocumentationError>> OfWorktreeAsync(string worktree, CancellationToken cancellationToken) => Task.FromResult(Result<DocsRules, DocumentationError>.Success(rules));

        public Task<Result<DocsRules, DocumentationError>> OfRepositoryAsync(string repository, CancellationToken cancellationToken) => Task.FromResult(Result<DocsRules, DocumentationError>.Success(rules));

        public Task<Result<DocsRules, DocumentationError>> SwitchAsync(string repository, bool enabled, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    internal sealed class FixedRoles : IRoleSettings
    {
        public static ModelChoice Research { get; } = new(Option<string>.Some("haiku"), Option<string>.Some("low"));

        public ValueTask<ModelChoice> OfRoleAsync(string worktree, ChildRole role, CancellationToken cancellationToken) =>
            ValueTask.FromResult(role == ChildRole.Research ? Research : ModelChoice.Default);
    }

    internal sealed class NoEvidence : IVerifications, IWorkspaceChanges
    {
        public IReadOnlyList<VerificationReport> OfJob(JobId job) => [];

        public ValueTask<Result<WorkspaceDiff, WorkspaceFailure>> DiffAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<WorkspaceDiff, WorkspaceFailure>.Success(new WorkspaceDiff(workspace, "base", "head", [new FileChange("internal/webhooks/handler.go", ChangeKind.Added, 84, 0)])));

        public ValueTask<Result<FileDiff, WorkspaceFailure>> FileDiffAsync(WorkspaceId workspace, string path, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<IReadOnlyList<string>, WorkspaceFailure>> ConflictsAsync(WorkspaceId workspace, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<MergedWork, WorkspaceFailure>> MergeAsync(WorkspaceId workspace, string message, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    internal sealed class MemoryStore : IDocumentationStore
    {
        private readonly ConcurrentDictionary<JobId, DocumentedJob> jobs = new();

        public IReadOnlyList<DocumentedJob> Kept => [.. jobs.Values];

        public Task<IReadOnlyList<DocumentedJob>> ReadAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DocumentedJob>>([.. jobs.Values]);

        public Task ChooseAsync(JobId job, FeatureChoice choice, CancellationToken cancellationToken) => ChangeAsync(job, found => found with { Choice = choice });

        public Task WritingAsync(JobId job, PendingWriter writer, CancellationToken cancellationToken) => ChangeAsync(job, found => found with { Writer = writer });

        public Task WrittenAsync(JobId job, FeatureVersion version, string plan, CancellationToken cancellationToken) =>
            ChangeAsync(job, found => found with { Versions = found.Versions.Add(version), Writer = Option<PendingWriter>.None, LastPlan = plan, Requests = [.. found.Linked(version)] });

        public Task RequestedAsync(JobId job, ChangeRequest request, CancellationToken cancellationToken) =>
            ChangeAsync(job, found => found with { Requests = found.Requests.Add(request) });

        public Task ForgetAsync(JobId job, JobId writer, string plan, CancellationToken cancellationToken) =>
            ChangeAsync(job, found => found with { Writer = Option<PendingWriter>.None, LastPlan = plan });

        private Task ChangeAsync(JobId job, Func<DocumentedJob, DocumentedJob> change)
        {
            jobs.AddOrUpdate(job, added => change(new DocumentedJob(added, FeatureChoice.Undocumented)), (_, found) => change(found));

            return Task.CompletedTask;
        }
    }
}
