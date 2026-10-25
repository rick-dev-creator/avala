using System.Collections.Concurrent;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Verification.Discovering;
using Avala.Verification.Evidence;
using Avala.Verification.Tests.Verifying;
using Avala.Verification.Verifying;
using Avala.Workspaces.Contracts;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Verification.Tests.Discovering;

internal sealed class Scouted : IAsyncDisposable
{
    public const string Repository = "/repos/shop";

    public const string ChecksPath = ".avala/checks.json";

    private Scouted(FakeJobs jobs, WorkingFolders folders, CommittedFiles committed, MemoryDiscoveries store, Toggle toggle, RecordingBus bus, FakeTimeProvider clock, ScriptedProcesses processes, FakePolicies policies, FakeAgents agents)
    {
        Policies = policies;
        Agents = agents;
        Jobs = jobs;
        Folders = folders;
        Committed = committed;
        Store = store;
        Settings = toggle;
        Bus = bus;
        Clock = clock;
        Processes = processes;
        Book = new DiscoveryBook(Store, Bus, Clock);
        var runner = new CheckRunner(Processes, Clock);
        var worktrees = new JobWorktrees(Jobs, Jobs);
        var discovered = new DiscoveredJobs(worktrees, Committed, Folders, new Scratch());
        Desk = new DiscoveryDesk(Book, discovered, new ProposalReview(worktrees, runner, new DiscoveryTerms(Settings, Policies)), new JobActions(Jobs, new FixedRoles(), Bus));
        Scout = new ChecksScout(Book, Desk, discovered);
        Proposals = new ProposalDesk(Book, Agents, Answers);
        Evidence = new EvidenceBook(new InMemoryEvidence());
        Gate = new ChecksGate(new AttemptChecks(Committed, new DiscoveredChecks(Desk, Book, new ShippedChecks(Folders, Jobs, Jobs), worktrees)), runner, new EvidenceLedger(Evidence, Bus), Clock);
    }

    public static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public FakeJobs Jobs { get; }

    public FakePolicies Policies { get; }

    public IReadOnlyList<JobId> Rechecked => [.. Bus.Published.OfType<DeferredGateReady>().Select(ready => ready.Job)];

    public FakeAgents Agents { get; }

    public FakeAnswers Answers { get; } = new();

    public ProposalDesk Proposals { get; }

    public WorkingFolders Folders { get; }

    public CommittedFiles Committed { get; }

    public MemoryDiscoveries Store { get; }

    public Toggle Settings { get; }

    public RecordingBus Bus { get; }

    public FakeTimeProvider Clock { get; }

    public ScriptedProcesses Processes { get; }

    public DiscoveryBook Book { get; }

    public DiscoveryDesk Desk { get; }

    public ChecksScout Scout { get; }

    public EvidenceBook Evidence { get; }

    public ChecksGate Gate { get; }

    public JobId LastScout => Jobs.Submitted[^1].Job;

    public static Scouted Fresh()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.Zero));

        return new(new FakeJobs(), new WorkingFolders(), new CommittedFiles(), new MemoryDiscoveries(), new Toggle(), new RecordingBus(), clock, new ScriptedProcesses(clock), new FakePolicies(), new FakeAgents());
    }

    public async Task<Scouted> RestartedAsync()
    {
        await DisposeAsync();
        var restarted = new Scouted(Jobs, Folders, Committed, Store, Settings, Bus, Clock, Processes, Policies, Agents);
        await restarted.Book.RunAsync(Cancellation);
        await restarted.Scout.HandleAsync(new StartupCompleted(), Cancellation);

        return restarted;
    }

    public JobId Job(Option<string> checks = default, Option<JobId> parent = default, bool companion = false, string repository = Repository)
    {
        var job = Jobs.Add(JobStatus.Running, "Add a discount code to the checkout", parent, companion, repository);
        var worktree = FakeJobs.Worktree(job);
        _ = checks.Match(content => Committed.With(worktree, ChecksPath, content), () => Committed.Workspace(worktree));

        return job;
    }

    public async Task<JobId> RunningAsync(Option<string> checks = default, string repository = Repository)
    {
        var job = Job(checks, repository: repository);
        await Scout.HandleAsync(new JobProgressed(job, JobStatus.Running), Cancellation);

        return job;
    }

    public async Task MoveAsync(JobId job, JobStatus status)
    {
        Jobs.Move(job, status);
        await Scout.HandleAsync(new JobProgressed(job, status), Cancellation);
    }

    public async Task<ToolResult> ProposesAsync(JobId scout, string proposal, bool ran = true)
    {
        var session = SessionId.New();
        await Proposals.HandleAsync(new JobSessionStarted(scout, session), Cancellation);

        foreach (var check in ran ? Avala.Verification.Checks.CheckDeclaration.Parse(proposal).Match(checks => checks, _ => []) : [])
        {
            await RanAsync(scout, check.CommandLine);
        }

        await Proposals.HandleAsync(new AgentActivity(new ToolCalled(session, TurnId.New(), new ItemId("proposal"), ChecksProposal.Tool, proposal)), Cancellation);

        return Agents.Returned[^1];
    }

    public async Task RanAsync(JobId scout, string command) =>
        await Proposals.HandleAsync(
            new Permissions.Contracts.PermissionDecided(new Permissions.Contracts.PolicyDecision(
                SessionId.New(),
                TurnId.New(),
                new ItemId(command),
                scout,
                ItemKind.Command,
                command,
                Permissions.Contracts.PolicyAnswer.Allow,
                Option<Permissions.Contracts.PolicyRule>.None,
                Permissions.Contracts.DecisionDelivery.Answered,
                DateTimeOffset.UnixEpoch)),
            Cancellation);

    public ValueTask<GateVerdict> EvaluateAsync(JobId job, int attempt = 1) =>
        Gate.EvaluateAsync(new CompletedAttempt(job, attempt, FakeJobs.Worktree(job), "Add a discount code to the checkout"), Cancellation);

    public IReadOnlyList<VerificationReport> Reports => [.. Bus.Published.OfType<AttemptVerified>().Select(verified => verified.Report)];

    public ValueTask DisposeAsync() => Desk.DisposeAsync();

    internal sealed class FakePolicies : Permissions.Contracts.IRepositoryPolicies
    {
        public Autonomy Autonomy { get; set; } = Autonomy.Autonomous;

        public ValueTask<Permissions.Contracts.RepositoryPolicy> OfRepositoryAsync(string repository, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new Permissions.Contracts.RepositoryPolicy(Permissions.Contracts.PolicyFileStatus.Applied, Option<Permissions.Contracts.PolicyError>.None, [], Option<FileOrigin>.None) { Autonomy = Autonomy });
    }

    internal sealed class FakeAnswers : Permissions.Contracts.IPermissionAnswers
    {
        private readonly ConcurrentQueue<(SessionId Session, Permissions.Contracts.PermissionReply Reply)> given = new();

        public IReadOnlyList<(SessionId Session, Permissions.Contracts.PermissionReply Reply)> Given => [.. given];

        public ValueTask<Result<Permissions.Contracts.HumanAnswer, Permissions.Contracts.PolicyError>> AnswerAsync(SessionId session, Permissions.Contracts.PermissionReply reply, CancellationToken cancellationToken)
        {
            given.Enqueue((session, reply));

            return ValueTask.FromResult(Result<Permissions.Contracts.HumanAnswer, Permissions.Contracts.PolicyError>.Failure(default));
        }
    }

    internal sealed class FakeAgents : IAgents
    {
        private readonly ConcurrentQueue<ToolResult> returned = new();
        private readonly ConcurrentQueue<(SessionId Session, FormAnswer Answer)> forms = new();

        public IReadOnlyList<(SessionId Session, FormAnswer Answer)> Forms => [.. forms];

        public IReadOnlyList<ToolResult> Returned => [.. returned];

        public ValueTask<Result<ItemId, AgentError>> ReturnAsync(SessionId session, ToolResult result, CancellationToken cancellationToken)
        {
            returned.Enqueue(result);

            return ValueTask.FromResult(Result<ItemId, AgentError>.Success(result.Item));
        }

        public ValueTask<Result<OpenedSession, AgentError>> OpenAsync(AgentRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public bool IsOpen(SessionId session) => false;

        public ValueTask<Result<AgentTurn, AgentError>> SendAsync(SessionId session, string message, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<AgentTurn, AgentError>> SteerAsync(SessionId session, string message, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<AgentTurn, AgentError>> SayAsync(SessionId session, UserTurn turn, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<ItemId, AgentError>> RespondAsync(SessionId session, PermissionDecision decision, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<ItemId, AgentError>> AnswerAsync(SessionId session, FormAnswer answer, CancellationToken cancellationToken)
        {
            forms.Enqueue((session, answer));

            return ValueTask.FromResult(Result<ItemId, AgentError>.Success(answer.Item));
        }

        public ValueTask<Result<TurnId, AgentError>> InterruptAsync(SessionId session, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<RemoteState, AgentError>> ControlRemotelyAsync(SessionId session, bool on, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<SessionId, AgentError>> StopAsync(SessionId session, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    internal sealed class Toggle : IDiscoverySettings
    {
        public bool Discovers { get; set; } = true;

        public ValueTask<bool> DiscoversAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Discovers);
    }

    internal sealed class Scratch : IScratchRepositories
    {
        public const string Folder = "/data/scratch";

        string IScratchRepositories.Folder => Folder;

        public bool Holds(string repository) => repository.StartsWith(Folder, StringComparison.Ordinal);

        public ValueTask<Result<string, WorkspaceFailure>> CreateAsync(string instruction, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    internal sealed class FixedRoles : IRoleSettings
    {
        public static ModelChoice Research { get; } = new(Option<string>.Some("haiku"), Option<string>.Some("low"));

        public ValueTask<ModelChoice> OfRoleAsync(string worktree, ChildRole role, CancellationToken cancellationToken) =>
            ValueTask.FromResult(role == ChildRole.Research ? Research : ModelChoice.Default);
    }

    internal sealed class MemoryDiscoveries : IDiscoveryStore
    {
        private readonly ConcurrentDictionary<JobId, Discovery> kept = new();

        public IReadOnlyList<Discovery> Kept => [.. kept.Values];

        public Task<IReadOnlyList<Discovery>> DiscoveriesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Discovery>>([.. kept.Values]);

        public Task KeepAsync(Discovery discovery, CancellationToken cancellationToken)
        {
            kept[discovery.Job] = discovery;

            return Task.CompletedTask;
        }
    }

    internal sealed class WorkingFolders : IWorkingFiles
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

        public ValueTask<Result<Option<string>, WorkspaceFailure>> LocateAsync(string repository, string path, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<Option<byte[]>, WorkspaceFailure>> ReadBytesAsync(string repository, string path, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    internal sealed class FakeJobs : IJobs, IJobCatalog, IWorkspaces, IWorkspaceChanges
    {
        private readonly ConcurrentDictionary<WorkspaceId, string[]> committedPaths = new();

        public void Commit(JobId job, string path) => committedPaths[new WorkspaceId(job.Value)] = [.. committedPaths.GetValueOrDefault(new WorkspaceId(job.Value), []), path];

        public ValueTask<Result<WorkspaceDiff, WorkspaceFailure>> DiffAsync(WorkspaceId workspace, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<WorkspaceDiff, WorkspaceFailure>.Success(new WorkspaceDiff(workspace, "base", "head", [.. committedPaths.GetValueOrDefault(workspace, []).Select(path => new FileChange(path, ChangeKind.Added, 1, 0))])));

        public ValueTask<Result<FileDiff, WorkspaceFailure>> FileDiffAsync(WorkspaceId workspace, string path, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<IReadOnlyList<string>, WorkspaceFailure>> ConflictsAsync(WorkspaceId workspace, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<MergedWork, WorkspaceFailure>> MergeAsync(WorkspaceId workspace, string message, CancellationToken cancellationToken) => throw new NotSupportedException();

        private readonly ConcurrentDictionary<JobId, JobHistory> histories = new();
        private readonly ConcurrentQueue<(JobId Job, JobRequest Request)> submitted = new();
        private readonly ConcurrentQueue<JobId> discarded = new();
        private readonly ConcurrentQueue<(WorkspaceId Workspace, string Label)> checkpoints = new();

        public IReadOnlyList<(JobId Job, JobRequest Request)> Submitted => [.. submitted];

        public IReadOnlyList<(WorkspaceId Workspace, string Label)> Checkpoints => [.. checkpoints];

        public IReadOnlyList<JobId> Discarded => [.. discarded];

        public Option<JobRejection> Rejection { get; set; }

        public Option<JobStatus> SubmittedAs { get; set; }

        public static string Worktree(JobId job) => $"/worktrees/{job.Value}";

        public JobId Add(JobStatus status, string instruction, Option<JobId> parent, bool companion, string repository)
        {
            var job = JobId.New();
            histories[job] = new JobHistory(
                new JobSummary(job, repository, instruction, DateTimeOffset.UnixEpoch, status, new ConnectionName("work"), Option<Autonomy>.None, new WorkspaceId(job.Value))
                {
                    Parent = parent,
                    Companion = companion,
                },
                [],
                []);

            return job;
        }

        public void Move(JobId job, JobStatus status) =>
            histories[job] = histories[job] with { Summary = histories[job].Summary with { Status = status } };

        public ValueTask<Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken)
        {
            if (Rejection.IsSome)
            {
                return ValueTask.FromResult(Result<JobId, JobRejection>.Failure(Rejection.Match(rejection => rejection, () => default)));
            }

            var job = Add(SubmittedAs.Match(status => status, () => JobStatus.Preparing), request.Instruction, request.Parent, request.Companion, request.RepositoryPath);
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

        public ValueTask<IReadOnlyList<JobSummary>> ChildrenAsync(JobId parent, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<JobSummary>>([.. histories.Values.Select(history => history.Summary).Where(summary => summary.Parent == Option<JobId>.Some(parent))]);

        public ValueTask<Option<JobTree>> TreeAsync(JobId root, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<JobHold, JobRejection>> HoldAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<JobHold, JobRejection>> HoldRunningAsync(JobId job, HoldReason reason, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, JobMessage message, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, string message, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<JobContinuation, JobRejection>> ContinueOnAsync(JobId job, ConnectionName connection, JobMessage message, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<JobSteered, JobRejection>> SteerAsync(JobId job, JobMessage message, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<JobApproval, JobRejection>> ApproveAsync(JobId job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, string feedback, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<JobContinuation, JobRejection>> ResumeAsync(JobId job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<JobContinuation, JobRejection>> HandOffAsync(JobId job, JobHandoff handoff, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<JobModelChosen, JobRejection>> ChooseModelAsync(JobId job, ModelChoice model, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> PrepareAsync(WorkspaceRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<CheckpointInfo, WorkspaceFailure>> CheckpointAsync(WorkspaceId workspace, string label, CancellationToken cancellationToken)
        {
            checkpoints.Enqueue((workspace, label));

            return ValueTask.FromResult(Result<CheckpointInfo, WorkspaceFailure>.Success(new CheckpointInfo(workspace, checkpoints.Count, "c0ffee", label)));
        }

        public ValueTask<Result<WorkspaceId, WorkspaceFailure>> RemoveAsync(WorkspaceId workspace, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<WorkspaceInfo, WorkspaceFailure>> FindAtAsync(string folder, CancellationToken cancellationToken) =>
            FindAsync(new WorkspaceId(Guid.Parse(folder[(folder.LastIndexOf('/') + 1)..])), cancellationToken);

        public ValueTask<WorktreeReconciliation> ReconcileAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<WorktreeReconciliation> CleanAsync(WorktreeReconciliation found, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
