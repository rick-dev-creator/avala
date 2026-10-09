# Core design

Status: draft. Each decision is marked **Accepted** or **Proposed**.

## Goals

- Run any coding agent through one agnostic core. The core never knows which agent or harness it drives.
- Decompose orchestration into small modules with explicit contracts, instead of one orchestrator that owns everything.
- Keep business rules in the domain, deterministic and unit-testable without infrastructure.
- Favor composition over inheritance everywhere.

## Lessons from existing harnesses

T3 Code is the reference point. Its history (5,023 commits, about 907k non-test lines) shows what to avoid:

| Problem in T3 Code | Evidence | Answer in Avala |
| --- | --- | --- |
| One orchestrator mixes execution with UI state such as pin, snooze and unread | `Orchestrator.ts`, 11,031 lines, about 60 commands | Execution, workspaces, UI state and integrations live in separate modules |
| Every provider adapter re-derives lifecycle semantics | Adapters of 4,000 to 8,000 lines; fixes counted per provider | Thin adapters translate protocols; one `TurnLifecycle` in the core enforces the rules |
| The model is built around one provider, others are adapted through optional methods | 102 fixes about runs that never settle | The contract is the common denominator plus declared capabilities |
| Several durable layers: event log, projections, outbox, receipts, cursors | 113 fixes about resume and recovery | Plain state persistence, idempotent handlers, recovery from state |
| A 10,400-line view component | `ChatView.tsx` | One view model and one template per event type |

## Terminology

| Term | Meaning |
| --- | --- |
| Job | A unit of work delegated to an agent. Not `Task`, which collides with `System.Threading.Tasks.Task` in every async signature. **Accepted** |
| Attempt | One run of an agent on a job, from start until the agent ends its turn |
| Session | A live connection to an agent through its provider |
| Turn | One exchange inside a session, from the agent starting to work until it stops |
| Workspace | The isolated working copy of a job: folder, branch, checkpoints |
| Gate | A component that judges a completed attempt before the job moves on |

## Modules

Every module is internal. Only its `Contracts` project is public, and only when another module or a plugin needs it. **Accepted**

| Module | Responsibility | Public contracts |
| --- | --- | --- |
| Jobs | Job lifecycle, attempts, attempt budget, the job flow coordinator, holding a job for a typed reason, including one whose session was lost, continuing a held job, reviewing a job: approving it through its repository's approval strategy, or into its parent's worktree for a child job, sending it back or discarding it, stopping the session of a job that ended, admitting launches, the job's resume token, the autonomy a job asks for and the connection it runs on, the tree of parent and child jobs, and the catalog of jobs for the views | `JobId`, `Autonomy`, integration events, `IJobs`, `IJobCatalog`, `JobTree`, `ICompletionGate`, `IJobAdmission`, `IApprovalStrategy` |
| Agents | Sessions, turn integrity, provider registry, the connections sessions open on and their credential sources, the harness tools and resume tokens handed to providers by capability, the results of the harness tools the harness executes, the process tree each session's processes run in, the forms agents ask humans to fill, and the decorators every provider is started through | `IAgents`, `IAgentProvider`, `IAgentSession`, `IAgentProviderDecorator`, `IConnections`, `ICredentialSource`, `ConnectionName`, `ConnectionEnvironment`, `AgentEvent`, `AgentCapabilities`, `HarnessTool`, `ToolResult`, `ResumeToken`, `AgentAccount`, `AgentForm`, `FormAnswer`, integration events |
| Workspaces | Working copies, branches, checkpoints, the files of the commit a job's rules come from, the diff of a workspace against the commit it started from, merging a workspace's work into its base branch, and the reconciliation of worktrees on disk with the store | `IWorkspaces`, `IBaseFiles`, `IWorkspaceChanges`, `WorktreeReconciliation`, integration events |
| Resources | Samples the processes, ports and disk every job uses, reaps the processes a session leaves behind, leases ports per worktree and reclaims worktrees by retention | `IResources`, `IOrphans`, `IWorktreeHousekeeping`, integration events |
| Canvas | Offers the canvas tool in the media types renderer plugins declare, accumulates the canvases agents stream and publishes throttled snapshots | `CanvasId`, `CanvasFormat`, `CanvasUpdated`, `ICanvases` |
| Workbench | The user interface's module: the job board, the conversation projection of every job since the application started, the composer's commands, the answers a person gives permissions and forms, and every view model of the main window, see [Workbench](#workbench) | None: other modules never depend on it |
| Observability | Tokens, cost, limits and turns by provider, account, connection, session and job, stored as facts so they survive a restart and can be read over time windows, and their metrics | `IUsage`, `IUsageHistory` and their summaries |
| Verification | Runs the checks a repository declares as a completion gate and keeps the evidence of every attempt | `AttemptVerified`, `VerificationReport`, `IVerifications` |
| Permissions | Answers permission requests and forms through an explicit policy at the job's level of autonomy, takes a human's answers with their session rules, and records why each decision was made | `PolicyLoaded`, `PermissionDecided`, `AutonomyApplied`, `FormDecided`, `PermissionAnswered`, `IPermissionAudit`, `IPermissionAnswers` |
| Supervision | Holds a job whose agent stays silent, and records every intervention | `SilenceNoticed`, `SupervisorIntervened`, `ISupervision` |
| Budgets | Holds a job that reaches a cap on cost, tokens or memory, or a provider limit threshold, carves a child job's budget out of its parent's, records every intervention and carve, and admits launches up to the machine's limit of running jobs | `BudgetLoaded`, `BudgetIntervened`, `BudgetCarved`, `JobQueued`, `JobAdmitted`, `IBudgets` |
| Recording | Records every provider session, when the data folder asks for it, as a file the simulator replays | None: it implements `IAgentProviderDecorator`, and its files are the [recording format](#session-recording-and-replay) |
| Autopilot | Runs a repository's tasks one job after another, unattended: approves a job automatically only on clean evidence, takes its tasks from job sources, stops on its circuit breakers, pauses across a usage limit window, and keeps the digest of what it did | `IAutopilot`, `IJobSource`, `LoopId`, the loop's events and digest |
| Delegation | Offers orchestrating agents the `delegate` tool, decides each call by the repository's delegation rules, routes the child job to a connection, inherits its autonomy, and reports each child's verified work back to its parent as the call's result | `IDelegations`, `DelegationRecord`, `ChildReport`, `DelegationError`, `ChildOutcome`, the delegation events |

Agent providers such as Claude Code or Codex are plugins of their own. They depend only on `Agents.Contracts`. **Accepted**

## Domain model

### No base classes

Classic DDD relies on `Entity`, `AggregateRoot` and `ValueObject` base classes. Avala uses none of them. **Accepted**

- Identifiers and value objects are `readonly record struct`: value equality, immutability, no inheritance.
- Aggregates are `sealed` classes that compose what they need.
- Aggregates do not collect domain events. Every operation returns its event inside a `Result`.

```csharp
public readonly record struct JobId(Guid Value)
{
    public static JobId New() => new(Guid.CreateVersion7());
}

internal sealed class Job
{
    public JobId Id { get; }
    public JobState State { get; private set; }
    public RepositoryPath Repository { get; }
    public Option<WorkspaceId> Workspace { get; private set; }
    public Option<SessionId> Session { get; private set; }
    public Option<ResumeToken> Resume { get; private set; }
    public Option<ConnectionName> Connection { get; private set; }
    public Option<JobId> Parent { get; private init; }
    public DateTimeOffset Submitted { get; private init; }

    public static Result<Job, JobError> Create(JobId id, Instruction instruction, AttemptBudget budget, RepositoryPath repository, DateTimeOffset submitted, Option<Autonomy> autonomy, Option<ConnectionName> connection, Option<JobId> parent);
    public Result<JobSubmitted, JobError> Submit();
    public Result<AttemptStarted, JobError> Start(WorkspaceId workspace, SessionId session, ConnectionName connection);
    public Result<AttemptStarted, JobError> Recover(SessionId session, bool resumed);
    public Result<ResumeRecorded, JobError> RecordResume(SessionId session, ResumeToken token);
    public Result<AttemptCompleted, JobError> CompleteTurn();
    public Result<AttemptPassed, JobError> Pass();
    public Result<AttemptRetried, JobError> Retry(Feedback feedback);
    public Result<HelpRequested, JobError> RequestHelp();
    public Result<AttemptStarted, JobError> Hint(Feedback guidance);
    public Result<AttemptStarted, JobError> Hint(Feedback guidance, SessionId session, bool resumed);
    public Result<AttemptStarted, JobError> SendBack(Feedback feedback);
    public Result<AttemptStarted, JobError> SendBack(Feedback feedback, SessionId session, bool resumed);
    public Result<JobApproved, JobError> Approve();
    public Result<JobDiscarded, JobError> Discard();
    public Result<JobFailed, JobError> Fail(FailureReason reason);
    public Result<JobHeld, JobError> Hold(HoldReason reason);
}
```

### State machines

State machines use the [Stateless](https://github.com/dotnet-state-machine/stateless) library. **Accepted**

- The aggregate contains its machine. Nothing inherits from it.
- The machine uses external state storage, so the aggregate's `State` property stays the source of truth and is persisted normally.
- Hierarchical states (`SubstateOf`) and guards (`PermitIf`) model the rules.
- Several small machines are composed instead of one large one. This is the statechart equivalent of orthogonal regions.
- Machines decide transitions only. Their actions perform no I/O. Side effects belong to the application layer.
- The aggregate never calls `Fire` blindly. It asks `CanFire` first, guards included, and returns a typed error when the trigger is not allowed. `Fire` therefore never throws.
- Diagrams in this folder are generated from the machines with Stateless's `MermaidGraph`, and a test fails when a diagram is out of date. **Accepted**

| Machine | Module | States |
| --- | --- | --- |
| `JobLifecycle` | Jobs | Draft, Preparing, Running, Checking, AwaitingReview, NeedsHelp, Approved, Discarded, Failed, inside the superstates Open and Active |
| `TurnLifecycle` | Agents | Working, AwaitingPermission and AwaitingAnswer inside the superstate Live, then Finished, Interrupted or Failed |
| `WorkspaceLifecycle` | Workspaces | Creating, Ready, Disposed |
| `CanvasLifecycle` | Canvas | Streaming, then Completed, Failed, Cancelled, Abandoned or Expired inside the superstate Closed |

The generated [job lifecycle diagram](../diagrams/job-lifecycle.md) is the reference. A test fails when it no longer matches the code.

The job references its workspace and its agent session by identifier only. Both are absent until the job starts. `Recover` brings a job that was `Running` or `Checking` when the application stopped back to `Running`: it interrupts the attempt that was underway, records the new session and starts a `Recovery` attempt with a fresh round of retries.

The job also keeps the latest [resume token](#resuming-a-conversation) its current session issued: `RecordResume` accepts a token only from the job's current session and returns `ForeignSession` otherwise. When the job moves to a new session, through `Recover` or the `Hint` that names a session, it keeps the token only if that session resumed the conversation; a session that started over forgets it, since its conversation is a new one that will issue its own token.

Attempts have no state machine of their own. The job lifecycle already decides when an attempt starts, completes, passes or is rejected, so a second machine would be a second source of truth for the same facts. An attempt is an entity inside the `Job` aggregate, and only the job changes it. Each attempt records the session it ran in, so the job's history of sessions is the order of its attempts' sessions, and the job records the time it was submitted, from `TimeProvider` in `SubmitJob`. Their origin and outcome are the `AttemptOrigin` and `AttemptOutcome` enums of `Jobs.Contracts`, which the catalog reports as they are.

### Attempt budget

The budget limits automatic retries, not human involvement. It counts the attempts of the current round, and a round starts with the first attempt, a hint, a job sent back from review or a recovery. When the round is spent, `Retry` returns `AttemptBudgetExhausted` and the job can only ask for help.

### Result pattern

**Accepted**

- `Result` never throws. There is no `Value` property that throws on failure. Values are read only through `Match` or `TryGetValue`.
- Failures are deterministic, predictable error codes: one `enum` per module. No magic strings.
- `Result` is typed by its module's error enum, so the compiler guarantees that Jobs returns only Jobs errors, and a `switch` over the codes reports missing cases.
- The domain returns codes only. The presentation layer maps codes to text, which keeps the domain free of copy and ready for localization.
- At module boundaries, errors are translated explicitly. An error of one module never leaks into another.
- Exceptions are reserved for bugs and broken infrastructure.
- `Result` lives in the SDK and is written in-house: it is small, and a third-party library would put a dependency at the heart of every module.

```csharp
internal enum JobError
{
    InvalidTransition,
    AttemptBudgetExhausted,
    NotAwaitingReview,
}

var outcome = job.Approve().Match(
    approved => …,
    error => error switch
    {
        JobError.NotAwaitingReview => …,
        JobError.InvalidTransition => …,
        JobError.AttemptBudgetExhausted => …,
    });
```

### Absence

**Accepted**

- A value that may be missing is an `Option<T>`, never a nullable type. Absence is explicit and handled with `Match`, like a `Result`.
- `Option<T>` lives in the SDK: a `readonly record struct` whose default is `None`, with `Match`, `Map`, `Bind` and `ToResult`, an implicit conversion from `T`, and `MatchAsync` on `Task<Option<T>>`.
- Nullable values coming from outside, such as a framework query, become an `Option` at the edge through the `ToOption()` extension members of `Optional`.
- No non-private member exposes a nullable type in its signature, enforced by an architecture rule listed in [docs/architecture.md](../architecture.md#rules).

## Events

### Two kinds of events

**Accepted**

- **Domain events** are internal to a module. The application layer receives them from the aggregate and reacts directly. They need no bus.
- **Integration events** live in a module's `Contracts` and travel through the event bus to other modules and plugins.
- Each module translates domain events into integration events in a single place, which makes the public surface an explicit choice.

### Publishing

1. The application layer loads the aggregate.
2. It calls the operation and receives `Result<TEvent, TError>`.
3. On failure it returns the error. Nothing is stored and nothing is published.
4. On success it stores the new state.
5. It translates the event and publishes it on the bus.

### Event bus

**Accepted**

```csharp
public interface IEventBus
{
    ValueTask PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent;
}

public interface IHandle<in TEvent>
    where TEvent : IIntegrationEvent
{
    ValueTask HandleAsync(TEvent integrationEvent, CancellationToken cancellationToken);
}
```

- Plugins subscribe by composition: they register `IHandle<T>` implementations and the bus discovers them.
- `Avala.Runtime` implements the bus with `System.Threading.Channels`. Publishing writes the event to one routing queue and never blocks the publisher. A single router reads that queue in publishing order and posts each event to the mailbox of every handler of its type, and to the feed of every subscriber.
- **One mailbox per handler.** A mailbox is a channel with its own reader loop. A handler instance has one mailbox for every event type it handles, so a class that implements `IHandle<A>` and `IHandle<B>` sees its `A` and `B` events in the order they were published, one at a time. That reader loop owns the handler's state: a handler keeps plain collections and needs no lock.
- **No order between handlers.** Two handlers of the same event run concurrently, and a slow handler delays only its own mailbox. A handler never relies on another handler having seen an event: what it needs from another module arrives as an event that module publishes once its work is done, such as `UsageRecorded` after Observability recorded a report, or `BudgetLoaded` after a budget file was read.
- A failing handler is isolated and logged. Its mailbox goes on with the next event, and the other handlers are unaffected.
- Stopping cancels the token of every handler, completes every mailbox and returns once every handler has ended. An event published after the bus stopped is logged and ignored.
- Long work does not belong in a handler: it would delay the next event of that handler. Jobs, for example, hands the evaluation of a finished turn to the job's own queue and returns, see [the job flow coordinator](#job-flow-coordinator).
- Unit tests use an in-memory bus that records what was published.

### Event feed for view models

**Accepted**

View models do not implement handlers. They subscribe to a stream while they are active.

```csharp
public interface IEventFeed
{
    IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>(CancellationToken cancellationToken)
        where TEvent : IIntegrationEvent;

    Task DeliveredAsync(CancellationToken cancellationToken);
}
```

- `DeliveredAsync` completes once every handler has handled every event published before the call: a mark is routed behind them and posted to every mailbox. Events the handlers publish meanwhile are not awaited. It lets an observer, such as a simulation test about to advance the clock, know that no handler still holds an earlier event; it orders nothing between handlers.
- Activation starts an `await foreach`. Deactivation cancels the token and ends the subscription, so no subscription outlives its screen.
- Each subscription is a channel of its own: it receives its events in publishing order, and no handler can delay it. A subscriber therefore may see an event before the handlers of that event ran; one that needs a handler's result waits for the event that handler publishes afterwards.
- Events arrive off the UI thread. The SDK defines `IUiDispatcher` and the host implements it, so view models stay unaware of Avalonia.
- A page that follows the feed implements `IActivatable` from the SDK: the shell activates the page it selects and deactivates the one it leaves, which is when the page starts and ends its subscriptions.
- A view model that needs what happened before it opened reads it from an application-layer read model that a handler keeps from startup, such as the Workbench's [job board](#the-job-board), instead of subscribing itself: a subscription only sees what is published after it starts, and subscriptions to different event types have no order between them.

### Consistency without an outbox

**Accepted**

- Stored state is the truth. Events are notifications.
- On startup `JobRecovery` inspects every active job and resumes it from its state, so a lost event is recovered.
- Handlers are idempotent: receiving an event twice has the effect of receiving it once.

### Concurrency

**Accepted**

Components run concurrently, and they coordinate only through the event bus or `System.Threading.Channels`. Production code holds no lock, semaphore, monitor, mutex, wait handle, barrier or concurrent collection; an architecture rule and the banned API analyzer enforce it, see [the architecture](../architecture.md#rules).

- **One owner per piece of state.** Mutable state belongs to a single reader loop: a handler's mailbox, or a channel consumer. `SerialExecutor` in the SDK is that consumer in its simplest form: it runs the operations queued to it one at a time, in order, and hands each caller its result. The SQLite stores, the jobs' queues, the canvas throttle and the simulator's sessions own their state through one.
- **Queries read snapshots.** What other threads read is an immutable value the owner replaces whole, such as an `ImmutableDictionary` published with `Volatile` or `ImmutableInterlocked`. Publishing a reference is not coordination: no reader ever waits for a writer.
- **Signals.** A `TaskCompletionSource` used once, to say that something happened, is allowed.
- **Timers.** A timer callback never touches state. It hands its work to the owner: the canvas throttle queues its flush on its executor, and a silence alarm publishes `SilenceNoticed` for the watchdog's mailbox.
- Tests may use whatever they need to observe concurrent code.

## Job flow coordinator

The coordinator replaces the orchestrator. It is a set of small stateless classes in the application layer of Jobs, grouped by use case in the folders `Submission`, `Launching`, `TurnChecks`, `Recovery` and `Ledger`: the state of the flow is the `Job` aggregate itself. Each class keeps four or fewer dependencies. **Accepted**

| Class | Role |
| --- | --- |
| `JobLedger` | Stores a job, then publishes `JobProgressed` with its status |
| `JobQueues` | One serial queue per job: every piece of work on a job loads it and runs in that queue, in the order it was queued |
| `SubmitJob` | Creates a job, submits it, stores it and publishes `JobSubmitted` |
| `JobLauncher` | Prepares the workspace, opens the agent session, starts the job, stores it, announces `JobSessionStarted` and only then sends the instruction. It also relaunches a job after a restart, and continues a held job, or a job sent back from review, when a human sends it a message, resuming the job's conversation when it can |
| `WorkspacePlanner` | Prepares the workspace a job starts in, from its repository's `HEAD`, or for a child job from a checkpoint of its parent taken in the parent's queue, with the parent's rules commit; and finds the connection a job opens on |
| `ConnectionChooser` | Gathers the usable connections of the machine default's provider and asks the registered `IConnectionSelector`s to choose among them, for a job that names no connection and whose repository names none, then publishes `ConnectionChosen` |
| `PrepareJob` | Handles `JobSubmitted` by queueing the launch of the job, once every admission let a root job through |
| `CheckTurn` | Handles `TurnFinished` by queueing the evaluation of the turn, `SessionEnded` by queueing a hold as `SessionLost`, and `SessionResumable` by queueing the record of the job's resume token, then returns at once |
| `EvaluateTurn` | Runs in the job's queue: checkpoints the workspace, evaluates the gates, then passes the job, retries with feedback to the same session, or asks for help when the budget is spent |
| `CompletionGates` | Combines every registered gate into one verdict |
| `JobRecovery` | An `IStartupTask` that launches `Preparing` jobs and recovers `Running` or `Checking` jobs, each in its queue, leaving alone every job the ledger already recorded since the application started |
| `HoldJob` | Behind `IJobs.HoldAsync`, in the job's queue: holds a running job for a typed reason, stores it, halts its agent session and publishes `JobHeld`, see [Holding a job](#holding-a-job) |
| `ReviewJob` | Behind `IJobs.ContinueAsync`, `ApproveAsync`, `SendBackAsync` and `DiscardAsync`, in the job's queue: the commands a human gives a job that waits for them, see [Review and approval](#review-and-approval) |
| `Approvals` | Finds the job's workspace, reads the approval strategy its repository names in `.avala/jobs.json` at the base commit, and delivers the job's work through it; a child job's work goes into its parent's worktree instead, in the parent's queue |
| `JobCatalog` | Behind `IJobCatalog`: the jobs, each job's history of sessions and attempts, and its children and tree, from snapshots of the store |

- **One queue per job.** Evaluating a turn runs real builds and tests through the gates and can take minutes, so it never runs inside a handler. `CheckTurn` only finds the job of the session and queues the work, so the bus keeps delivering while the checks run. Different jobs proceed in parallel; the work on one job runs one piece at a time, in the order it was queued: its launch, the evaluation of each turn and every hold. A hold that arrives while a turn of the same job is being checked waits for that check and then finds a job that is no longer `Running`.
- The job stores its session before the instruction is sent, so a fast agent cannot finish a turn the job does not know yet.
- A message is only ever sent to a `Running` job's session, so a job held between storing its session and sending the instruction is never told.
- A turn that ends interrupted or failed fails the job, unless the job was held first: a held job is no longer `Running`, so `EvaluateTurn` ignores the end of the turn the hold interrupted. A person may continue the job in the same session before that end reaches the job's queue, since it travels through the provider, Agents' pump and `CheckTurn`'s mailbox while the continuation comes straight from the interface, and it would then find the job `Running` again in the same session and fail it. So `HoldJob` remembers the turn `IAgents.InterruptAsync` returned, in memory only, since the turns of a session never outlive the application, and `CheckTurn` lets the hold settle that turn's end, whenever it arrives, instead of evaluating it.
- **A lost session holds the job.** When the current session of a running job ends on its own, `SessionEnded` reaches `CheckTurn` before the failed `TurnFinished` that `AgentSessions` publishes after it, and both go to the job's queue in that order. The job is held as `SessionLost` and its session stopped, so the failed turn finds a held job and a human decides. `SessionEnded` of a session the job no longer uses is ignored.
- Handlers are idempotent. `EvaluateTurn` acts only on a job that is still `Running` in the session that finished, so a repeated `TurnFinished` changes nothing.
- **Recovery recovers only what an earlier run left.** Startup tasks run one after the other, so recovery may run after a human already submitted a job, or after that job started. `JobLedger` remembers every job it stored since the application started, and recovery, in the job's queue, leaves those jobs to their own flow: a submitted job still waits for its admission, and a started job keeps its one session.
- Recovery opens a new session in the existing workspace and calls `job.Recover`, which interrupts the attempt that was underway and starts a `Recovery` attempt. It asks to resume the job's conversation with its stored resume token: when the session resumed it, the agent is told that the harness restarted and to continue where it left off; otherwise the new session starts over with the instruction, as before.
- **Resume tokens.** `SessionResumable` reaches `CheckTurn` in the same mailbox as `SessionEnded`, and both are queued on the job in that order, so a session that issues a token and then dies has its token stored before the job is held. The token is stored with the job and announced with `JobResumable`.
- **Connection.** The connection a job opens on is decided in this order, and the first that names one wins:
  1. `JobRequest.Connection`, the connection the job names. `SubmitJob` checks it through `IConnections.CheckAsync` and rejects the request with `UnknownConnection` or `UnusableConnection`, storing and announcing nothing.
  2. The repository's default: `JobLauncher` reads `.avala/jobs.json` from the job's [base commit](#rules-from-the-base-commit) once the workspace exists, `{ "connection": "work" }`, through the `IRepositoryDefaults` port that `JobFileReader` implements in the `JobFiles` folder.
  3. [Capacity](#choosing-a-connection-by-capacity): without the field, without the file, or with `"connection": "auto"`, `ConnectionChooser` asks the registered `IConnectionSelector`s to choose among the usable connections of the machine default's provider, in catalog order, and publishes `ConnectionChosen` with the choice. Without a selector, or without a usable candidate, the session opens on the machine's default connection, as before.

  A connection named in the first two steps is used even when it is near its limit: the budget's [limit hold](#caps) applies to it as to any job, and the job is never moved silently. `auto` is a keyword of the file, so a connection named `auto` cannot be a repository's default. The file is parsed strictly, at most 16 KiB with no other field, and a file that is invalid or cannot be read fails the job as `ConnectionUnavailable` before any session opens, as does a connection that turns out unknown or unusable when the session opens, since the repository's preference is only known after submission. `Job.Start` records the connection the session actually opened on, so the job keeps it even if the default changes later: it is stored with the job, and recovery and `IJobs.ContinueAsync` open their new session on it. A recovery whose connection is gone fails the job as `ConnectionUnavailable`; a continuation whose connection is gone is rejected with `UnknownConnection` or `UnusableConnection` and the job stays held.
- **Parent and child jobs.** `JobRequest.Parent` optionally names the job a new job is a child of, which is how [delegation](#delegation) submits its children; Jobs stores it with the job and never interprets why. `SubmitJob` refuses a parent that does not exist with `UnknownParent`, one that is not `Running` with `ParentNotRunning`, and a child in another repository with `InvalidRequest`, storing and announcing nothing. `JobSubmitted` carries the parent, so `PrepareJob` asks no admission for a child: it runs inside its parent's slot, which already holds one. A child's workspace starts from its parent's current state: `WorkspacePlanner`, in `Launching`, runs in the parent's queue, checkpoints the parent's workspace as `Delegated to job <child>` and prepares the child's workspace from the parent's branch, whose tip is that checkpoint, with the parent's rules commit, see [Rules from the base commit](#rules-from-the-base-commit); running in the parent's queue serializes it with the parent's own checkpoints and with its other children. The tree is a column of the job, and `IJobCatalog` answers a job's children and its whole tree.
- **Autonomy.** `JobRequest.Autonomy` is the optional [level of autonomy](#autonomy-levels) a job asks for. Jobs stores it with the job, never interprets it, and announces it with every `JobSessionStarted` of the job, at launch, recovery and continuation alike, so Permissions applies it before the session's first instruction is sent. Whether it may apply is Permissions' decision: a job can be stricter than its repository, never looser.

### Completion gates

**Accepted**

Plugins join the flow through gates, without touching the core.

```csharp
public interface ICompletionGate
{
    ValueTask<GateVerdict> EvaluateAsync(CompletedAttempt attempt, CancellationToken cancellationToken);
}
```

- With no gate registered, every attempt passes, so the core works on its own.
- Gates run in registration order, and the first `Retry` wins: its feedback goes back to the agent.
- Verification registers a gate that runs the checks, see [Verification](#verification). Future plugins, such as security policy or a review by a second agent, are further gates.

### Holding a job

**Accepted**

Supervision and Budgets stop an agent from outside Jobs through one operation, so Jobs stays ignorant of who calls it and why beyond a typed reason. Jobs holds a job itself, through the same operation, when its session is lost.

```csharp
public interface IJobs
{
    ValueTask<Result<JobId, JobRejection>> SubmitAsync(JobRequest request, CancellationToken cancellationToken);
    ValueTask<Result<JobHold, JobRejection>> HoldAsync(JobId job, HoldReason reason, CancellationToken cancellationToken);
    ValueTask<Result<JobContinuation, JobRejection>> ContinueAsync(JobId job, string message, CancellationToken cancellationToken);
    ValueTask<Result<JobContinuation, JobRejection>> ContinueOnAsync(JobId job, ConnectionName connection, string message, CancellationToken cancellationToken);
    ValueTask<Result<JobId, JobRejection>> DiscardAsync(JobId job, CancellationToken cancellationToken);
    ValueTask<Result<JobApproval, JobRejection>> ApproveAsync(JobId job, CancellationToken cancellationToken);
    ValueTask<Result<JobContinuation, JobRejection>> SendBackAsync(JobId job, string feedback, CancellationToken cancellationToken);
}
```

- `Job.Hold(reason)` moves a `Running` job to `NeedsHelp` and concludes its underway attempt as `Interrupted`. No new state: a held job needs a human exactly like one whose retries ran out, and a hint resumes either. Any other state returns `CannotHold`, which `IJobs` reports as `NotRunning`; an unknown job is `UnknownJob`.
- `HoldReason` is `Stalled`, `SessionLost`, `BudgetExceeded`, `LimitNearlyReached`, `InvalidBudget`, `MemoryExceeded`, `Interrupted`, a person who interrupted the job from its conversation, or `Stopped`, a person who stopped it, see [Workbench](#the-composer).
- `HoldJob` stores the held job first, which publishes `JobProgressed` with `NeedsHelp`, and only then halts the session, so the end of the interrupted turn finds a job that is no longer `Running`. It then publishes `JobHeld` with a `JobHold`: job, session, reason and how the session was halted.
- Halting: `SessionLost` stops the session, since it is already gone, and `Stopped` stops it, since the person asked for the agent to end rather than pause; the worktree and the job's work stay, and a continuation opens a new session that resumes the conversation when the provider can. Every other reason interrupts the turn through `IAgents.InterruptAsync` and keeps the session open for a human: `Interrupted` when a turn was interrupted, `Idle` when none was running. A provider that cannot interrupt, or an interruption that fails otherwise, stops the session instead: `Stopped`. A session that is no longer open is `AlreadyClosed`.
- The hold reason is not persisted: the stored job is `NeedsHelp` with an interrupted attempt, and the reason lives in `JobHeld` and in the audit of the module that held it. Persisting it arrives with the first schema migration.

**Continuing a held job.** `IJobs.ContinueAsync` is how a human answers a job that needs help, whatever held it: it hints the job with the human's message, in the job's queue, and returns a `JobContinuation` with the session the job continues in and how (`ContinuedIn`).

- `SameSession`: the job's session is still open, as after a `Stalled` or budget hold that interrupted the turn, or after the retries ran out. The message goes to that session.
- `ResumedConversation`: the session is gone, because it was lost or the application restarted since. A new session opens in the job's workspace with the job's resume token, and the provider resumed the conversation, so the message alone is sent.
- `NewConversation`: the session is gone and the conversation could not be resumed: the provider cannot resume, there is no token, or the provider rejected it. The new session starts over, and gets the instruction followed by the message.
- Jobs decides whether the session is open by asking `IAgents.IsOpen`, never by the hold reason, which is not stored. A new session is opened before the job changes, on the job's connection, so a job whose workspace is gone (`WorkspaceUnavailable`), whose connection is gone (`UnknownConnection`, `UnusableConnection`) or whose agent cannot start (`AgentUnavailable`) stays held. A job that is not `NeedsHelp` is `NotHeld`, an empty message `EmptyMessage`, an unknown job `UnknownJob`.

**Continuing on another connection.** A conversation belongs to the account it ran on, so a job held as `LimitNearlyReached`, or held for any other reason, is never moved to another connection on its own: it waits for its window to reset, or for a person. `IJobs.ContinueOnAsync` is the explicit way to move it: in the job's queue, it opens a new session on the named connection in the job's workspace, without the resume token, so the agent starts a new conversation with the instruction followed by the message; `Job.ContinueOn` records the new session and connection, forgets the token and starts a `Hint` attempt; the old session, if it is still open, is stopped; and the answer is a `JobContinuation` in `NewConversation`. The job keeps the new connection for recovery and later continuations. A job that is not `NeedsHelp` is `NotHeld`, the connection it already runs on `SameConnection`, an empty message `EmptyMessage`, an unknown job `UnknownJob`, a missing workspace `WorkspaceUnavailable`, and a connection that cannot open `UnknownConnection`, `UnusableConnection` or `AgentUnavailable`; any of them leaves the job held where it was.

**Discarding a job.** `IJobs.DiscardAsync` discards an open job in its queue, whatever it is doing: `Job.Discard` interrupts the attempt underway, and the job ends `Discarded`. A job that already ended is `NotDiscardable`, an unknown job `UnknownJob`. It is one of the commands of [review](#review-and-approval); Resources reclaims the worktree of a discarded job at once by default.

**An ended job stops its session.** Whenever `JobLedger` stores a job that ended, `Approved`, `Discarded` or `Failed`, it stops the job's session through `IAgents.StopAsync` before it publishes the job's `JobProgressed`, so the agent's process does not outlive its job and Resources reaps what the session left behind before it reclaims the worktree.

**Admitting launches.** `IJobAdmission` is an extension point in `Jobs.Contracts`: `PrepareJob` awaits every registered admission before it queues the launch of a submitted job, in its own mailbox, so submitted jobs start in submission order as admissions let them. With no admission registered, every job launches at once. Budgets registers the first one, the [limit of running jobs](#running-jobs). Recovery at startup and continuations are not admitted: they resume jobs that already held a slot. Nor is a child job: it works for a parent that holds a slot and waits for it, so admitting it could only deadlock the parent; the [delegation](#delegation) fan-out cap bounds how many children run at once.

### Review and approval

**Accepted**

A job that passed its gates waits for a human as `AwaitingReview`. The human reads what it did, then approves it, sends it back with feedback or discards it. Approving delivers the work through an approval strategy that the repository chooses, an extension point plugins implement, like the gates.

**Queries for the views.** `IJobCatalog` in `Jobs.Contracts` answers from snapshots of the store, read without tracking, so a query never sees, nor disturbs, a job its queue is changing, and never waits for a turn being checked.

- `ListAsync` lists every job as a `JobSummary`: repository, instruction, submission time, status, connection, autonomy, workspace and parent, in submission order.
- `ChildrenAsync(job)` lists a job's children in submission order, and `TreeAsync(job)` the job with its children and theirs, as a `JobTree` of summaries; an unknown job has no tree.
- `HistoryAsync(job)` adds the job's attempts, each with its number, origin, outcome, guidance and session, and its sessions in the order they started, each with the attempts it ran. An unknown job has none.

**The diff.** `IWorkspaceChanges` in `Workspaces.Contracts` compares the job's branch, its latest checkpoint, with its base commit, through git and `IProcessRunner`: `DiffAsync` lists the files changed, added, modified or deleted, with the lines added and removed, absent for a binary file; `FileDiffAsync(path)` gives the hunks of one file on demand, each with its ranges, its section header and its context, added and removed lines. Renames count as a deletion and an addition, and the worktree's uncommitted edits are not part of it: the diff is exactly what approval delivers. An unknown workspace is `UnknownWorkspace`, a file the job did not change `FileUnchanged`, a git failure `GitFailed`.

**Commands.** Each runs in the job's queue, so it never races the evaluation of a turn.

- `ApproveAsync`: only a job `AwaitingReview`, otherwise `NotAwaitingReview`; an unknown job is `UnknownJob`. `Approvals` finds the job's workspace (`WorkspaceUnavailable`), reads its strategy and delivers through it. Only a delivery that succeeds approves the job: `Job.Approve` moves it to `Approved`, `JobLedger` stores it, stops its session and publishes `JobProgressed`, and `ReviewJob` then publishes `JobApproved` with the `JobApproval`: the job and the `ApprovalDelivery`, the strategy, the branch the work is on and the commit it created, if any. A delivery refused leaves the job `AwaitingReview`, its session open, nothing published, and returns the strategy's rejection.
- `SendBackAsync(job, feedback)`: only a job `AwaitingReview`, otherwise `NotAwaitingReview`; blank feedback is `EmptyMessage`. It starts a new round through the same machinery as continuing a held job: `Job.SendBack` starts a `SendBack` attempt with a fresh round of retries, and the feedback goes to the job's session when it is still open, or to a new session that resumes the conversation, or starts over with the instruction followed by the feedback, exactly as `ContinueAsync` decides, with the same rejections when the workspace, the connection or the agent is gone. The job's gates judge the new round like any other.
- `DiscardAsync`, unchanged, see [Holding a job](#holding-a-job).
- After approval, the worktree follows the retention of [Resources](#worktrees): by default an approved job's worktree and branch are kept.

**The approval strategy.** An extension point in `Jobs.Contracts`:

```csharp
public interface IApprovalStrategy
{
    string Name { get; }
    ValueTask<Result<ApprovalDelivery, JobRejection>> DeliverAsync(ApprovalRequest request, CancellationToken cancellationToken);
}

public sealed record ApprovalRequest(JobId Job, string Instruction, int Attempts, WorkspaceInfo Workspace);
```

- Strategies are registered through DI. The repository names one in `.avala/jobs.json` at the job's [base commit](#rules-from-the-base-commit), `{ "approval": "merge" }`, next to its preferred connection; without the file or the field the strategy is `keep`. The file is parsed strictly, as for the connection: at most 16 KiB, only `connection` and `approval`, each a non-empty string, `connection` naming a connection or `auto` for [capacity](#choosing-a-connection-by-capacity), `autopilot`, an object of plain values Jobs does not read and the [Autopilot](#autopilot) module parses strictly, and `delegation`, an object of plain values and lists of them that the [Delegation](#delegation) module parses strictly. A file that is invalid or cannot be read is `InvalidJobFile`, and a name no registered strategy has is `UnknownApprovalStrategy`; either leaves the job awaiting review. When two strategies share a name, the first registered wins.
- The core registers two. `keep`: nothing is touched; the delivery names the job's branch, ready to be merged by hand or by a later strategy. `merge`: the job's checkpoints are squashed into one commit and land on the base branch, see below. Opening a pull request arrives as a strategy of a GitHub plugin, without touching the core.

**The merge.** `MergeStrategy` composes the commit message and asks `IWorkspaceChanges.MergeAsync`, which never forces anything:

1. The base branch is the branch the repository's `HEAD`, or the requested base reference, named when the workspace was prepared, stored with the workspace as `WorkspaceInfo.BaseBranch`. A workspace prepared from a detached `HEAD` has none: `NoBaseBranch`, as when the branch no longer exists.
2. `git merge-tree --write-tree --merge-base=<base commit> <base branch tip> <job branch tip>` applies the job's changes since its base commit onto the base branch's current tip, in memory, touching no working tree and no index. A conflict, whether the base moved or not, is `MergeConflict`; `IWorkspaceChanges.ConflictsAsync` lists the conflicting files with the same command. A base that moved without conflicting is fine: the job's changes land on top of it.
3. When the resulting tree is the tip's own tree, the base already holds the work: the merge succeeds with no commit, so approving again after an interrupted approval delivers nothing twice.
4. When the base branch is checked out in a worktree of the repository, typically the user's main checkout, found with `git worktree list --porcelain -z`, that checkout must have no staged or unstaged change to a tracked file, by `git status --porcelain --untracked-files=no`; otherwise `BaseCheckoutDirty` and nothing is touched.
5. `git commit-tree` writes one commit whose tree is the merged tree and whose single parent is the tip, with the repository's configured identity, or `Avala <avala@localhost>` when it has none, unsigned. The message is the first line of the job's instruction, cut at 72 characters, then the whole instruction when it is longer, then `Squashed by Avala from the checkpoints of N attempts.` and the trailer `Avala-Job: <id>`.
6. `git update-ref refs/heads/<base> <commit> <tip>` moves the branch only if it still points at the tip it was merged onto: an atomic compare and swap. A branch that moved meanwhile is `BaseMoved`, and approving again merges onto the new tip.
7. In the checkout of the base branch, if any, `git read-tree -m -u <tip> <commit>` brings the index and the files to the new commit, a two-tree fast-forward that refuses to overwrite a local change or an untracked file. If it refuses, the branch is moved back to the tip with the same compare and swap and the result is `BaseCheckoutDirty`; the user's files are left exactly as they were.
8. The job's branch keeps its checkpoints; the delivery names the base branch, the new commit and the checkout it updated.

Git 2.40 or later is required, for `merge-tree --merge-base`. Plumbing runs no hooks, so neither the user's commit hooks nor the merge hooks run on the squashed commit.

**Approving a child job.** A child's work belongs to its parent, never to the repository's base branch, so `Approvals` ignores the repository's `approval` for a job with a parent and delivers it into the parent's worktree with the same merge, whatever the strategy the repository names:

1. The child's workspace was prepared from the parent's branch, so its `BaseBranch` is the parent's branch, checked out in the parent's worktree, and its base commit the checkpoint the child started from.
2. In the parent's queue, so nothing else touches the parent's worktree meanwhile: a parent that is neither `Running` nor `NeedsHelp` is `ParentNotRunning`, since a parent waiting for review, being checked or ended must not change under its reviewer; an unknown parent is `UnknownParent`.
3. The parent's workspace is checkpointed as `Before integrating job <child>`, which commits whatever the parent's agent wrote meanwhile, so the dirty-checkout rule of step 4 of the merge holds by construction and nothing the parent wrote is lost.
4. The `merge` strategy then squashes the child's checkpoints into one commit on the parent's branch and brings the parent's worktree to it with the two-tree fast-forward, refusing to overwrite anything: a conflict is `MergeConflict`, and the child stays `AwaitingReview`.

The delivery names the strategy `merge`, the parent's branch and the new commit, or none when the parent already held the work.

## Agents

### Contract

**Accepted**

`Avala.Agents.Contracts` is the only thing a provider plugin depends on.

```csharp
public interface IAgentProvider
{
    ProviderInfo Info { get; }
    AgentCapabilities Capabilities { get; }
    ValueTask<Result<IAgentSession, AgentError>> StartAsync(SessionOptions options, CancellationToken cancellationToken);
}

public interface IAgentSession : IAsyncDisposable
{
    SessionId Id { get; }
    Option<AgentAccount> Account { get; }
    IAsyncEnumerable<IAgentEvent> Events { get; }
    ValueTask<Result<TurnId, AgentError>> SendAsync(UserTurn turn, CancellationToken cancellationToken);
    ValueTask<Result<ItemId, AgentError>> RespondAsync(PermissionDecision decision, CancellationToken cancellationToken);
    ValueTask<Result<ItemId, AgentError>> AnswerAsync(FormAnswer answer, CancellationToken cancellationToken);
    ValueTask<Result<TurnId, AgentError>> InterruptAsync(CancellationToken cancellationToken);
}
```

- `SessionOptions` holds harness concepts only: working directory, permission mode, the `Resume` token of a conversation to resume, the harness `Tools` the agent may call, the `Connection` environment the session runs with, see [Connections](#connections), and the `Processes` launcher every process of the session starts through, see [Process trees](#process-trees). Paths and protocols belong to each provider's own settings, and how a provider applies a connection's configuration folder, key and settings is its own business.
- `AgentSessions` opens every session in `AskEveryTime`: the agent asks before every file edit and every command, so every action reaches the policy of [Permissions](#permissions), which allows edits inside the workspace by default. A provider is never told to allow edits on its own, since that would let edits bypass the policy, its guard and its audit. Without the Permissions plugin nothing answers for the harness, and every request waits for a human through `IAgents.RespondAsync`, the documented behavior of `Ask`.
- Behavior depends on `AgentCapabilities`, never on a provider's name: partial output, reasoning, interruption, resumption, injected tools, usage, cost, limits and [forms](#human-input-forms) (`AsksQuestions`).
- `PermissionDecision` carries an optional `Message`: with `Deny`, it tells the agent why and what to do instead, the "no, do this instead" of a harness's permission prompt. "Don't ask again" is never sent to a provider: it is a [session rule](#session-rules) of Avala's policy.

Other modules use agents through `IAgents`, in two steps:

```csharp
public interface IAgents
{
    ValueTask<Result<OpenedSession, AgentError>> OpenAsync(AgentRequest request, CancellationToken cancellationToken);
    bool IsOpen(SessionId session);
    ValueTask<Result<AgentTurn, AgentError>> SendAsync(SessionId session, string message, CancellationToken cancellationToken);
    ValueTask<Result<ItemId, AgentError>> RespondAsync(SessionId session, PermissionDecision decision, CancellationToken cancellationToken);
    ValueTask<Result<ItemId, AgentError>> AnswerAsync(SessionId session, FormAnswer answer, CancellationToken cancellationToken);
    ValueTask<Result<TurnId, AgentError>> InterruptAsync(SessionId session, CancellationToken cancellationToken);
    ValueTask<Result<SessionId, AgentError>> StopAsync(SessionId session, CancellationToken cancellationToken);
}
```

- `OpenAsync` opens a session in the working directory of `AgentRequest` and returns an `OpenedSession`: its `SessionId` and whether it `Resumed` the conversation of the request's optional `Resume` token. `SendAsync` sends a message and returns the `AgentTurn` it started. Opening and sending are separate so the caller can store the session before any turn can finish: Jobs records it on the job first.
- `SessionStarter` resolves the request's [connection](#connections), which chooses the provider, and builds the `SessionOptions` from the provider's capabilities, so no caller decides for a provider: it passes the harness tools only to a provider that `AcceptsTools`, and the resume token only to one that `CanResume`. When such a provider rejects the token, it starts a fresh session instead, which is not `Resumed`.
- **Decorators.** Before it starts a session, `SessionStarter` wraps the provider in every registered `IAgentProviderDecorator`, in registration order, so a plugin can observe or adapt every session of every provider without Agents knowing it. A decorator returns an `IAgentProvider` that keeps the provider's `Info` and `Capabilities`. The [recorder](#session-recording-and-replay) is the first one.
- `IsOpen` says whether a session is open and its event stream has not ended. Jobs asks it before continuing a held job in its old session.
- `RespondAsync` answers the permission request of a live session and returns the item it unblocked. A session that is not open returns `SessionClosed`.
- `AnswerAsync` answers the open form of a live session, see [Human-input forms](#human-input-forms). A provider that does not declare `AsksQuestions` returns `Unsupported` without being asked; an item with no open form returns `NoPendingForm`; an answer that does not fit its form returns `InvalidAnswer`, and neither reaches the provider.
- `InterruptAsync` asks the agent of a live session to end its running turn, through `IAgentSession.InterruptAsync`; the agent then ends the turn as `Interrupted`. A provider whose capabilities do not declare `CanInterrupt` returns `Unsupported` without being asked, and a session that is not open returns `SessionClosed`. The provider contract does not change.
- `AgentSessions` implements `IAgents`. It announces every session it opens with `SessionOpened`, carrying the `ProviderInfo` of its provider, the connection it opened on and the session's account, before pumping any of its events. It pumps the events of each session through the `Turn` aggregate and publishes the accepted ones as `AgentActivity`, plus `TurnFinished` when a turn ends and `SessionResumable` when a provider that `CanResume` issues a resume token.
- When a session's event stream ends on its own, `AgentSessions` publishes `SessionEnded` with `Crashed` when the stream failed and `Closed` when it completed. It publishes it before closing the turn left live, if any, as `Failed`, so a consumer learns the session is gone before it sees that turn fail. A stream that completes while a turn is live no longer leaves the turn open forever. Stopping a session through `StopAsync`, including at shutdown, never publishes `SessionEnded`, so a restart is never mistaken for a lost session and recovery still finds its jobs running; once the provider's session is disposed it publishes `SessionStopped`, the fact Resources reaps the session's process tree on. At shutdown the bus has stopped by then, and the runtime closes every tree itself.

### Process trees

**Accepted**

Every process an agent starts belongs to its session's process tree, contained by the operating system where it can, so the harness can measure what the session uses and reap what it leaves behind.

```csharp
public interface IProcessLauncher
{
    IReadOnlyDictionary<string, string> Environment { get; }
    Result<Process, ProcessError> Start(ProcessStartInfo info);
}
```

- **The harness hands the session a launcher.** `SessionStarter` opens a tree for the request's working directory through `IProcessTrees`, the runtime's port, and passes it to the provider as `SessionOptions.Processes`. A provider starts its own process, and anything else it runs, through `Processes.Start`, with the redirections and arguments it needs; the launcher adds the tree's environment, contains the process and returns it. The provider never learns how containment works on the platform, and a launcher refuses `UseShellExecute`, whose environment cannot be set. `SessionOpened.ProcessTree` names the tree, and a session that cannot start closes it at once.
- **The tree's environment.** Every process started in a tree gets `AVALA_PROCESS_TREE`, the tree's identifier, and the variables the registered `IProcessEnvironment` contributors give the tree's folder, the [port lease](#port-leases) today. A provider that runs no process can read them from `Processes.Environment`.
- **Processes the harness runs.** `IProcessRunner` starts a process whose `WorkingDirectory` is the folder of an open tree inside the most recent such tree, so the checks of Verification, which run in the worktree, and the git commands of a checkpoint, which Workspaces runs in the worktree, join the tree of the job's session. A process run anywhere else is not contained.
- **One port, three platforms.** The runtime's `ProcessTrees` registry implements `IProcessTrees` over `IContainment`, an internal port with one implementation per platform:

| Platform | Containment | Members |
| --- | --- | --- |
| Linux | Each process starts through `setsid` when it is on the `PATH`, so it leads a new session and process group. No cgroup: a delegated cgroup needs systemd user delegation, which neither CI nor every desktop offers | Every process, not a zombie, started after the tree opened, that carries the tree's `AVALA_PROCESS_TREE` in `/proc/<pid>/environ`, belongs to the session of a process the tree started, or descends from a member. A process that clears its environment and leaves its session is the only escape |
| Windows | A job object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, the process assigned to it right after it starts; its children join it as they start. Where Windows refuses a job object, the tree tracks the processes it started | The job's process list |
| macOS | None: no `setsid` and no rootless cgroup. Best effort, not run by CI | The processes whose environment, read with `ps -E`, carries the tree's variable, and their descendants |

- **Console hosts.** On Windows a console process started without a console of its own, as `CreateNoWindow` does, gets a `conhost.exe` that joins its job. It is a member, so closing the tree kills it and waits for it, but a member list never reports it on its own: its memory and CPU time count in its client, the member that is its parent, and a host whose client is not a member is left out, since it ends with its client. Orphans and usage therefore name the processes the agent started. .NET offers no clean way to start a console process with no host at all, so the harness does not try.
- **Closing a tree** kills every member, process by process, until none is left or twenty rounds passed, and returns the survivors; on Windows closing the job handle kills what remains. The registry closes every open tree when the application shuts down, so nothing Avala started outlives it, except on Linux and macOS a process that escaped its tree.
- **Listening ports.** `IListeningPorts` lists the listening TCP sockets of the machine, with the process that holds each one when it is among the given owners: from `/proc/net/tcp` and `tcp6` and the owners' `/proc/<pid>/fd` on Linux, from the TCP listener tables of `GetExtendedTcpTable` on Windows, and from `lsof` on macOS. Windows reads the tables in process rather than starting `netstat`: a port is listed when a tree opens, at every sample and when a session ends, and on Windows every child process costs a console host, the runtime's global process-creation lock and a blocked thread pool thread for each redirected stream it reads, since .NET reads them from synchronous anonymous pipes.
- **Known limits.** On Windows a process can start a child in the moment between its start and its assignment to the job; the child then escapes. Linux reads processes' start times in clock ticks of 100 per second, the value Linux reports on every common architecture.

### Agnostic events

**Accepted**

Every provider translates its protocol into one closed set of events. Each event carries its `SessionId` and `TurnId`.

| Event | Meaning |
| --- | --- |
| `TurnStarted`, `TurnCompleted` | A turn begins, and ends as finished, interrupted or failed |
| `ItemStarted` | Work begins: a message, reasoning, a file edit, a command, a search, a web request, an MCP call, a subagent |
| `CanvasStarted` | A canvas begins, with a title and one of the media types the canvas tool offers, today `image/svg+xml` or `text/markdown` |
| `ItemProgressed` | More content for an open item or canvas, appended in order |
| `ItemCompleted` | An item or canvas ends as succeeded, failed, cancelled, abandoned or expired |
| `PermissionRequested`, `PermissionResolved` | An open item waits for a decision, and gets it |
| `FormRequested`, `FormAnswered` | A form opens an item that waits for a human's or the policy's answer, and gets it; `ItemCompleted` closes it, see [Human-input forms](#human-input-forms) |
| `PlanUpdated` | The agent's plan and the status of each step |
| `UsageReported` | Tokens used: input, output, cache reads, cache writes and reasoning, plus the cost when the provider reports it |
| `LimitReported` | A usage limit: its window, the fraction used and when it resets |
| `ResumeTokenIssued` | The opaque token that resumes this session's conversation from here, see [Resuming a conversation](#resuming-a-conversation) |
| `ToolCalled`, `ToolReturned` | A call of a harness tool the harness executes opens an item that waits for its result, and the provider reports the result it received, see [Harness tools](#harness-tools) |

All work inside a turn shares one lifecycle: started, progressed, completed. Messages, tools and canvases therefore get the same integrity guarantees and the same rendering pipeline.

### Resuming a conversation

**Accepted**

A provider that declares `CanResume` lets the harness continue a conversation in a new session, after the application restarted or after a session was lost.

- **The token is the provider's.** `ResumeToken` is opaque text the provider issues and only that provider reads: a Claude Code session id, a Codex thread id, or whatever its protocol resumes from. The core stores and returns it, never parses it.
- **Issued in a turn.** The provider reports it with `ResumeTokenIssued`, inside a turn, whenever its protocol makes it known, such as the start message of the turn. It may issue a new one in a later turn; the latest wins. The `Turn` aggregate passes it through like a plan or a usage report.
- **Announced only by capability.** `AgentSessions` publishes `SessionResumable` for a token only when the provider declares `CanResume`; a token from another provider is still forwarded as activity, but nothing will try to resume it.
- **Resumed through the options.** `AgentRequest.Resume` carries the token, and `SessionStarter` passes it in `SessionOptions.Resume` only to a provider that `CanResume`. The provider either resumes that conversation or rejects the token with `CannotResume`; the harness then starts over in a fresh session, so a stale token never blocks a job.
- **Jobs keeps it.** Jobs stores the latest token of a job's current session with the job, and uses it for [recovery](#job-flow-coordinator) and when a human [continues a held job](#holding-a-job).

### Harness tools

**Accepted**

The harness offers tools of its own to the agent, starting with the canvas.

```csharp
public sealed record HarnessTool(string Name, string Description, string InputSchema, ToolSurface Surface);

public enum ToolSurface { Canvas, Executed }

public sealed record ToolResult(ItemId Item, string Content) { bool IsError; }
```

- **Shaped like MCP.** A tool is a name, a description for the model and its input as a JSON Schema in text, exactly what an MCP server lists. The real adapter transports the tools through MCP, as a server it gives its agent: the Claude Code plugin serves them, with its permission prompt, from an MCP server inside the session, see [the Claude Code provider](claude-code.md#avalas-mcp-server). The contract does not depend on it.
- **Contributed, not known.** A module that offers a tool registers its `HarnessTool` in the container. `SessionStarter` gives every registered tool to providers that declare `AcceptsTools`, and none to the others. Agents never knows which module offered a tool.
- **The surface says how a call is reported.** The adapter knows its own protocol, so it translates a call of an injected tool into agnostic events; the surface of the tool tells it which ones. `Canvas`: a call is a canvas item whose identifier is the call's. `CanvasStarted` opens it with the call's `title` and `mediaType`, `ItemProgressed` streams its `content`, in chunks when the provider streams partial input or at once otherwise, and `ItemCompleted` closes it as succeeded, or failed when the input cannot be read. The adapter answers the call to the agent itself; nothing else needs to run.
- **`Executed`: the harness runs the call and answers it.** The minimal version of the delegation plan's harness-executed tools, which [Autopilot's follow-ups](#follow-ups) needed first. The adapter reports a call as `ToolCalled`, with the call's item, the tool's name and its input as JSON text; it opens an item of the turn, as `CanvasStarted` does. The harness answers with `IAgents.ReturnAsync(session, ToolResult)`, which reaches the provider through `IAgentSession.ReturnAsync`; the adapter hands the result to its agent, reports it with `ToolReturned` and closes the item with `ItemCompleted`, succeeded, or failed when the result `IsError`.
- **Who answers.** The module that offers an executed tool handles `AgentActivity` of `ToolCalled` for its own tool's name and answers through `IAgents.ReturnAsync`, exactly as Permissions answers `PermissionRequested` through `RespondAsync`. Agents never runs a tool and never learns which module offered it.
- **Integrity.** The `Turn` aggregate keeps every call waiting for its result: a `ToolReturned` for an item with no pending call, or whose result names another item, is `NoPendingCall`, and a pending call never expires, since the harness owes the answer. `IAgents.ReturnAsync` refuses a provider that does not declare `AcceptsTools` with `Unsupported`, an item with no pending call with `NoPendingCall` and a session that is not open with `SessionClosed`, without asking the provider; `LiveSession` keeps the pending calls from the accepted `ToolCalled` until `ToolReturned`, the item's completion or the end of its turn.
- **Long calls and calls at once.** A result may come long after its call: delegation answers when a child job ends, minutes or hours later. A turn may hold several pending calls at once, answered in any order, as an agent that issues parallel tool calls does; the `Turn` aggregate and `LiveSession` already keep a set of them. Supervision pauses the job's silence window while any call of its session is pending, like a form, and restarts it when the last one is answered or closed, or the turn ends.
- **A call outlived by its turn.** A call has no result after its turn ended: an interrupted turn closes its pending calls as `Abandoned`, and `ReturnAsync` then answers `NoPendingCall` or `SessionClosed`. The module that offers the tool keeps the result in its own records anyway.

### Human-input forms

**Accepted**

Harnesses stop to ask a human: Claude Code's multiple-choice questions, with a header, options described one by one, one marked recommended, several selectable and an "other" answer in free text; its permission prompts with "yes", "yes, and don't ask again" and "no, do this instead"; its plan approval; Codex and the others have their own. The core defines one closed, provider-agnostic form, and each provider plugin translates its own modals into it. Providers compose the format; they never add core types, so one generic form component renders any form.

```csharp
public enum FormPurpose { Permission, Question, PlanApproval, Other }
public enum FieldKind { SingleChoice, MultipleChoice, FreeText, Confirmation }

public sealed record FormOption(string Label, string Description, bool Recommended = false);
public sealed record FormField(string Id, string Header, string Prompt, FieldKind Kind, IReadOnlyList<FormOption> Options, bool AcceptsFreeText = false);
public sealed record AgentForm(FormPurpose Purpose, string Title, string Context, IReadOnlyList<FormField> Fields);

public sealed record FieldAnswer(string Field) { IReadOnlyList<string> Chosen; Option<string> Text; bool Confirmed; }
public sealed record FormAnswer(ItemId Item, IReadOnlyList<FieldAnswer> Fields) { bool Declined; Option<string> Message; }
```

- **The purpose says what the policy answers**, never who asked: a question, a plan to approve, a permission asked as a form, or anything else. A permission whose facts the provider knows, its item kind and target, stays a `PermissionRequested`, so the policy can match it; `Permission` forms are for prompts without such facts.
- **An item like any other.** `FormRequested` opens an item of the turn, as `CanvasStarted` does, and the turn waits in `AwaitingAnswer`, as it waits in `AwaitingPermission`: a form never expires, and Supervision does not count the wait as silence. The provider reports the answer with `FormAnswered`, then closes the item with `ItemCompleted`: succeeded, or cancelled when the form was declined. Forms get the integrity of every item.
- **Well formed or rejected.** The `Turn` aggregate rejects a malformed form with `MalformedForm` and opens no item: a purpose and a kind out of their lists, a form without fields, fields without an identifier or sharing one, a choice without options, a free text or confirmation field with options, options without a label or sharing one, or more than one recommended option in a field.
- **Answers fit their form.** A declined form carries no field answers and may carry a `Message` for the agent. Otherwise every field is answered exactly once: a single choice by one of its labels or, when it accepts free text, by text alone; a multiple choice by distinct labels and, when accepted, text; free text by non-blank text; a confirmation by `Confirmed`, with text when accepted. `IAgents.AnswerAsync` checks it against the form the session has open, which `AgentSessions` keeps from the moment the turn accepts `FormRequested` until it is answered, closed or its turn ends.
- **Only by capability.** A provider that declares `AsksQuestions` may ask forms; the conformance kit reports a form from any other.

### Accounts

**Accepted**

A provider may report the account a session runs under, as `IAgentSession.Account`: an opaque `Id` and a `Label` for people, or none. It is known when the session opens, `SessionOpened` carries it, and it never changes during the session. Observability aggregates usage by account with it.

### Connections

**Accepted**

A user may hold several subscriptions of the same harness, such as a work and a personal account, or an API key next to a subscription. A **provider** is the adapter that speaks one harness's protocol, one per harness, inside its plugin. A **connection** is a named, configured instance of a provider: the provider, a credential source and settings, composed, never inherited. A user has any number of connections per provider.

```csharp
public readonly record struct ConnectionName(string Value);

public sealed record ConnectionEnvironment
{
    public Option<string> ConfigurationDirectory { get; init; }
    public Option<Secret> ApiKey { get; init; }
    public IReadOnlyDictionary<string, string> Settings { get; init; }
}

public interface ICredentialSource
{
    string Source { get; }
    ValueTask<Result<ConnectionEnvironment, ConnectionError>> ResolveAsync(CredentialRequest request, CancellationToken cancellationToken);
}

public interface IConnections
{
    ValueTask<ConnectionCatalog> CatalogAsync(CancellationToken cancellationToken);
    ValueTask<Result<ConnectionInfo, ConnectionError>> CheckAsync(Option<ConnectionName> connection, CancellationToken cancellationToken);
}
```

- **The provider never learns the source.** `ConnectionRegistry` resolves a connection when a session opens: its declaration, its provider among the registered ones, and its credential through the `ICredentialSource` it names. `SessionStarter` hands the result to the provider as `SessionOptions.Connection`, an agnostic `ConnectionEnvironment`: the harness configuration folder to use, an API key, and the connection's settings. The adapter decides inside its own plugin how to apply it, such as which environment variable names the folder or carries the key; the core never names one. `Secret` prints as `[secret]`, so a key never reaches a log through a record's text.
- **Credential sources are an extension point.** The interface lives in `Agents.Contracts`, and plugins register implementations through DI, named by `Source`. Agents registers the first two, in its `Credentials` folder: `login`, a subscription login kept apart per connection through the harness's own configuration folder, which the `reference` names, absolute or relative to the data folder, or `connections/<name>` under the data folder by default; and `apiKey`, a key held as a reference to the environment variable the `reference` names. A missing folder is `MissingFolder`, an unset or empty variable `MissingVariable`, a source without its reference `MissingReference`. Later sources, such as the operating system's keychain or a corporate gateway, add an implementation without touching the core or an adapter.
- **Never a silent fallback.** A connection that cannot be resolved is a typed error, never another account: an unknown name is `UnknownConnection`, a provider that is not installed `UnknownProvider`, a source nobody registered `UnknownSource`. A rejected `connections.json` makes every connection unusable rather than falling back to the implicit ones.
- **Opening on a connection.** `AgentRequest.Connection` names the connection, or none for the default one. `OpenedSession` and `SessionOpened` carry the connection the session opened on. `IAgents.OpenAsync` reports `UnknownConnection` for an unknown name, `ProviderUnavailable` when the connection's provider is not registered or no connection exists, and `UnusableConnection` for everything else; the detailed `ConnectionError` is logged and answered by `IConnections.CheckAsync`, which resolves a connection without opening anything and returns its name and provider, never its secrets.
- **The implicit default.** Without `connections.json`, every registered provider has one implicit connection named after its identifier, with no credential, which leaves the provider on its own default configuration, and the first registered provider's is the default: the behavior before connections existed.
- **Belongs to the machine.** Connections are declared in the data folder, never in a repository. A repository may only name the connection its jobs prefer, see [the job flow coordinator](#job-flow-coordinator).

#### Discovery

A machine may hold several subscriptions of the same harness, such as two logins of Claude Code in two configuration folders, without anyone declaring them. Discovery is an extension point of the provider contract in `Agents.Contracts`, which a provider plugin implements when its harness keeps its accounts somewhere it can find:

```csharp
public sealed record CredentialReference(string Source, string Reference);

public sealed record DiscoveredConnection(ConnectionName Name, string Provider, CredentialReference Credential)
{
    public IReadOnlyDictionary<string, string> Settings { get; init; }
}

public interface IConnectionDiscovery
{
    ValueTask<IReadOnlyList<DiscoveredConnection>> DiscoverAsync(CancellationToken cancellationToken);
}
```

- **References only.** A discovered connection carries the name of a credential source and a reference to the secret, such as a configuration folder's absolute path for `login` or an environment variable's name for `apiKey`, never a secret, and discovery never reads one. It is resolved like a declared connection, through the source it names, when a session opens.
- **Stable, readable names.** The plugin derives each name from what it found, such as the folder's name, so the same machine always yields the same names, valid by the rules of `connections.json`.
- **Merging.** `ConnectionRegistry` asks every registered discovery once, when connections are first needed, and keeps the result for the application's lifetime, like the file. It leaves out a discovered connection whose name is not valid, whose provider is not registered, whose source or reference is blank, or that repeats the name or the source and reference of an earlier one. Then:
  - **With `connections.json`**, the declared connections win: they come first, in file order, and a discovered connection whose name is declared, or whose provider, source and reference are those of a declared one, is left out; the others follow. The file's default stays the default.
  - **Without the file**, a provider that discovered connections has them instead of its implicit one, which would only repeat the harness's own default account; a provider that discovered none keeps its implicit connection. The first connection is the default.
  - **A rejected file** still makes every connection unusable, discovered ones included: never a silent fallback.
- **Origin.** `DeclaredConnection.Origin` in the catalog says where each connection comes from: `Declared` in `connections.json`, `Discovered` on this machine, or `Implicit`. The settings page shows it.

#### Connections file

`connections.json` in the data folder, read once, when first needed.

```json
{
  "default": "work",
  "connections": [
    { "name": "work", "provider": "claude-code", "credential": { "source": "login" } },
    { "name": "personal", "provider": "claude-code", "credential": { "source": "login", "reference": "/home/ana/.avala-logins/personal" } },
    { "name": "team-api", "provider": "claude-code", "credential": { "source": "apiKey", "reference": "TEAM_ANTHROPIC_KEY" }, "settings": { "model": "opus" } }
  ]
}
```

- `connections` is required and holds at least one connection. `name` is required: 1 to 64 ASCII letters, digits, `-`, `_` and `.`, starting with a letter or a digit, unique in the file. `provider` is the identifier of a provider plugin and is required. `credential` is optional; without it the provider uses its own default configuration. Its `source` is required and its `reference` optional, but never blank. `settings` is optional, an object of strings or booleans the provider interprets, a boolean reaching it as `true` or `false`; it is not for secrets.
- `default` is optional and names a declared connection; without it the first declared connection is the default.
- Secrets are never stored in the file, only references to them: a folder or the name of an environment variable.
- The file is parsed strictly: unknown fields, duplicate fields, values of the wrong type, nesting deeper than the format needs and files over 64 KiB are rejected. Whether a provider is installed and a source registered is checked when a connection is used, so a file may declare connections for a plugin that is not loaded.

| `ConnectionError` | Cause |
| --- | --- |
| `Unreadable` | The file exists but cannot be read |
| `TooLarge` | Over 64 KiB |
| `Malformed` | Not JSON, a value of the wrong type, a duplicate field or nesting too deep |
| `UnknownField` | A field the format does not define |
| `InvalidName` | A connection without a valid name |
| `DuplicateName` | Two connections with the same name |
| `MissingProvider` | A connection without a provider |
| `MissingSource` | A credential without a source |
| `UnknownDefault` | A `default` that names no declared connection |
| `NoConnections` | No connection declared, or no provider registered without a file |
| `UnknownConnection` | Not a file error: a name that no connection has |
| `UnknownProvider`, `UnknownSource` | Not file errors: a connection whose provider or credential source is not registered |
| `MissingReference`, `MissingVariable`, `MissingFolder` | Not file errors: a credential that cannot be resolved |

#### The folders

| Folder | Holds | Layer |
| --- | --- | --- |
| `Connections` | `ConnectionRegistry`, which implements `IConnections`, merges the declared, discovered and implicit connections and resolves a connection for `SessionStarter`; the declarations and their name rule; and the `IConnectionFile` port | Application |
| `ConnectionFiles` | `ConnectionFileReader` behind `IConnectionFile`, and `ConnectionFileParser` | Infrastructure |
| `Credentials` | `LoginFolderSource` and `ApiKeySource`, the first `ICredentialSource` implementations | Infrastructure |

### Turn integrity

**Accepted**

The `Turn` aggregate applies every incoming event and returns a `Result`:

- Every started item completes, and no item starts twice or progresses before it starts or after it ends.
- Events from another session or turn are rejected, and nothing is accepted after the turn ends.
- When a turn ends with items still open, the turn closes them as `Abandoned` before forwarding the end, so the stream stays consistent for every consumer.
- An item silent for longer than the allowed patience expires. An item waiting for permission or for the answer to its form never expires: that is human time.
- A turn waits for one human at a time: a form or a permission requested while another waits is rejected, and an answer only resolves the form that waits.
- Time is passed in by the caller, so the domain stays pure and tests never wait.

The generated [turn lifecycle diagram](../diagrams/turn-lifecycle.md) shows the states.

### Conformance kit

**Accepted**

Every provider plugin must pass the same check: start a session, send a turn and audit every event through the `Turn` aggregate, allowing every permission the turn requests and filling every form it asks with its recommended or first options. It reports items left open, rejected events, a missing `TurnStarted`, turns that never end and turns that end other than `Finished`, since nothing in the check fails or interrupts them; a replay that diverges ends its turn `Failed`, so the kit reports it too. When the session was asked to `AskEveryTime`, it also reports every file edit and command that goes ahead, by progressing or succeeding, without having asked permission first. A scripted provider and the simulator exercise the kit today: every well-behaved scenario of the simulator passes in `AskEveryTime`, the mode Agents uses, and its `left-open` and `hang` scenarios are reported, which proves the kit and the simulator against each other. The kit also runs on every committed [regression recording](#regression-fixtures), replayed by the simulator, so a recorded session of a real provider is checked by the same kit as the provider itself, each permission answered as it was recorded. The Claude Code provider passes it twice: through its agnostic recordings replayed by the simulator, and itself, run against its recorded protocol transcripts by a fake `claude`, see [the Claude Code provider](claude-code.md#recording-and-replay). The kit stays with the Agents tests, since it audits through the `Turn` aggregate, internal to Agents; a provider's checks therefore live there too, and start the provider through its plugin's public entry.

Every addition to the provider contract arrives with a check of the kit, the simulator implementing it and, through the simulator, a host simulation test:

| Check | Requires |
| --- | --- |
| Every turn | A resume token only from a provider that declares `CanResume`; a canvas only from a session that was given a canvas tool, and only in a media type that tool's schema offers in its `mediaType` enum; a form only from a provider that declares `AsksQuestions`, and only well formed, since the `Turn` aggregate rejects the others; an account that does not change during the session |
| `CheckFormsAsync` | A provider that declares `AsksQuestions`, given an instruction that asks, asks a form, refuses an answer to an item that has no open form, accepts the answer to its form, and refuses a second answer to it once the turn ended. A provider that does not declare it runs the turn check, which forbids forms |
| `CheckDenialAsync` | Every permission is denied with a message, the "no, do this instead" answer: the turn still conforms, a permission was asked, and no denied item progresses or succeeds afterwards |
| `CheckResumeAsync` | A provider that declares `CanResume` issues a token during the turn, and a new session started with it is accepted and runs a conforming turn. A provider that does not declare it only runs the turn check, which forbids tokens |
| `CheckCanvasToolAsync` | A provider that declares `AcceptsTools`, given a canvas tool that offers SVG and Markdown and an instruction that draws, reports the call as a canvas that completes, in an offered media type: a canvas in any other type is reported, which the simulator's `unoffered-canvas` scenario proves. A provider that does not declare it is given no tool, and runs the turn check |
| `CheckProcessesAsync` | A provider given a launcher and an instruction that runs a process starts it through the session's launcher, so the process belongs to the session's tree. The kit's launcher records what it starts and kills it afterwards, so the check leaks nothing |
| `CheckHarnessToolAsync` | A provider that declares `AcceptsTools`, given an executed tool and an instruction that calls it, reports the call as `ToolCalled`, refuses a result for an item with no pending call, accepts the kit's result and reports that same result with `ToolReturned`. A provider that does not declare it is given no tool and runs the turn check, which reports a call of a tool the session was not given |
| `CheckParallelToolCallsAsync` | A provider that declares `AcceptsTools`, given an executed tool and an instruction that calls it twice at once, holds both calls pending: the kit answers neither until the second is reported, then answers the second before the first, and each result is reported with `ToolReturned` for its own call. A provider that does not declare it is given no tool and runs the turn check |
| `CheckConnectionsAsync` | Two sessions of the same provider, started with the environments of two different [connections](#connections), stay isolated: each runs a conforming turn, they share no session, no account when both report one, and no resume token, and a token issued on the first connection is not accepted by a session started on the second |
| `CheckDiscoveryAsync` | A provider's [discovery](#discovery) names only that provider, gives valid names, never the same name or the same credential twice, and references only: an absolute folder for `login`, an environment variable's name for `apiKey`, a non-blank reference otherwise; and two discoveries in a row find the same connections |

### Claude Code

**Accepted**

`Avala.ClaudeCode` is the first real provider, `claude-code`, described with its sources in [the Claude Code provider](claude-code.md). It runs the `claude` CLI in its streaming JSON mode through the session's launcher and only translates: stream events into items, `TodoWrite` into the plan, results into usage with cost, rate-limit events into limits, the session id into resume tokens. Every acting tool reaches Avala's policy: a `PreToolUse` hook answers `ask`, which sends the call to the permission prompt tool of Avala's MCP server, served inside the session over the CLI's own control channel, so no port or second program exists. `AskUserQuestion` and plan approval become forms, and the harness tools are served by the same server. The account comes from the connection only: its configuration folder or its key, every inherited credential removed. The session loads the user's configuration of that folder alone, its `CLAUDE.md`, skills, plugins and MCP servers, whose processes join the session's tree, while the user's permission rules and modes never answer for Avala and the user's hooks stay off; the connection settings `userConfiguration` and `userHooks` switch both, see [the user's configuration](claude-code.md#the-users-configuration). It declares every capability and discovers the machine's logins, one connection per configuration folder.

| Folder | Holds | Layer |
| --- | --- | --- |
| `Protocol` | The pure translations: tools into kinds, titles and targets, questions and plans into forms and back, usage, cost and limits, and the resume token | Application |
| `Conversations` | `ClaudeCodeProvider`, `ClaudeCodeSession`, the single reader of the CLI's output and the harness's inputs, `Conversation` with its `StreamTranslator` and `ControlDesk`, `AvalaServer`, the MCP server, the command line, and the `ICli` and `IConfigurationFolders` ports | Application |
| `Cli` | `ProcessCli`, the CLI started through the session's launcher, and `TranscriptTap` | Infrastructure |
| `Folders` | `ConfigurationFolders`: the account of a login and whether it holds a conversation | Infrastructure |
| `Discovery` | `LoginFolders`, the `IConnectionDiscovery` of the machine's configuration folders | Infrastructure |

### Simulator

**Accepted**

`Avala.Simulator` is a provider plugin that plays a Claude Code session without a model, so the harness runs end to end for demos and for catching bugs without spending tokens. It depends only on `Agents.Contracts` and the SDK, the same contracts a real provider uses, and registers itself as an `IAgentProvider`.

- Scenarios are declarative data in its domain, the `Scenarios` folder: one ordered script per turn, made of reasoning, message deltas, file edits, commands with output, permission requests, plan updates, usage with cost, a usage limit, streamed canvases and the end of the turn. A session advances to the next script with every turn, so a scenario can change its behavior after feedback.
- The first message of a session chooses the scenario with a tag such as `[simulate: fix-after-feedback]`. Without a tag, or with an unknown name, the scenario is `reply`. Later messages never change it. `[replay: <recording>]` and `[replay as recorded: <recording>]` choose a [recorded session](#replay) instead.
- File edits write real files into the session's working directory through a port of `Playback`, implemented in `FileSystem`, which creates missing folders.
- It honors the permission mode like Claude Code: in `AskEveryTime` every file edit and every command asks first, naming the file's full path or the command line, and an edit is written only once allowed; in `AllowEdits` edits go ahead and only the commands a scenario marks as asking do ask; in `AllowAll` nothing asks. A denied edit or command is cancelled and the turn finishes; a denial with a message is answered first with a reply that repeats it, `Understood, I will not go on: <message>`, so a test sees the message reach the agent.
- **Forms.** A scenario step asks a form as declarative data and waits for `AnswerAsync`; an answer for any other item is `NoPendingForm`. It then reports the answer, closes the item, cancelled when declined, and replies with what it goes on with, such as `Going with Database: PostgreSQL`. A declined form, or a confirmation left unconfirmed, ends the turn there, like a denial.
- Events can be spaced by a delay measured with `TimeProvider`. It is zero by default and in tests; the plugin entry uses a short pace for in-app demos, and a constructor overload takes another.
- It declares every capability, `AsksQuestions` included, and an interruption ends the running turn as `Interrupted` before `InterruptAsync` returns, like an agent that acknowledges the interruption once it has stopped, so the next turn can start at once. Returning first would let a message sent right after a hold find the interrupted turn still in progress and fail with `TurnInProgress`.
- **Resume.** A session's conversation is its scenario and the number of turns it played. Every turn issues, right after `TurnStarted`, a resume token that encodes both with the conversation's identifier and a fingerprint of the session's account, so a token survives a restart of the application without any storage. A session started with it on the same account continues the same conversation with its next script; a token it never issued, or one issued on another account, is rejected with `CannotResume`, as a harness rejects a conversation its configuration folder does not hold.
- **Canvas tool.** Given a tool whose surface is `Canvas`, a canvas of a scenario is a call of that tool, reported as the canvas events the contract defines. Without one, the simulator writes the same content as a message, like an agent that has no canvas.
- **Executed tools.** A scenario step calls a tool by name; given a tool of that name on the `Executed` surface, the simulator reports `ToolCalled`, waits for `ReturnAsync`, refusing a result for any other item with `NoPendingCall`, then reports `ToolReturned`, closes the item and replies with the result. A step may call several tools at once, as an agent issues parallel calls: it reports every `ToolCalled`, then each `ToolReturned` and completion in the order the results arrive, and replies with all of them. Without the tool it says in a message what it would have called. The replay of a recorded tool call is not supported yet: the recorder writes calls, results and `return` entries, and the simulator's reader ignores them.
- **Account.** The simulated equivalent of a login: a session reports the account of its connection's credential. A configuration folder is the account `simulated-login:<folder>`, labelled `Simulated account (<folder name>)`; an API key is `simulated-key:<fingerprint>`, labelled `Simulated API key`, and never shows the key; a connection without a credential reports the fixed account `simulated-account`, labelled `Simulated account`. Two connections of the simulator therefore run on distinct accounts.
- **Simulated logins.** The simulator discovers its accounts like a harness that keeps one configuration folder per login: every folder under `simulated-logins` in the data folder is a connection named `simulator-<folder>`, its characters outside the name rule replaced by `-`, with the `login` credential referencing the folder's absolute path, in name order. Each runs on its own account, `simulated-login:<folder>`. Without the folder it discovers nothing, and the implicit `simulator` connection stays.
- **A recording on its own connection.** A simulator connection whose settings hold `replay`, such as `"settings": { "replay": "edit-allowed" }`, replays that recording in every session it opens, from the first message and whatever it says, and its sessions report the recorded account, so a recorded account's usage stays apart from the simulator's own.
- `tests/Avala.Host.Tests` plays its scenarios inside the application composed from the published plugin folder, with its in-app pace, and observes the jobs and the canvas snapshots through the event feed.

| Scenario | Behavior |
| --- | --- |
| `reply` | Reasoning and a streamed reply |
| `edit` | A plan, a file written into the working directory, a test command and a reply |
| `fix-after-feedback` | The first turn writes a file marked `BROKEN`; the turn after feedback rewrites it fixed |
| `rewrite-checks` | Like `fix-after-feedback`, but the first turn also empties `.avala/checks.json`, an agent trying to loosen the rules that judge it |
| `permission` | Asks permission for a command and waits for `RespondAsync`. Allowed, it runs the command and goes on; denied, it cancels the command and finishes the turn |
| `repeated-permission` | Asks twice for the same command, `dotnet ef database update`, so a "don't ask again" answer to the first request decides the second |
| `outside-edit` | Runs `dotnet build`, then edits `../avala-outside-note.txt`, a file outside its worktree |
| `question` | Asks which database to use: one single-choice field, PostgreSQL recommended, SQLite, free text accepted; then replies with the choice and finishes |
| `plan-approval` | Asks to approve a two-step plan with a confirmation field that accepts a comment; approved, it writes `PLAN.md` and finishes; not approved, it stops |
| `crash` | The event stream throws in the middle of the turn; a session that resumes the conversation finishes the next turn |
| `left-open` | Starts an item and finishes the turn without closing it |
| `hang` | `TurnStarted` and its resume token, then nothing until interrupted; the next turn, in the same session or one that resumes it, replies and finishes |
| `canvas` | Draws two SVG diagrams and Markdown notes through the canvas tool, in chunks: only offered media types |
| `unoffered-canvas` | Draws a Mermaid canvas, a media type the canvas tool does not offer, so the conformance kit reports it and the application shows it as source |
| `processes` | Runs `dotnet build`, a real process that works briefly and exits, then `dotnet run`, a real server that listens on the port in `AVALA_PORT` and replies with it, and finishes the turn leaving the server running past the session |
| `follow-up` | Writes `CHANGELOG.md`, then calls the executed tool `propose_follow_up` with the follow-up `[simulate: reply] Announce the changelog to the team`, waits for the harness's result, replies with it as `The harness answered: …` and finishes. Without that tool it writes the proposal as a message |
| `near-limit` | Replies, reports its usage and a `5h` limit window 95% used that resets two seconds after the report, measured with `TimeProvider`, and finishes |
| `spent-window` | Reports 0.06 USD of usage and a `5h` limit window 95% used that resets an hour later, then keeps working until interrupted, so a hold at the limit always finds the job running; the turn after replies and finishes |
| `delegate` | An orchestrator: calls the executed tool `delegate` twice at once, `[simulate: notes] Write the release notes` and `[simulate: todo] Write the to-do list`, waits for both results, replies and finishes |
| `delegate-conflict` | Like `delegate`, with the children `notes` and `notes-revised`, which both write `NOTES.md` |
| `delegate-loosen` | Calls `delegate` for `notes` asking for `autonomous`, then again without asking, one after the other |
| `delegate-expensive` | Calls `delegate` once for `expensive` |
| `recursive` | Calls `delegate` with `[simulate: recursive]`, so each child delegates again, until the harness refuses |
| `notes`, `notes-revised`, `todo` | Children: each writes its file, `NOTES.md` or `TODO.md`, replies and finishes |
| `expensive` | A child: writes `INDEX.md` and reports 0.60 USD of usage, then waits until interrupted; the turn after replies and finishes |

The processes of the `processes` scenario are the `Avala.Simulator.Workload` program, shipped beside the simulator in its plugin folder and started with the `dotnet` host through the session's launcher, so they run the same on Linux, Windows and macOS without a shell. It works (`work`), prints an environment variable (`env`), listens on `AVALA_PORT` (`serve`), starts a detached child and exits (`spawn`) or waits (`hold`); every waiting mode ends on its own once the process that started the harness is gone, or after ten minutes, so a test that fails never leaves one behind. The tests of the runtime and the conformance kit use the same program.

Every scenario that reaches its end reports usage with cost and a usage limit, so observability can be exercised.

### Session recording and replay

**Accepted**

A session of any provider can be recorded once and replayed by the simulator as often as needed. When the real Claude Code adapter arrives, its sessions become simulator scenarios and regression tests, so the simulator converges on reality and every quirk of the adapter is pinned by a test.

#### Recording

- **A plugin of its own.** The Recording module implements `IAgentProviderDecorator`: every session Agents starts goes through it, whatever the provider. It depends only on `Agents.Contracts` and the SDK, so it records what the harness sees, the agnostic level, never a provider's protocol.
- **Off by default.** It records only when `recording.json` in the data folder says so, read once, parsed strictly like the other settings files: unknown fields, duplicate fields, values of the wrong type and files over 16 KiB are rejected, and a rejected file records nothing and logs why.

```json
{ "enabled": true, "redact": ["ana@example.com", "sk-ant-..."] }
```

- **What it records.** The session's provider, capabilities and account; its permission mode, whether it was asked to resume and the name and surface of its tools; every agnostic event in the order the harness received it; every input the harness sent: user turns, permission decisions with their message, form answers, interruptions, each with the error the provider refused it with, if it did; how the event stream ended, closed or crashed; and the stop of the session by the harness. Every entry carries its time since the session started, in milliseconds, measured with `TimeProvider`.
- **Edited files.** The agnostic events do not carry what an edit wrote, so the recorder reads it: when an edit item whose permission request named its file succeeds, the content of that file, up to 1 MiB of text, is recorded before the item's completion, with its path relative to the working directory. Every session opens in `AskEveryTime`, so every edit names its file. A deleted file, a binary file or an edit that never asked is not captured.
- **What it never records.** The working directory, replaced everywhere by `${workingDirectory}`; the resume token passed to the provider; the descriptions and schemas of the tools; the session's connection environment, its configuration folder, key and settings; environment variables, credentials and anything else the agnostic contract does not carry. A crash is recorded without its exception, whose message could hold anything.
- **Redaction.** Every text of the recording, titles, targets, messages, answers, file contents, account labels and tokens included, has each string of `redact` replaced by `[redacted]`. Redaction is literal, so it is only as good as the list; a recording is meant to be read before it is shared.
- **Files.** One file per session, `recordings/<yyyyMMddTHHmmssZ>-<session>.json` under the data folder. The decorator never writes: it hands each entry to `RecordingFiles`, the single reader of a channel, which owns every open recording and rewrites the session's file, through a temporary file it then moves, when a turn completes, when the stream ends, when the session stops and when the application shuts down. Recording never delays the agent beyond reading an edited file.

#### Format

The format is `avala-recording`, version 1. A reader refuses another version; within a version, fields may be added, and readers ignore the fields and entry kinds they do not know.

```json
{
  "format": "avala-recording",
  "version": 1,
  "recordedAt": "2026-10-09T08:30:00+00:00",
  "provider": { "id": "claude-code", "name": "Claude Code" },
  "capabilities": { "streamsPartialOutput": true, "exposesReasoning": true, "canInterrupt": true, "canResume": true, "acceptsTools": true, "reportsUsage": true, "reportsCost": true, "reportsLimits": true, "asksQuestions": true },
  "account": { "id": "account-1", "label": "[redacted]" },
  "options": { "permissions": "askEveryTime", "resumed": false, "tools": [ { "name": "canvas", "surface": "canvas" } ] },
  "entries": [
    { "at": 0, "send": { "text": "Add a greeting" } },
    { "at": 40, "event": { "type": "turnStarted", "turn": 1 } },
    { "at": 90, "event": { "type": "permissionRequested", "turn": 1, "item": "edit", "title": "Edit GREETING.md", "kind": "fileEdit", "target": "${workingDirectory}/GREETING.md" } },
    { "at": 95, "respond": { "item": "edit", "answer": "allow" } },
    { "at": 96, "event": { "type": "permissionResolved", "turn": 1, "item": "edit", "answer": "allow" } },
    { "at": 120, "file": { "item": "edit", "path": "GREETING.md", "content": "# Hello\n" } },
    { "at": 121, "event": { "type": "itemCompleted", "turn": 1, "item": "edit", "outcome": "succeeded" } },
    { "at": 130, "interrupt": {}, "refused": "noTurnInProgress" },
    { "at": 200, "stop": {} }
  ]
}
```

- `account` is absent when the provider reports none. Enumerations are written in camel case, such as `askEveryTime` or `fileEdit`.
- Each entry has `at` and exactly one of: `event`, an agnostic event; `file`, the content an edit left; `send`, `respond`, `answer`, `return`, a tool's result with its `item`, `content` and `isError`, and `interrupt`, the harness's inputs, with `refused` when the provider returned an `AgentError`; `end`, with `crashed`; `stop`.
- An event has a `type`, the camel-cased name of its record such as `itemProgressed`, and `turn`, the turn's number in the session from 1, instead of the session and turn identifiers. Its other fields are the record's own, with the same nesting: `form`, `answer`, `steps`, `tokens`, `cost`, `limit`. An item is its identifier as text.

#### Replay

- **Converted, not a second format.** The simulator's declarative scenarios stay C# data in its domain: they are written by hand, at the level of intentions such as "write this file" and "run this command", and they adapt to the permission mode and the tools. A recording is a different thing, a transcript at the level of events, so it is not a scenario format; the simulator converts it into a scenario of replay steps when it is chosen, and the same session plays both. Recording files are the only format on disk.
- **Chosen by name.** `[replay: edit-allowed]` in the first message replays `recordings/edit-allowed.json` of the data folder, the folder the recorder writes to. A name is letters, digits, `-`, `_` and `.`, not starting with a dot, so a tag never reaches outside the folder.
- **Turns.** Each recorded `turnStarted` starts a turn of the scenario, and the session's `SendAsync` plays them in order; what the user turn says is not compared, since the harness writes feedback with durations and other details that change. Every event is replayed as the session's own, with its session and turn, `${workingDirectory}` replaced by the replaying session's working directory, and the replay's own resume token in place of the recorded one: a session resumed with it continues the recording at its next turn. A `file` entry writes its content through the simulator's `IFileWriter` at the point it was recorded. A recorded crash makes the stream fail, a recorded end closes it.
- **Inputs.** A recorded permission request or form waits, like the real agent, for the harness's answer, and the answer must be the recorded one: the same `Allow` or `Deny` and message, or the same form answer field by field. An answer the provider refused at the time is not expected again. A recorded interruption waits to be interrupted.
- **Divergence.** When the harness does something the recording did not, the replay says so instead of going on: an item titled `Replay diverged` reports what was expected and what came, such as `Replay diverged: the recording answered the permission for edit with Allow, but the harness answered Deny "Not now".`, the turn ends `Failed` and the session's stream closes, so the job fails and nothing silently continues. Divergences are a different answer, an answer to something the recording never answered, an interruption the recording never made, a turn beyond the recorded ones, a session opened in another permission mode than the recorded one, and a recording that cannot be read: absent, malformed or of another version.
- **Timing.** `[replay: …]` compresses the recording, played at the simulator's pace; `[replay as recorded: …]` waits the recorded time between entries, measured with `TimeProvider`.
- **Provider identity.** The replay runs inside the simulator, so the session reports the simulator's provider and capabilities, not the recorded ones, which the file keeps for reference. A recording replayed on [its own connection](#simulator) reports the recorded account, so the account is lifted; the provider and the capabilities are not, because they belong to a provider, not to a session or a connection, in the contract: reporting another provider's identity would make the simulator impersonate it in every aggregate by provider. A recording whose provider lacked a capability the harness then relies on, such as interruption, diverges and says so.

#### Regression fixtures

`tests/recordings` holds recordings committed as tests. Each `<name>.json` comes with `<name>.expected.json`:

```json
{
  "record": "[simulate: edit] Greet the team",
  "repository": { ".avala/permissions.json": "{ \"rules\": [] }" },
  "journey": ["Running", "Checking", "AwaitingReview"],
  "permissions": ["edits-inside-the-workspace FileEdit GREETING.md Allow Answered"],
  "forms": [],
  "verifications": ["1 NoChecksDeclared"],
  "files": { "GREETING.md": "# Hello\n" }
}
```

- `repository` is committed to the job's repository before it runs. The expectations are the job's journey from `Running`, each permission decision of its audit as `rule kind target answer delivery`, each form decision as `delivery autonomy` and its assumptions, each verification as `attempt outcome`, and the content the worktree's files must end with.
- `RecordedSessionTests` in the host tests replays every committed recording in the real application with the `[replay: <name>]` tag and checks its expectations. When the fixture has `record`, the instruction of a simulator scenario, it also runs that scenario with recording on, checks the expectations, replays the new recording and checks that the replay reaches the same outcome. `AVALA_UPDATE_RECORDINGS=1` writes the new recordings over the committed ones.
- The conformance kit runs on every committed recording replayed by the simulator.
- A recording of a real provider is added with its expectations and no `record`: the round trip is skipped for it, since the tests cannot run that provider.
- The fixtures are `edit`, `fix-after-feedback`, two turns with a failed then a passed verification, and `question-autonomous`, a form answered by the policy, recorded from the simulator; and `claude-code-edit`, a command and a canvas, `claude-code-question`, an `AskUserQuestion` answered by the autonomous policy and a `Write`, and `claude-code-denial`, a command a repository rule denies, recorded from real Claude Code jobs.

## Canvas

**Accepted**

The harness can paint charts, diagrams, screens and designs while the agent writes them, for every provider.

- A canvas is an item of the turn: `CanvasStarted` opens it with its media type, `ItemProgressed` streams its content and `ItemCompleted` closes it. It inherits every integrity rule of items.
- The harness offers the canvas to every agent that accepts tools as a [harness tool](#harness-tools): the Canvas module registers its definition, `canvas` with a `title`, a `mediaType` and the `content`, on the `Canvas` surface, in its `Drawing` folder.
- **Avala offers, harnesses adapt.** The tool is built from what Avala can draw. Every renderer plugin declares the media types it draws as `CanvasFormat` services from its core registration, with no UI framework; the Rendering plugin declares SVG and Markdown. The schema's `mediaType` is an `enum` of exactly the declared types, and the description lists each one with what to draw in it, SVG for every diagram, and says any other type is not drawn. A harness plugin hands that tool to its agent as it is; it never widens it to what its agent happens to produce. A renderer plugin added later, such as Mermaid, appears in the offer automatically, see [canvas rendering](canvas-rendering.md#the-offer).
-  Agents passes it to providers without knowing Canvas, and each adapter reports the tool's calls as canvas events. Providers that stream partial output deliver the canvas in chunks; the others deliver it at once. The real adapter transports the tool through MCP; the simulator's `canvas` scenario draws through it today.
- The Canvas module accumulates each canvas, throttles updates and publishes snapshots. The shared canvas surface shows them, keeps each snapshot as a version and draws it through the renderer a plugin registered for its media type, see [canvas rendering](canvas-rendering.md).
- Canvas content is untrusted: it renders in an isolated surface with no network access by default. The SVG renderer draws only sanitized markup, Markdown loads no image and opens no link, and a canvas in a type that is not offered, such as Mermaid or HTML, is never rendered: it shows its source, see [canvas rendering](canvas-rendering.md#a-canvas-that-was-not-offered).

### Canvas module

**Accepted**

The module subscribes to `AgentActivity` with an `IHandle<T>`, like every other consumer of agent events, so it receives only events the `Turn` aggregate has already accepted. It works for every provider without knowing any.

| Folder | Holds | Layer |
| --- | --- | --- |
| `Canvases` | The `CanvasDocument` aggregate, `CanvasLifecycle`, the error enum and the domain events | Domain |
| `Drawing` | `CanvasOffer`, the media types every renderer plugin declared, and `CanvasTool`, the definition of the canvas tool the module builds from it and offers to agents as a `HarnessTool` | Application |
| `Gallery` | The documents of every session, changed only from the feed's mailbox, and the `ICanvases` query, which reads an immutable list of their snapshots the gallery replaces on every change | Application |
| `Streaming` | `CanvasFeed`, the handler that applies canvas events to the gallery | Application |
| `Throttling` | `SnapshotThrottle`, which decides when a snapshot is published. Its cadences belong to a `SerialExecutor`: the feed's changes and the flushes its timers schedule run there one at a time | Application |

- A `CanvasDocument` is identified by its `CanvasId`, the turn and the item that carry it. It opens from `CanvasStarted`, which needs a media type; a media type the offer does not hold opens the document as source only, with its rejection `NotOffered`, which the feed logs, and its snapshots say it was not offered, so the content still reaches the person as source. It appends every `ItemProgressed` chunk in arrival order and closes on `ItemCompleted` in the state of its outcome: completed, failed, cancelled, abandoned or expired. It rejects content and completions of another item with `ForeignItem`, and anything after it closed with `AlreadyClosed`. The gallery rejects a canvas that starts twice with `AlreadyOpen`, and reports content for an item that never started as a canvas as `UnknownCanvas`, which is how the feed ignores messages, reasoning and tools.
- The generated [canvas lifecycle diagram](../diagrams/canvas-lifecycle.md) shows the states: `Streaming`, then one of the closed states inside the superstate `Closed`.
- `CanvasUpdated` carries a `CanvasSnapshot`: the canvas, its session, title, media type, the full content so far, its status and `IsOffered`, false for a canvas in a media type the tool did not offer. A snapshot holds the full content rather than a delta, so a consumer that misses one loses nothing.
- Snapshots are throttled per canvas with `TimeProvider`: a change publishes at once when the last snapshot of that canvas is older than the interval, 100 ms in the plugin; otherwise one flush is scheduled for the end of the interval and carries every change made meanwhile. A canvas therefore gets at most one streaming snapshot per interval, its start is visible at once and a pause never hides content. Completion cancels the pending flush and publishes the final snapshot at once, whatever the interval. A flush that comes due after the canvas closed publishes nothing, so no streaming snapshot follows the final one.
- `ICanvases.InSession` returns the current snapshot of every canvas of a session in start order, for view models that open after the canvases started.
- The canvases live in memory for the life of the application. They are not persisted, since the agent can redraw them; dropping a session's canvases when it closes arrives with the view models that decide when a session is no longer shown.

## Observability

**Accepted**

Everything the harnesses process goes through observability: tokens, cost, usage limits, durations and outcomes.

- The agnostic events carry the raw facts, so observability works the same for every provider.
- An Observability module subscribes to the events on the bus and aggregates them by provider, account, connection, session and job. The account is the one the provider reports when the session opens, see [Accounts](#accounts), and the connection the one the session opened on, see [Connections](#connections).
- It publishes metrics through `System.Diagnostics.Metrics`, the .NET standard that OpenTelemetry collects, and later feeds view models for the in-app dashboards.

### Correlation

Agent events know only their session. Two integration events tie a session to the rest:

| Event | Published by | Carries |
| --- | --- | --- |
| `SessionOpened` | `AgentSessions`, before it pumps the session's events | `SessionId`, `ProviderInfo`, the working directory, the `ConnectionName` it opened on and the `Option<AgentAccount>` of the session |
| `JobSessionStarted` | `JobLauncher`, after storing the job and before sending the instruction, at launch and at recovery | `JobId`, `SessionId` |

Each handler receives its events in publishing order, so both arrive at the tracker before the first activity of the session. Observability does not rely on it: it keeps everything per session and groups sessions by provider and job only when queried, so a late correlation still lands in the right aggregate.

### The module

| Folder | Holds | Layer |
| --- | --- | --- |
| `Usage` | `SessionUsage`, an immutable record of one session: provider, account, job, tokens, cost per currency, unpriced reports, turns by outcome with their duration, and the latest reading of each limit window. The arithmetic on tokens and turns, and the rollup of several sessions into a summary | Domain |
| `Tracking` | `UsageTracker`, the handler of `SessionOpened`, `JobSessionStarted` and `AgentActivity`; `UsageBook`, the book that implements `IUsage` and restores earlier runs at startup; `UsageHistory`, behind `IUsageHistory`; and the `IUsageMetrics` and `IUsageStore` ports | Application |
| `Metrics` | `UsageMeter`, the `Avala.Observability` meter behind `IUsageMetrics` | Infrastructure |
| `Storage` | `SqliteUsageStore` behind `IUsageStore`, in `observability.db` | Infrastructure |

- The domain is a projection of facts that already happened, so it has no aggregate and nothing to reject: records that return their next version, no error enum.
- Each `UsageReported` adds to the totals. A report without a cost adds its tokens and counts as unpriced, so a dashboard can tell a partial cost from a complete one. Costs add up per currency.
- A turn lasts from its `TurnStarted` to its `TurnCompleted`, measured with `TimeProvider` when the tracker receives each event. A turn counts once: a repeated start or end changes nothing.
- A limit belongs to the account a connection runs on, not to a session: each window keeps its latest reading, and `ByConnection` keeps the limits of two connections of one provider apart.
- `IUsage` in `Avala.Observability.Contracts` answers by provider, by account, by connection, by session and by job, with a `UsageSummary`: tokens, costs, unpriced reports, a `TurnTally` and limits. A job adds up every session it ran, recovery included.
- An account belongs to its provider: `ByAccount` groups sessions by provider and account, so two providers that use the same identifier stay apart, and leaves out sessions whose provider reported no account. The metrics carry no account tag, to keep their cardinality bounded.
- `ByConnection` groups sessions by the connection they opened on, with its provider, ordered by connection name, and leaves out sessions that were never announced.
- After it records a `UsageReported` or a `LimitReported`, the tracker publishes `UsageRecorded` with the session and its job. A consumer that reacts to spending, such as Budgets, handles it and reads `IUsage`, which already includes the report. Handling `AgentActivity` directly would not do: handlers run concurrently, so such a consumer could read the aggregates before the tracker applied the report.
- The tracker is the only writer of `UsageBook`. The book holds an immutable dictionary of sessions that the tracker replaces on every change, so `IUsage` answers from a consistent snapshot on any thread.
- **Stored history.** Every usage report, limit reading and counted turn is also a `UsageFact`: its session, the time the tracker handled it, and its tokens and cost, its limit, or its outcome and duration. The tracker keeps each session's attribution, provider, account, connection and job, and each fact in `observability.db` before it publishes `UsageRecorded`, through the store's single `SerialExecutor`. The book holds two snapshots: the sessions of earlier runs, which it restores once, as a startup task, by folding the stored facts, and the live sessions of this run, which only the tracker writes. Session identifiers are new in every run, so the two never overlap; the store remembers the sessions it wrote itself and leaves them out of the restore, so a startup task that runs late cannot count a live session twice. `IUsage` therefore answers across restarts: a recovered job's spending goes on from where it was, and a connection's limit windows keep their latest reading.
- **Time windows.** `IUsageHistory` reads the stored facts: `WithinAsync(from, to)` adds up the facts from `from`, included, to `to`, excluded, into a `UsagePeriod` with its summary by provider and by connection; `DailyAsync(first, last, zone)` gives one period per calendar day of the time zone, from local midnight to local midnight. A turn counts in the window it ended in, and a limit window keeps its latest reading within the period.
- The history is kept whole; compacting old facts arrives when its size calls for it.

| Instrument | Kind | Unit | Tags |
| --- | --- | --- | --- |
| `avala.agent.tokens` | Counter | `{token}` | `avala.provider`, `avala.connection`, `avala.token.type`: `input`, `output`, `cache_read`, `cache_write`, `reasoning` |
| `avala.agent.cost` | Counter | `{currency}` | `avala.provider`, `avala.connection`, `avala.currency` |
| `avala.agent.turns` | Counter | `{turn}` | `avala.provider`, `avala.connection`, `avala.turn.outcome` |
| `avala.agent.turn.duration` | Histogram | `s` | `avala.provider`, `avala.connection`, `avala.turn.outcome` |
| `avala.agent.limit.used` | Gauge | `1` | `avala.provider`, `avala.connection`, `avala.limit.window` |

The provider tag is the provider's identifier and the connection tag the connection's name; each is left out when the session never announced it. Connection names are few and declared by the user, so the tag keeps the cardinality bounded, unlike an account.

## Rules from the base commit

**Accepted**

A repository declares the rules of its jobs in three files: its checks in `.avala/checks.json`, its permission policy in `.avala/permissions.json` and its budget in `.avala/budget.json`. A fourth, `.avala/jobs.json`, names the connection its jobs prefer and the strategy that delivers an approved job, read the same way by Jobs, see [the job flow coordinator](#job-flow-coordinator) and [review](#review-and-approval), and in its `autopilot` section whether a job may be approved automatically and whether follow-ups are accepted, read the same way by [Autopilot](#autopilot), and in its `delegation` section whether and how its jobs delegate, read the same way by [Delegation](#delegation). The agent works in the job's worktree, so a rule read from the worktree is a rule the agent can rewrite. Every rule is therefore read from the job's base commit, the commit its worktree was created from, and nothing the agent changes during the job can loosen the rules that judge it.

```csharp
public interface IBaseFiles
{
    ValueTask<Result<BaseFile, WorkspaceFailure>> ReadAsync(string worktree, string path, CancellationToken cancellationToken);
}

public sealed record FileOrigin(string Commit, bool EditedInWorktree);

public sealed record BaseFile(string Path, FileOrigin Origin, Option<string> Content);
```

- **One mechanism.** Workspaces resolves the base reference to a commit when it prepares a workspace, creates the worktree at that exact commit, stores it with the workspace and reports it as `WorkspaceInfo.BaseCommit`. `IBaseFiles` reads a file of that commit for the workspace whose worktree is the given folder. Verification, Permissions and Budgets read their file through it, from the working directory they already know: the `CompletedAttempt` of a gate and the `SessionOpened` of a session.
- **Git, not the file system.** The file is read with `git ls-tree` and `git cat-file` at the stored commit, with replace objects ignored, so neither the worktree, nor a checkpoint, nor a later commit to the repository, nor a `git replace` changes what it returns. A path the commit does not hold has no content; a folder at that path counts as no file.
- **Edits are ignored and reported.** `EditedInWorktree` says whether the worktree's copy differs from the committed one, compared by git object identity so line ending conversions do not count; a file the worktree added or deleted differs too. The rule stays the committed one, and each module surfaces the origin in its evidence: the verification report, the session's policy and the session's budget carry the commit and the flag, and the feedback of a failed verification tells the agent its changes to the checks do not apply to this job. An edit of a rule file is still an ordinary edit for review: approving the job is how a rule changes.
- **Outside a workspace.** A folder that is no job's worktree has no base commit: `UnknownWorkspace`. Permissions and Budgets then apply no repository file, as if it were absent, so a session outside a job gets the built-in policy and no caps; Verification, which only ever judges a job, fails closed. A git failure is `GitFailed`, which each module treats as an unreadable file.
- **Recovery.** A recovered session opens in the same worktree and reads the same base commit, so a restart cannot pick up a file the agent edited before it.
- **The rules commit.** A workspace reads its rule files from its rules commit, `WorkspaceInfo.RulesCommit`, which is its base commit unless `WorkspaceRequest.Rules` named another when it was prepared. A [child job](#delegation) starts from its parent's checkpoint, which may hold rule files its parent's agent edited, so Jobs prepares it with its parent's rules commit: every job of a delegation tree is judged by the rules of the commit its root started from, and an orchestrator cannot loosen its children's checks, policy, budget or delegation rules by editing them. `EditedInWorktree` compares with the rules commit too, so a child that inherited such an edit reports it. The diff of a workspace stays against its base commit, the work it did.
- **Before a job exists.** `IBaseFiles.ReadCurrentAsync(repository, path)` reads a file of the commit the repository's `HEAD` points at, the base the next job starts from, for any folder inside the repository, with the same commands; `EditedInWorktree` then says whether the checkout's copy differs. Autopilot reads the [backlog](#job-sources) this way each time it takes a task, so an uncommitted edit of the backlog is ignored like an agent's edit of a rule. A folder outside any repository is `NotAGitRepository`.
- **A repository's current rules.** The settings page shows what a repository declares before any job runs, so each module that owns a rule file answers for its own format: `IRepositoryPolicies` of Permissions, `IRepositoryBudgets` of Budgets and `IRepositoryChecks` of Verification, each with `OfRepositoryAsync(repository)`, read the file through `ReadCurrentAsync` with the parser the jobs use, and report its status, its error and its `FileOrigin`. The policy lists its rules in decision order without session rules, the guards and defaults included; the budget lists its top-level caps and those of each connection; the checks list each check's name, command line and timeout. A folder outside a repository is an unreadable file without origin. `.avala/jobs.json` belongs to three modules, so the Workbench reads it raw through `IBaseFiles` and shows each top-level section as written.

## Verification

**Accepted**

A job's completion depends on evidence, not on the agent's word. The Verification module is a plugin that registers an `ICompletionGate`: after every finished turn, once Jobs has checkpointed the worktree, it runs the checks the repository declares inside the job's worktree and turns their results into the gate's verdict. Jobs knows only the gate contract, never the module.

### Declaring checks

A repository declares its checks in `.avala/checks.json`, at its root. The declaration is read from the job's [base commit](#rules-from-the-base-commit), never from the worktree:

```json
{
  "checks": [
    { "name": "build", "command": "dotnet", "arguments": ["build"] },
    { "name": "tests", "command": "dotnet", "arguments": ["test", "--no-build"], "timeoutSeconds": 900 }
  ]
}
```

- `command` is a program found on the `PATH` and `arguments` its arguments, one per item. No shell runs them, so quoting, pipes and globs mean nothing, and the same declaration works on Linux, macOS and Windows. A check that needs a shell names it as its command.
- `name` is optional and defaults to the command line. `arguments` is optional. `timeoutSeconds` is optional, defaults to 600 and must be greater than 0 and at most 86,400.
- Comments and trailing commas are tolerated.
- The checks run in declaration order, in the worktree, through `IProcessRunner`.

### Rules

- **No declaration.** A base commit without `.avala/checks.json`, or with an empty `checks` array, passes, and the evidence says so: the report's outcome is `NoChecksDeclared`. A repository is never verified silently.
- **Invalid declaration.** A file that is not a JSON object with a `checks` array of objects, a check without a command, or a timeout out of range fails closed: nothing runs, the outcome is `InvalidDeclaration` and the verdict is `Retry` with feedback naming the file and the problem, and saying that the worktree cannot fix it since the job is verified against its base commit. The attempt budget runs out and the job asks for help. A declaration that cannot be read from the base commit, because git failed or the folder is no workspace, is `InvalidDeclaration` too, with the error `UnreadableDeclaration` and no origin.
- **Failure.** A check fails when it exits with a code other than 0, when its command is not found, or when it outlasts its timeout. The checks after the first failure are not run and are recorded as `Skipped`, since a broken build makes the tests meaningless.
- **Timeout.** Each check runs with a cancellation token that fires after its timeout, measured with `TimeProvider`. The runner kills the process tree and the check is `TimedOut`. When the job flow itself is cancelled, at shutdown, the cancellation propagates and nothing is recorded.
- **Feedback.** A failure becomes `GateVerdict.Retry` with feedback naming the check, its command line, its exit code or the reason it stopped, its duration and the tails of its output and error streams. Jobs sends it back to the same agent session through the existing retry path, and asks for help once the attempt budget is spent. Verification adds no second path.
- **Evidence.** Every evaluation, whatever its outcome, produces one `VerificationReport`, kept in memory and published as `AttemptVerified` before the gate returns. Output tails keep the last 4,000 characters of each stream, prefixed by `[...]` when cut.
- **Trust.** The declaration comes from the base commit, so an agent that edits it, even to empty it, is still judged by the committed checks. The report carries the commit the declaration came from and whether the worktree's copy differs, and a failure's feedback adds that the agent's changes to the declaration do not apply to this job. The report also lists every command that ran, which the reviewer sees.

### The module

| Folder | Holds | Layer |
| --- | --- | --- |
| `Checks` | `DeclaredCheck`, the parsing of the declaration with `JsonDocument`, the evidence of a check and its bounded tails, and `VerificationError` | Domain |
| `Verifying` | `ChecksGate`, the `ICompletionGate`, which reads the declaration through `IBaseFiles` from Workspaces; `CheckRunner`, which runs one check with its timeout; and `AgentFeedback`, which writes the verdict | Application |
| `Evidence` | `EvidenceBook`, the in-memory book behind `IVerifications`, and `EvidenceLedger`, which keeps a report and publishes it | Application |

- The domain has no aggregate: it parses a declaration and describes facts. `VerificationError` is its single error enum, for the declaration it can reject: `MalformedDeclaration`, `MissingCommand`, `InvalidTimeout` and `UnreadableDeclaration`.
- The reports live in memory and start empty with the application. Storing them arrives with the job's evidence, together with the decisions of Permissions, see [its module](#the-module-2).

## Permissions

**Accepted**

Agents ask before they act. For agents to work unattended, the harness must answer those requests itself, and for its answers to be trusted, it must say why it gave each one. The Permissions module answers through an explicit policy and records every decision, including the ones it leaves to a human.

### Policy

- A policy is an ordered list of rules. Each rule may name an item kind, a target pattern and a scope, and gives one answer: `Allow`, `Deny` or `Ask`, which leaves the request to a human exactly as if no policy existed.
- The first rule that matches decides. When none matches, the answer is `Ask`: the safe default never grants anything.
- A rule matches on the agnostic facts of `PermissionRequested`: its item kind and its target. A missing kind or target matches any. No rule knows a provider.
- A target pattern matches the whole target, case-sensitively, with `*` for any run of characters, `/` included, and `?` for one character. The matcher is linear and backtracks only to the last `*`, so no pattern can make it slow.
- A rule scoped to the `workspace` matches only file edits whose path lies inside the session's working directory. The path is resolved against the working directory, so `src/../x` and an absolute path inside both count, and `../x` does not; the target an inside edit is matched and recorded with is its path relative to the working directory, with `/` separators. Other kinds have no path to scope, so a policy file that scopes them is rejected. The built-in guard for edits elsewhere uses the scope `OutsideWorkspace`, which a policy file cannot declare.
- The policy of a session is, in order: the built-in guards, the repository rules, the [session rules](#session-rules), the built-in defaults, the autonomous rules when the session is [autonomous](#autonomy-levels), then the default `Ask`. Under `autonomous`, every `Ask` the policy would give becomes `Deny`, so an unattended agent is never left waiting.

| Rule | Origin | Matches | Answer |
| --- | --- | --- | --- |
| `policy-file-goes-to-a-human` | Built-in guard | Edits of `.avala/permissions.json` inside the workspace | `Ask` |
| `edits-outside-the-workspace-go-to-a-human` | Built-in guard | Edits outside the workspace, or of a session outside any workspace | `Ask` |
| Repository rules | `.avala/permissions.json` | Whatever they declare, in file order | Their own |
| Session rules | A human's "don't ask again" | The exact kind and target that human answered | The human's answer |
| `edits-inside-the-workspace` | Built-in default | Edits inside the workspace | `Allow` |
| `autonomous-commands-in-the-worktree` | Built-in, autonomous only | Commands, which run in the worktree | `Allow` |
| `autonomous-denies-the-rest` | Built-in, autonomous only | Anything else | `Deny` |
| Default | None | Anything else | `Ask` |

The guards come first so that no repository rule can let an agent rewrite the policy file or write outside its worktree unattended; under `autonomous` their `Ask` becomes `Deny`. The policy of a job comes from its base commit, so such an edit could not loosen that job's own policy anyway; the guard keeps a human in the loop for a change that would govern every later job once approved. Agents asks for every edit, see [the contract](#contract), so the guard and the edit rules apply to every edit the agent makes. Editing inside the workspace is allowed by default because the workspace is the job's own disposable worktree, reviewed before anything is approved; a repository can still deny or ask for it with a rule of its own.

### Autonomy levels

The provider's permission mode is always `AskEveryTime`; how much an agent may do without a human is Avala's policy, at one of two levels.

| Level | Permission requests | Forms |
| --- | --- | --- |
| `supervised`, the default | Decided by the rules; the ones they leave to `Ask` wait for a human, and only that job pauses | Wait for a human |
| `autonomous` | Edits inside the worktree and commands, which run in it, are allowed; anything else, an edit outside the worktree, a web request, an MCP call, is denied instead of asked, so the agent never blocks | Answered by the policy, see [Forms](#forms) |

- **Guards at every level.** Editing the policy file, writing outside the worktree and whatever the repository denies are never allowed automatically: they come before the autonomous rules, and under `autonomous` their `Ask` becomes `Deny`.
- **Declared by the repository.** `.avala/permissions.json` declares the level, read from the job's base commit like its rules, so an agent cannot raise its own autonomy.
- **Tightened by the job, never loosened.** A job may ask for a level at submission, `JobRequest.Autonomy`, and Jobs announces it with each `JobSessionStarted`. Permissions caps the session's level at it: a job submitted as `supervised` in an `autonomous` repository runs supervised. A job that asks for more than its repository declares is refused: it runs at the repository's level and `AutonomyApplied` reports the request as `Refused`. The repository's level is only known once the workspace exists, so the refusal is recorded when the session starts, not at submission.

### Session rules

A human who answers a request through `IPermissionAnswers.AnswerAsync` may say "don't ask again". Avala's policy, not the provider, turns that answer into a session rule:

- It matches the exact item kind and target of the answered request, the target as the policy matched it, never as a pattern, and gives the human's answer, `Allow` or `Deny`. It is named `don't ask again` with the origin `Session`.
- It comes after the guards and the repository rules, so a request the repository sends to a human, or a guard keeps for one, still asks. It lasts as long as the session.
- It is added before the answer reaches the agent and removed when the agent no longer awaits it, so the agent's next identical request already meets it.
- Only a request the policy left to a human can be answered this way; any other is `NotAwaitingAnswer`. Every answer is published as `PermissionAnswered` and kept in the audit with the rule it created, if any. A `Deny` may carry a message, which reaches the agent through `PermissionDecision.Message`.

### Forms

The policy answers a [form](#human-input-forms) only when the session is `autonomous`; a supervised session leaves it to a human, who answers through `IAgents.AnswerAsync`. Either way the decision is published as `FormDecided`.

- A `Permission` form is declined with a message, since the policy cannot see what it would grant: `Nobody is watching this job, so permissions asked through a form are not granted. Stay within the worktree.`
- Any other form is answered field by field with the repository's strategy, `formAnswers` in the policy file. `recommended`, the default, takes the option the agent marked recommended. `bestJudgment` answers every field that accepts text with `Nobody is watching this job. Decide with your best judgment, state the assumption you make and go on.` Under either, a field the strategy cannot answer falls back in this order: the judgment text when the field accepts text, the recommended option, the first option. A confirmation is confirmed, so a plan is approved and carried out within the worktree's confines.
- Every automatic answer is audited with one `Assumption` per field: its identifier, its prompt, its basis (`RecommendedOption`, `FirstOption`, `AgentJudgment` or `Confirmed`) and the options chosen. They are the job's evidence of what was decided without asking, queryable through `IPermissionAudit.FormsOfJob` beside the verification reports.

### Policy file

A repository declares its rules in `.avala/permissions.json`, read when the session opens from the [base commit](#rules-from-the-base-commit) of the job whose worktree is the session's working directory. The committed file governs every job of that repository, and an edit of it in a worktree never changes the policy of that job, recovered sessions included. A session outside any job's worktree has no base commit and gets the built-in policy.

```json
{
  "autonomy": "autonomous",
  "formAnswers": "recommended",
  "rules": [
    { "name": "run the tests", "kind": "command", "target": "dotnet test*", "answer": "allow" },
    { "name": "no network", "kind": "web", "answer": "deny" },
    { "kind": "fileEdit", "within": "workspace", "target": "docs/*", "answer": "ask" }
  ]
}
```

- Every section is optional. `autonomy` is `supervised`, the default, or `autonomous`. `formAnswers` is `recommended`, the default, or `bestJudgment`. `rules` defaults to none.

- `kind` is an `ItemKind` name, case-insensitive: `message`, `reasoning`, `fileEdit`, `command`, `search`, `web`, `mcp`, `subagent` or `other`. `within` is `anywhere`, the default, or `workspace`. `answer` is `allow`, `deny` or `ask`, and is required. A rule without a `name` is named by its position, such as `rule 3`.
- The file is parsed strictly: unknown fields, duplicate fields, numbers for names, nesting deeper than the format needs and files over 64 KiB are rejected without being interpreted.
- An invalid file is reported with a `PolicyError`, and the session falls back to the built-in policy. A broken file can therefore only make the harness ask more, never allow more.

| `PolicyError` | Cause |
| --- | --- |
| `Unreadable` | The base commit cannot be read |
| `TooLarge` | Over 64 KiB |
| `Malformed` | Not JSON, a value of the wrong type, a duplicate field or nesting too deep |
| `UnknownField` | A field the format does not define |
| `UnknownKind`, `UnknownScope`, `UnknownAnswer` | A value outside its list |
| `MissingAnswer` | A rule without an answer |
| `ScopeNeedsFileEdits` | A `workspace` scope on a kind other than `fileEdit` |
| `UnknownAutonomy`, `UnknownStrategy` | An `autonomy` or a `formAnswers` outside its list |
| `NotAwaitingAnswer` | Not a file error: a human answered a request the policy did not leave to a human, or that the agent no longer awaits |

A rejected file falls back to the built-in policy, which is `supervised`.

### The module

The module subscribes to the bus like every other consumer of agent events, so it receives only permission requests and forms the `Turn` aggregate has already accepted, and answers through `IAgents.RespondAsync` and `IAgents.AnswerAsync`.

| Folder | Holds | Layer |
| --- | --- | --- |
| `Policies` | `PermissionPolicy`, the declared rules, level and strategy with the built-in rules, the cap a job puts on the level and the first-match decision; rule matching as extension members on `PolicyRule`; `FormPolicy`, the automatic answer to a form with its assumptions; `PermissionRequest`, `Verdict`, and `GovernedSession`, the immutable record of one session: working directory, policy, job, autonomy, decisions and forms | Domain |
| `Governance` | `SessionGovernor`, the handler of `SessionOpened`, `JobSessionStarted` and `AgentActivity`; `GovernanceBook`, the in-memory book that implements `IPermissionAudit`; and the `IPolicyFiles` port | Application |
| `Answering` | `PermissionResponder`, which decides a request or a form with its session's policy and answers the agent; `RequestFacts`, which locates a request against the working directory; `HumanAnswers`, behind `IPermissionAnswers`, which delivers a human's answer and keeps its session rule | Application |
| `PolicyFiles` | `PolicyFileReader` behind `IPolicyFiles`, which reads the file through `IBaseFiles` from Workspaces, and `PolicyFileParser` | Infrastructure |

- The domain decides and has nothing to reject, so it has no aggregate. The single error enum of the module is `PolicyError`, in its contracts: reading a policy file can fail, and so can a human's answer, with `NotAwaitingAnswer`.
- One handler, one mailbox. The governor handles the opening of a session, its job, its permission requests and its forms in publishing order, so a request is always decided by the policy its session's file loaded and at the level its job asked for. The governor is the only writer of the sessions in `GovernanceBook`, an immutable dictionary the audit queries read. `HumanAnswers` writes only the session rules and the human answers, each an immutable collection of its own replaced through `ImmutableInterlocked`, which the governor reads when it decides.
- On `SessionOpened` the governor reads the policy file of the base commit once, keeps the session's policy and publishes `PolicyLoaded` with the file's origin, its declared level and strategy. Edits of the file in the worktree never change the policy, of a running session or of a recovered one.
- On `JobSessionStarted` the governor caps the session's level at the one the job asked for, if any, and publishes `AutonomyApplied`. Jobs publishes it before it sends the session its first message, so the level applies to everything the agent asks.
- On `PermissionRequested` the responder decides with the session's policy and its session rules and answers `Allow` or `Deny` through `IAgents.RespondAsync`, leaving `Ask` pending; the governor keeps the decision and publishes `PermissionDecided` with the answer, the rule that decided, the level it was decided at and whether the answer reached the agent. A request from a session it never saw open is decided by the built-in policy with no workspace, so only the guards and the default apply.
- On `FormRequested` the responder answers through `IAgents.AnswerAsync` when the session is autonomous and leaves the form to a human otherwise; the governor keeps the decision and publishes `FormDecided`.
- Requests left to a human stay pending exactly as before the module existed: the turn waits in `AwaitingPermission`, never expires, and anyone may still answer through `IAgents.RespondAsync`, or through `IPermissionAnswers.AnswerAsync` to leave a message or a session rule and have the answer audited. Forms wait the same way in `AwaitingAnswer`. Without the module, every request and every form is left to a human that way.
- Decisions live in memory for the life of the application. Unlike usage and interventions, a decision is evidence of one session, read with the verification reports of the same attempts on the review screen; storing it alone would give the review half its evidence after a restart. Decisions, forms, assumptions and verification reports are stored together, as the job's evidence, in a later step.

## Supervision

**Accepted**

An unattended agent must not hang forever or die silently. The Supervision module watches every running job and holds it through `IJobs.HoldAsync`, with the facts it measured, when its agent goes silent. A session that dies is Jobs' own concern: it holds the job as `SessionLost`, see [the job flow coordinator](#job-flow-coordinator).

### Rules

- **Silence.** A job is watched while it is `Running`, from its `JobProgressed`. Every accepted event of the job's current session, the one its latest `JobSessionStarted` named, restarts the silence; events of a session the job no longer uses do not. When the job stays silent for the whole window, it is held as `Stalled` with the silence measured and the window, and Jobs interrupts the turn.
- **Human time and harness time.** While a permission request or a form of the job's session waits for an answer, or a call of a [harness tool](#harness-tools) waits for its result, the job is never silent: the watch pauses on `PermissionRequested`, `FormRequested` and `ToolCalled`, and restarts the window on `PermissionResolved`, `FormAnswered`, once the last pending call is answered by `ToolReturned` or closed by `ItemCompleted`, or at the end of the turn. An orchestrator waiting for its children is therefore not stalled; the children are watched as jobs of their own. This is the same rule as the `Turn` aggregate's expiry, where an item waiting for permission never expires. Checks running in `Checking` are not watched either: Verification bounds them with its own timeouts.
- **Why a lost session is not Supervision's.** A lost session must be held before Jobs evaluates the failed turn that follows it. With one mailbox per handler, a hold decided in Supervision would race that evaluation, and the job would sometimes fail instead of asking a human. Jobs receives both events in one mailbox and queues both on the job, so the order is guaranteed without any module having to be faster than another.
- **What it does not duplicate.** Sessions lost to a restart of the application are recovery's: stopping a session at shutdown publishes no `SessionEnded`, and `JobRecovery` resumes the job in a new session, continuing its conversation when the provider can resume it. A job held as `SessionLost` waits for a human, who continues it through `IJobs.ContinueAsync` in a new session that resumes the conversation when it can. Items left open when a turn ends are the `Turn` aggregate's: they are closed as `Abandoned`, the turn ends normally and the job goes on to its checks, so the `left-open` scenario needs no intervention.
- **Only a running job.** Jobs rejects a hold of a job that is not `Running`; the module then records nothing. An intervention exists only when a job was actually held.

### Timers

- Silence is measured with `TimeProvider`, at the time the module handles each event. One alarm per job is pending at most: it is set for the last activity plus the window, and activity in the meantime only moves the last activity.
- The watchdog's mailbox owns the watches and the pending alarms, so they need no lock. An alarm rings on a timer thread, which must not touch them: it publishes `SilenceNoticed`, and the watchdog confirms the silence when its mailbox reaches it. Every event published before the alarm rang is in that mailbox ahead of it, so an agent whose activity the watchdog has not handled yet, because a hold kept it busy, never looks silent. A confirmation that finds activity sets the alarm again from the last activity.
- A hold waits in the job's queue behind the work already queued on that job, such as the evaluation of a turn, and the watchdog's mailbox waits with it. A job whose turn is being evaluated is `Checking` and not watched, so this only delays the next confirmations; it never makes a job look silent.

### Settings

The harness reads `supervision.json` from its data folder, the folder of `AVALA_DATA_PATH`, once, when the module first needs it.

```json
{ "silenceSeconds": 900 }
```

- `silenceSeconds` is optional, a number greater than 0 and at most 86,400. The default is 15 minutes: generous enough for a long build or test command that reports nothing until it ends, short enough that a hung agent is noticed within the hour.
- The file is parsed strictly, like the policy file: unknown fields, duplicate fields, values of the wrong type and files over 16 KiB are rejected. A rejected file keeps the default window and is reported through `ISupervision.SettingsAsync` with its `SupervisionError`, so a broken file never disables supervision.
- `ISupervision.ChangeSilenceAsync(silence)` is how the settings page changes the window: it checks the range the parser checks, writes the file through a temporary file moved into place, one write at a time through a `SerialExecutor`, and replaces the settings the module holds, so the watchdog uses the new window from the next alarm it sets, for a job already running too, without a restart.

| `SupervisionError` | Cause |
| --- | --- |
| `Unreadable` | The file exists but cannot be read |
| `TooLarge` | Over 16 KiB |
| `Malformed` | Not JSON, not an object, a value of the wrong type or a duplicate field |
| `UnknownField` | A field the format does not define |
| `InvalidSilence` | A window not greater than 0 or over a day |
| `Unwritable` | A changed window could not be written to the file |

### The module

| Folder | Holds | Layer |
| --- | --- | --- |
| `Watching` | `JobWatch`, the immutable record of one job: whether it runs, its current session, the permission it waits for and its last activity; it decides whether the job is armed, when its alarm is due and whether it is silent | Domain |
| `Supervising` | `Watchdog`, the handler of `JobProgressed`, `JobSessionStarted`, `AgentActivity` and `SilenceNoticed`, which keeps the watch of every job; `SilenceAlarms`, the per-job timers; `Intervener`, which holds through `IJobs` and records; `SupervisionBook`, the book behind `ISupervision`, which restores the interventions of earlier runs at startup; and the `ISupervisionSettings` and `IInterventionStore` ports | Application |
| `Settings` | `SettingsFile` behind `ISupervisionSettings`, and `SettingsParser` | Infrastructure |
| `Storage` | `SqliteInterventionStore` behind `IInterventionStore`, in `supervision.db` | Infrastructure |

- The domain decides and rejects nothing, so it has no aggregate. `SupervisionError` is the module's single error enum, in its contracts, since only reading the settings can fail and its outcome is public.
- Every intervention is stored in `supervision.db` before `SupervisorIntervened` is published, and the book restores those of earlier runs as a startup task, leaving out what this run stored, so `ISupervision.OfJob` answers across restarts: earlier runs first, then this run, in the order they happened.

## Budgets

**Accepted**

An unattended agent must not spend without limit. The Budgets module holds a job through `IJobs.HoldAsync` when it reaches a cap its repository declares, and records what it spent against what it was allowed.

### Caps

- Caps are per job: they count every session of the job, recovery included, as `IUsage.OfJob` adds them up. The module reads spending from `IUsage` instead of adding reports up again.
- **Cost.** One cap per currency. A job is held as `BudgetExceeded` when what it spent in a currency reaches the cap of that currency. Only priced reports count: with a provider that reports no cost, cap tokens instead.
- **Tokens.** One cap on every token the provider reported: input, output, cache reads, cache writes and reasoning. Counting all of them holds earlier rather than later.
- **Limits.** A threshold between 0 and 1. A job is held as `LimitNearlyReached` when a usage limit window of its session's connection reaches the threshold, whichever session on that connection reported it, since a limit belongs to the account a connection runs on: a work subscription near its limit never holds a job on a personal one. A reading whose window has already reset, its `ResetsAt` past, no longer counts, so a job continued after the reset is not held again by the reading that held it. Only a running job is held: a reading that arrives with the end of a turn may find the job already checking or awaiting review, since Jobs and Budgets handle the turn's events in their own mailboxes, and then nothing is interrupted, as nothing runs; the job is held when it runs again while the window still counts, and the loop's own pause before its next task covers the autopilot.
- **By connection.** The `connections` section of the file gives the jobs on a named connection their own caps, which replace the top-level ones for those jobs, so an API key billed per token can be capped while a subscription is not. The loader reads the caps of the connection `SessionOpened` names.
- **Memory.** A cap in megabytes on the memory, the working sets, of every process of the job's trees, as `IResources.OfJob` measures it at the latest sample. A job is held as `MemoryExceeded` when its processes reach it, so parallel builds cannot exhaust the machine. Without the Resources plugin nothing is measured and the cap never holds.
- A cap is reached when the measure is equal to it or above it. The first breach found holds the job, in that order: cost, tokens, limit, memory.
- **What a job has committed.** Cost and tokens are measured as what the job has committed, not only what it spent: its own spending plus, for each of its [child jobs](#delegation), what the child reserves. A child that is still open reserves the larger of its carve and what it has committed itself, per currency and for tokens; a child that ended, approved, discarded or failed, reserves only what it committed. A job without children has committed exactly what it spent.

### Carves

Delegating must not multiply spending: a child's cost and token caps are carved out of its parent's.

- **When.** On `JobSubmitted` with a parent, the enforcer carves the child's allowance as soon as the parent's allowance is known, records it in `budgets.db` and publishes `BudgetCarved`. The parent's allowance is known once the enforcer has tied the parent to its session, the budget of that session has been loaded, and the parent, if it is itself a child, has its own carve. Until then the child waits in the enforcer's mailbox, among the children still to carve in submission order, and each `JobSessionStarted`, `BudgetLoaded` and new carve carves every waiting child whose parent became known, siblings in the order they were submitted.
- **Why it waits.** The loader and the enforcer are separate handlers with no order between them, so the child's `JobSubmitted` can reach the enforcer before the `BudgetLoaded` of its parent's session: the parent's session was opened, but the loader has not kept its budget yet. Carving then would carve from caps the enforcer does not know, as if the parent were unlimited, and the child would keep that empty carve for good. Waiting makes the order explicit in the one mailbox that owns the carves: a carve is computed only from a parent budget the loader kept, and `BudgetLoaded`, published after the loader kept it, always arrives behind the child to carve it. Delegation does not wait for the budget before submitting, since that would make it know about Budgets, and the enforcer does not read the budget file itself, since the loader is the only reader of budget files.
- **Meanwhile.** A child waiting for its carve is enforced against its own caps, which its carve can only narrow, so it is never unbounded and any hold it gets meanwhile is one its carve would give too. Once carved, the child and its ancestors are checked again against everything they have committed, so a child that spent past its carve while it waited is held at once. A child that waits for its carve also has no allowance to carve from for its own children, who wait behind it.
- **How much.** The parent's allowance is its own caps, those of its session's budget file and connection, narrowed by its own carve if it is itself a child. For each cost cap and for the token cap of that allowance, the child receives `share × max(0, cap − committed)`, where `committed` is what the parent has committed just before this child, its earlier children's reservations included, and `share` is the parent's `carvePerChild`, 0.5 by default. Tokens are rounded down to a whole number. A parent without caps carves nothing, and its child is capped by its own budget file only. With the default share, a parent capped at 1.00 USD that spent 0.20 USD carves 0.40 USD for its first child and 0.20 USD for a second, and keeps 0.20 USD for itself.
- **Enforced on the child.** A child's caps are its own, narrowed per currency and for tokens by its carve: the smaller of the two. Reaching them holds the child as `BudgetExceeded`, like any job.
- **Enforced on the parent.** The parent's caps are measured against what it has committed, its running children's reservations included, so the parent and its children together can never be allowed more than the parent's cap. A usage report of a child is also checked against each of its ancestors, so a child that overshoots its carve with one report, as any job can overshoot a cap by one report, holds its ancestors once the tree reaches their cap.
- **Restarts.** Carves are stored in `budgets.db` and restored at startup with the interventions. The status of the jobs is not: a child that ended in an earlier run counts as open, reserving at least its carve, the conservative side, until a later `JobProgressed` says otherwise. The children still waiting for their carve live in the enforcer's memory only, as they did between `JobSubmitted` and the carve before: a child submitted just before the application stopped and never carved is capped by its own budget file only.

### When it checks

- When `UsageRecorded` names a job, after Observability recorded a usage or limit report; when `ResourcesSampled` says Resources kept a new sample, for every running job; when `JobSessionStarted` ties a session to its job; when `BudgetLoaded` says the budget of a session tied to a job was read; when a child is [carved](#carves), for the child and its ancestors; and whenever `JobProgressed` says a job runs again, so a job already over its budget is held as soon as a retry, a hint or a recovery starts it, before it spends more.
- The loader and the enforcer are separate handlers, so the enforcer may learn that a job runs before the budget file of its session was read. It then has nothing to enforce yet, and `BudgetLoaded`, published once the loader kept the budget, makes it check again. The enforcer keeps which session each job runs in and the status of each job in its own mailbox; the loader is the only writer of the budgets in `BudgetBook`, which holds them in an immutable dictionary.
- Only a `Running` job is checked. A hold interrupts the turn, so an agent that reports usage as it goes is stopped mid-turn; one that reports only at the end of its turns can overshoot by one turn.

### Budget file

A repository declares its caps in `.avala/budget.json`, read when the session opens from the job's [base commit](#rules-from-the-base-commit), like the policy file. Without the file, or for a session outside any job's worktree, nothing is capped: the built-in default has no caps and no limit threshold.

```json
{
  "costPerJob": { "USD": 5.00 },
  "tokensPerJob": 2000000,
  "holdAtLimit": 0.9,
  "memoryPerJobMegabytes": 4096,
  "carvePerChild": 0.5,
  "connections": {
    "team-api": { "costPerJob": { "USD": 2.00 } }
  }
}
```

- Every field is optional. `costPerJob` maps a currency, as the provider reports it, to an amount greater than 0. `tokensPerJob` is a whole number greater than 0. `holdAtLimit` is greater than 0 and at most 1. `memoryPerJobMegabytes` is a whole number greater than 0. `carvePerChild` is the share of what a job has left that each of its children is [carved](#carves), greater than 0 and less than 1, so a parent always keeps part of its allowance; 0.5 without it.
- `connections` maps a [connection](#connections) name to caps of the same five fields, which replace the top-level caps for the jobs on that connection; a connection it does not name gets the top-level caps. A section names a connection of the machine that runs the job, so a name no connection has caps nothing. A blank name is `Malformed`, and a section is validated like the top level, without a `connections` of its own.
- The file is parsed strictly: unknown fields, duplicate fields, values of the wrong type, nesting deeper than the format needs and files over 64 KiB are rejected.
- **Invalid file, safe behavior.** A rejected file is reported in `BudgetLoaded` with its `BudgetError`, and the job is held as `InvalidBudget` as soon as it runs. A repository that declared caps meant to limit spending, so a broken declaration stops the agent instead of letting it spend without limit.

| `BudgetError` | Cause |
| --- | --- |
| `Unreadable` | The base commit cannot be read |
| `TooLarge` | Over 64 KiB |
| `Malformed` | Not JSON, a value of the wrong type, a duplicate field or nesting too deep |
| `UnknownField` | A field the format does not define |
| `InvalidCost` | A currency without a name or an amount not greater than 0 |
| `InvalidTokens` | A token cap that is not a whole number greater than 0 |
| `InvalidThreshold` | A threshold not greater than 0 or over 1 |
| `InvalidMemory` | A memory cap that is not a whole number greater than 0 |
| `InvalidCarve` | A `carvePerChild` not greater than 0 or not less than 1 |
| `InvalidRunningJobs` | Not a file error of the repository: a limit of running jobs in `budgets.json` that is not a whole number greater than 0 |

### Running jobs

The machine decides how many jobs run at once, never a repository: `budgets.json` in the data folder, read once, when first needed.

```json
{ "runningJobs": 2 }
```

- `runningJobs` is optional, a whole number greater than 0. Without the file, or without the field, any number of jobs runs at once.
- The file is parsed strictly, like the other settings files: unknown fields, duplicate fields, values of the wrong type and files over 16 KiB are rejected. A rejected file runs one job at a time, the limit that cannot exhaust a machine, and is reported through `IBudgets.MachineAsync` with its `BudgetError`, so a broken file never lifts the limit its author meant to set.
- `RunningJobs` is the `IJobAdmission` of Budgets. A job holds a slot from its admission until it leaves `Preparing`, `Running` and `Checking`: it ends, waits for review or a human. A submitted job beyond the limit waits, published as `JobQueued` with the jobs running and the limit, in its `Preparing` state and with no workspace yet, until a slot frees, published as `JobAdmitted`; waiting jobs start in submission order. A waiting job that is discarded meanwhile is let through without a slot, and its launch finds it ended.
- A job continued by a human or recovered at startup takes a slot again when it runs, even beyond the limit: the limit governs launches.
- The slots and the queue are owned by a `SerialExecutor`; the waiting launch awaits a one-shot signal.

### Choosing a connection by capacity

Avala always uses the connection with capacity unless something names one. The policy is agnostic, an extension point of Jobs that Budgets implements, since Budgets owns the threshold that would hold a job:

```csharp
public interface IConnectionSelector
{
    ValueTask<Option<ConnectionChoice>> ChooseAsync(ConnectionQuestion question, CancellationToken cancellationToken);
}

public sealed record ConnectionQuestion(string Worktree, IReadOnlyList<ConnectionName> Candidates);
public sealed record CandidateCapacity(ConnectionName Connection, double Used, Option<UsageLimit> Window, double Threshold, bool Available);
public sealed record ConnectionChoice(ConnectionName Connection, ChoiceReason Reason, IReadOnlyList<CandidateCapacity> Compared, DateTimeOffset At);
```

- **Readings.** For each candidate, `CapacitySelector` takes the latest limit readings of that connection from `IUsage.ByConnection`, leaves out a window whose `ResetsAt` is past, and keeps the most used of the others as `Used` and `Window`. A connection without readings counts as unused, `Used` 0: it is available.
- **Threshold.** The hold threshold of the job's worktree for that connection: `holdAtLimit` of its `connections` section, or the top-level one, from the [budget file](#budget-file) at the base commit, the same that would [hold](#caps) the job. Without one, the threshold is 1, a spent window. A candidate is `Available` when `Used` is below its threshold.
- **Choice.** Among the available candidates, the least used, which is the one with the most remaining capacity; a tie goes to the candidate listed first, the catalog's order, or the order a delegation section lists. The reason is `MostCapacity`. When none is available, the least used of all is chosen as `AllAtLimit`, so the budget holds the job there rather than any silent move; a loop pauses before it gets there, see [Autopilot](#the-loop).
- The choice is the data a view needs: which connection, why, and the readings compared, each candidate's used fraction, window and threshold. Jobs publishes it with the job as `ConnectionChosen`; Delegation keeps it on the child's `DelegationRecord.Choice`.
- Without the Budgets plugin no selector is registered, and a job that names nothing opens on the machine's default connection.

### The module

| Folder | Holds | Layer |
| --- | --- | --- |
| `Capacity` | `CapacityPolicy`: a candidate's capacity from its readings and threshold, and the choice among candidates | Domain |
| `Routing` | `CapacitySelector`, the `IConnectionSelector` that reads the thresholds through `IBudgetFiles` and the readings from `IUsage` | Application |
| `Caps` | `Breaches`: the evaluation of a session's budget against what a job has committed, its provider's limits and its memory, the reason each breach holds a job for, and the built-in caps; `Carves`: a job's allowance within its carve, the carve of a new child, `Commitment` and `Lineage`, what a job and its children have committed | Domain |
| `Enforcement` | `BudgetLoader`, the handler of `SessionOpened`; `BudgetEnforcer`, the handler of `BudgetLoaded`, `JobSubmitted`, `JobSessionStarted`, `JobProgressed`, `UsageRecorded` and `ResourcesSampled`; `BudgetActions`, which holds through `IJobs` and records, and records and announces carves; `BudgetBook`, the book behind `IBudgets`, which restores the interventions and carves of earlier runs at startup; and the `IBudgetFiles` and `IInterventionStore` ports | Application |
| `Admission` | `RunningJobs`, the `IJobAdmission` and handler of `JobProgressed` that keeps the slots, and the `IMachineBudgetFile` port | Application |
| `BudgetFiles` | `BudgetFileReader` behind `IBudgetFiles`, which reads the file through `IBaseFiles` from Workspaces, `BudgetFileParser`, and `MachineBudgetFile` behind `IMachineBudgetFile` | Infrastructure |
| `Storage` | `SqliteInterventionStore` behind `IInterventionStore`, the interventions and carves in `budgets.db` | Infrastructure |

- The domain decides and rejects nothing, so it has no aggregate. `BudgetError` is the module's single error enum, in its contracts.
- Session budgets live in memory: a recovered session reads its file again. Interventions are stored in `budgets.db` before `BudgetIntervened` is published and restored at startup like Supervision's, and so are carves before `BudgetCarved`. Spending comes from Observability, which stores it, so a job recovered after a restart goes on spending against what it had already spent.
- **Trust.** The budget file comes from the base commit, so an agent cannot raise its own caps: neither a running session nor a recovered one, which reads the same commit again, sees an edit made in the worktree. `BudgetLoaded` carries the file's origin, with whether the worktree's copy differs.
- Budgets measures memory through `IEnumerable<IResources>`, so it works without the Resources plugin.

## Resources

**Accepted**

Agents and the commands they run leave resources behind: processes that outlive their session, such as test hosts and build servers, ports two worktrees fight over, and worktrees that pile up on disk. The Resources module accounts for every resource the jobs use and reclaims what they leave, on top of the [process trees](#process-trees) the runtime contains.

### Sampling and attribution

- `ResourceSampler`, a startup task, samples on a `PeriodicTimer` driven by `TimeProvider`, every `sampleSeconds`: the members of every open tree with their working set and CPU time, the listening ports of those members, and, every `diskSeconds`, the size of every worktree a session ran in and of the data folder. Each sample is one `ResourcesSampled`, so the event is throttled to one per interval whatever happens. `SampleTaker` keeps the sample in `ResourceBook` before it publishes it, so a handler of the event, such as Budgets, reads it through `IResources`.
- **Attribution.** `ResourceTracker` ties a tree to its session, connection and provider from `SessionOpened.ProcessTree`, and the session to its job from `JobSessionStarted`. Each tree of a sample is attributed when it is taken; a worktree to the job that ran in it.
- **CPU load** is the CPU time a tree's processes used between two samples, over the time between them, in cores; a process new to the tree counts from its next sample. A process that ends between two samples takes its CPU time with it.
- `IResources` answers from the latest sample: `Global`, all trees and the data folder; `OfJob` and `OfSession`, their trees and their worktree; `ByConnection` and `ByProvider`, the trees of their sessions and the worktrees those sessions ran in. Memory is the sum of working sets, so memory shared between processes counts once per process.

### Orphans

- When a session ends, on `SessionEnded` or `SessionStopped`, the processes still alive in its tree are orphans: the agent's process is gone, so whatever remains, a test host, a build server, a dev server, was left behind. `OrphanReaper` reports them with their session, job, memory and ports as `OrphansFound`.
- **Policy.** `orphans` in `resources.json`: `kill`, the default, closes the tree, killing every member, and reports `Killed` with the processes that survived, normally none; `report` leaves them running, reported `LeftRunning`, until `IOrphans.ReapAsync(job)` kills them and publishes `OrphansReaped`. A tree that holds no process is closed without a report. Every report is kept in the audit, by job.
- The reaper runs in the tracker's mailbox, which also handles the end of the job afterwards: Jobs stops an ended job's session before it announces the end, so the orphans are reaped before the worktree is reclaimed.

### Port leases

- Every worktree leases a block of `perWorktree` ports from the range `first` to `last` when its first tree opens, through `IProcessEnvironment`, skipping blocks in which a port is already listened on. A second session in the same worktree gets the same lease. The lease reaches every process of the worktree's trees as `AVALA_PORT`, the first port, and `AVALA_PORTS`, the block as `first-last`, so a dev server, a test database or a second checkout never collide: the agent's harness passes them to the agent like any environment variable, and the agent uses them when it starts a service. A worktree that finds no free block gets no variables, and the shortage is logged.
- A lease is published as `PortsLeased` and released, as `PortsReleased`, when the job of the worktree ends.
- **Collisions.** Every sample compares the listening sockets of the machine with the leases: a leased port listened on by a process outside the worktree's trees, another worktree's or a process the harness does not know, is a `PortConflict`, published once per port, lease and process as `PortConflictObserved` and kept by `IResources.Conflicts`.

### Worktrees

- **Retention.** When a job ends, its worktrees are kept for the time its status is retained, then removed through `IWorkspaces.RemoveAsync`, which deletes the worktree and its branch, and published as `WorktreeReclaimed`. By default a discarded job's worktree is reclaimed at once, a failed job's after a week, and an approved job's is kept, since its branch is what gets merged. Retention is checked when a job ends and at every sample. Before a worktree is removed, the orphans its job left running under the `report` policy are reaped, published as `OrphansReaped`, so no process outlives the folder it runs in; a removal that fails, such as a folder Windows still holds open, is tried again at the next sample.
- **Reconciliation.** `IWorkspaces.ReconcileAsync` compares the worktree root with the store: folders under it that no workspace knows are strays, workspaces whose folder is gone are missing. `IWorkspaces.CleanAsync` cleans only what a reconciliation reported and is still the case: it deletes stray folders and forgets missing workspaces after `git worktree prune`, keeping their branches, which hold the work. At startup the housekeeper reconciles and publishes `WorktreesReconciled`; with `reconcile` set to `clean` it cleans too. `IWorktreeHousekeeping.CleanAsync` is the command.

### Settings

`resources.json` in the data folder, read once, when first needed.

```json
{
  "sampleSeconds": 5,
  "diskSeconds": 60,
  "orphans": "kill",
  "ports": { "first": 24000, "last": 24999, "perWorktree": 10 },
  "worktrees": { "keepDiscardedHours": 0, "keepFailedHours": 168, "keepApprovedHours": null, "reconcile": "report" }
}
```

- Every field is optional and the example shows the defaults. `sampleSeconds` and `diskSeconds` are numbers from 0.1 to 86,400. `orphans` is `kill` or `report`. `ports` holds whole numbers: `first` from 1024, `last` at most 65,535 and not below `first`, `perWorktree` from 1 to the size of the range; the default range sits below the ephemeral ports of Linux, Windows and macOS. Each `keep…Hours` is a number from 0 to 87,600, or `null` to keep the worktree. `reconcile` is `report` or `clean`.
- The file is parsed strictly: unknown fields, duplicate fields, values of the wrong type, nesting deeper than the format needs and files over 16 KiB are rejected. A rejected file keeps every default and is reported through `IResources.SettingsAsync` with its `ResourceError`.

| `ResourceError` | Cause |
| --- | --- |
| `Unreadable` | The file exists but cannot be read |
| `TooLarge` | Over 16 KiB |
| `Malformed` | Not JSON, not an object, a value of the wrong type or a duplicate field |
| `UnknownField` | A field the format does not define |
| `InvalidInterval` | A sampling interval out of its range |
| `InvalidPorts` | A port range out of its bounds or a block larger than the range |
| `InvalidRetention` | A retention out of its range |
| `UnknownPolicy` | An `orphans` or `reconcile` outside its list |
| `NothingToReap` | Not a file error: `IOrphans.ReapAsync` found no orphan left running for the job |

### The module

| Folder | Holds | Layer |
| --- | --- | --- |
| `Usage` | `Tallies`: the rollup of trees into a `ResourceUsage`, the CPU load between two samples, and the retention of each final status | Domain |
| `Leases` | `PortBook`: the leases of the range, leasing, releasing, the conflicts of the listening sockets with the leases, and the environment variables of a lease | Domain |
| `Tracking` | `ResourceTracker`, the handler of `SessionOpened`, `JobSessionStarted`, `SessionEnded`, `SessionStopped`, `JobProgressed` and `ResourcesSampled`; `Attribution`, the correlation of trees, sessions and jobs; `ResourceBook`, behind `IResources`; and the `IResourceSettings` and `IFolderSizes` ports | Application |
| `Sampling` | `ResourceSampler`, the timer; `SampleTaker`, which takes, keeps and publishes a sample and its conflicts; and `ProcessReadings`, which reads trees and their ports through `IProcessTrees` and `IListeningPorts` | Application |
| `Reaping` | `OrphanReaper`, behind `IOrphans` | Application |
| `Leasing` | `PortLeases`, the `IProcessEnvironment` that leases and releases, owning its book through a `SerialExecutor` | Application |
| `Housekeeping` | `WorktreeHousekeeper`, behind `IWorktreeHousekeeping`, the startup reconciliation and the retention of ended jobs' worktrees | Application |
| `Settings` | `ResourceSettingsFile` behind `IResourceSettings`, and `ResourceSettingsParser` | Infrastructure |
| `Disks` | `FolderSizes` behind `IFolderSizes` | Infrastructure |

- The domain is a projection and a ledger of leases; it rejects nothing, so it has no aggregate. `ResourceError` is the module's single error enum, in its contracts.
- The tracker is the only writer of the attribution; the sampler's loop is the only writer of the samples and the conflicts it reports; queries read immutable snapshots. Samples, orphans, leases and reclaimed worktrees live in memory, and so does the retention due of an ended job: a restart forgets it, and reconciliation reports what was left.

## Autopilot

**Accepted**

A loop takes a repository's next task, runs it as a job, approves it on its own when the evidence is clean, and takes the next, all night if need be, with the guarantees the other modules already give a job plus three of its own: approval without a person only on clean evidence, sources of tasks, and circuit breakers. The Autopilot module is a plugin like any other: it submits, approves and continues jobs through `IJobs`, reads the evidence through the contracts of Verification, Permissions, Jobs and Workspaces, the spending through Observability's, and no module knows it.

### Clean evidence

A job of a loop that reaches `AwaitingReview` is approved automatically, through `IJobs.ApproveAsync` and so by the repository's own [approval strategy](#review-and-approval), only when its evidence has no exception. Otherwise it stays `AwaitingReview` for a person, and the loop moves on. The exceptions, each an `ExceptionReason`, read from data the harness already keeps:

| Exception | When |
| --- | --- |
| `NotDeclared` | The `autopilot` section of `.avala/jobs.json` at the job's base commit does not say `"approve": "cleanEvidence"`. Without the file or the section nothing is approved automatically |
| `UnreadableRules` | That file cannot be read from the base commit or its `autopilot` section is invalid |
| `VerificationNotPassed` | The job's last `VerificationReport` in `IVerifications.OfJob` is not `Passed`: failed, `NoChecksDeclared`, `InvalidDeclaration`, or there is none. A repository without checks has no evidence that the work is right, so it is never approved alone. An earlier attempt that failed and was fixed by a retry does not count, only the last verification does |
| `Denial` | Any `PolicyDecision` of the job in `IPermissionAudit.OfJob` answered `Deny`, any human answer in `AnswersOfJob` that denied, or any form in `FormsOfJob` that was declined |
| `Assumption` | Any `FormDecision` of the job that carries an `Assumption`: the policy decided something for the agent that nobody confirmed |
| `RuleFileEdited` | The job's diff, `IWorkspaceChanges.DiffAsync`, exactly what approval would deliver, touches a file under `.avala/`; or the last verification's declaration, or the policy of any of the job's sessions in `IPermissionAudit.PolicyOf`, reports `EditedInWorktree` |
| `Held` | Any attempt of the job in `IJobCatalog.HistoryAsync` has the origin `Hint`. A job held for any reason, or that ran out of retries, waits in `NeedsHelp`, and only a continuation, which starts a `Hint` attempt, brings it back to review; so `Hint` marks every hold, whoever continued it, the loop itself after a limit window included |
| `ChangesUnknown` | The job's diff cannot be read, so the rule files cannot be proven untouched |
| `DeliveryRefused` | The evidence was clean but the strategy refused the delivery, such as `MergeConflict` or `BaseCheckoutDirty`; the refusal is kept with it |

The exceptions of the run itself, every one above but `NotDeclared`, `UnreadableRules` and `DeliveryRefused`, are the same for any job, whether or not a loop took it, so `CleanEvidence` is the single definition of what is worth a person's look: Autopilot answers it for any job through `IRunEvidence.OfJobAsync` in its contracts, a `RunEvidence` with the summary, the run's exceptions and the items behind each, the verifications, the denials of the policy and of people, the declined forms, the forms with assumptions, the rule files edited (a path under `.avala/` in the diff, `.avala/checks.json` when the last declaration was edited in the worktree, `.avala/permissions.json` when a session's policy was) and the attempts that continued a hold, plus the number of decisions the rules allowed. The review sheet of the [Workbench](#review) reads it. A contract may hold no logic, so the definition cannot move into one, and computing it again in the Workbench would let the two drift; the query keeps it in one place.

A job a person submitted is never approved automatically: only the jobs a loop took are judged, so a person who runs a job by hand reviews it. Every decision, approval or refusal, is published as `AutoApprovalDecided` with its `AutoApproval`: the loop, the job, whether it was approved, the `EvidenceSummary` (attempts, the last verification's outcome and the checks it passed, the permissions allowed, the forms decided and the files changed), the exceptions, the delivery or the refusal, and the time; and it is kept in the loop's digest.

### The rules file

The `autopilot` section of `.avala/jobs.json`, read from the job's [base commit](#rules-from-the-base-commit) by `AutopilotRulesReader`:

```json
{
  "approval": "merge",
  "autopilot": { "approve": "cleanEvidence", "followUps": "accept" }
}
```

- `approve` is `never`, the default, or `cleanEvidence`. `followUps` is `refuse`, the default, or `accept`. Both are optional; without the section both keep their default.
- Jobs accepts the section as an object and Autopilot parses it strictly: at most 16 KiB, no other field, duplicate fields, values of the wrong type and nesting deeper than the format are rejected. An invalid section is `Malformed`, `UnknownField` or `UnknownRule`, a file that cannot be read `Unreadable`; either way nothing is approved automatically and no follow-up is accepted.

### Job sources

A loop takes its tasks from job sources, an extension point in `Autopilot.Contracts` that plugins implement, like the approval strategies:

```csharp
public interface IJobSource
{
    string Name { get; }
    ValueTask<Result<SourceAnswer, AutopilotError>> NextAsync(SourceRequest request, CancellationToken cancellationToken);
    ValueTask MarkAsync(SourcedTask task, TaskMark mark, CancellationToken cancellationToken);
}

public sealed record SourceRequest(LoopId Loop, string Repository, DateTimeOffset Now);
public sealed record SourcedTask(string Source, string Key, string Repository, string Instruction);
public sealed record SourceAnswer(Option<SourcedTask> Task, Option<DateTimeOffset> NextDue);
public sealed record TaskMark(TaskState State, Option<JobId> Job, DateTimeOffset At);
```

- The loop asks every registered source in registration order and takes the first task offered; when none offers one, it keeps the earliest `NextDue` any source gave. A source error ends the loop. It marks the task `Taken` with its job once the job is submitted, then `Approved`, `WaitingForPerson` or `Failed` when the iteration ends. A source never sees a job; a GitHub or Linear plugin later is one more source that keeps its own mapping.
- The module registers three, in this order: the backlog, the follow-ups and the recurring tasks. Their marks and the proposals are kept in Avala's own store, `autopilot.db` in the data folder, never in the user's repository: one row per repository, source and task, with its state, its job, when it was last taken and when it last changed.

**The backlog.** `.avala/backlog.json`, read from the repository's current base, the commit its `HEAD` points at, through `IBaseFiles.ReadCurrentAsync`, each time the loop asks, so a task committed while the loop runs is taken and an uncommitted edit is not:

```json
{
  "tasks": [
    { "id": "greet", "instruction": "Add a greeting to the README" },
    { "id": "docs", "instruction": "Document the greeting" }
  ],
  "recurring": [
    { "id": "dependencies", "instruction": "Update the dependencies and keep the build green", "everyMinutes": 1440 }
  ]
}
```

- Both lists are optional. `id` is required: 1 to 64 ASCII letters, digits, `.`, `_` and `-`, starting with a letter or a digit, unique across both lists; it is how the store remembers a task, so renaming it makes it a new task. `instruction` is required and not blank. `everyMinutes` is required for a recurring task, greater than 0 and at most a year, 525,600.
- Parsed strictly: at most 64 KiB, unknown fields, duplicate fields, values of the wrong type and nesting deeper than the format are rejected, as `TooLarge`, `UnknownField`, `Malformed`, `InvalidKey`, `DuplicateKey`, `MissingInstruction` or `InvalidInterval`; a repository that cannot be read is `Unreadable`. A rejected backlog ends the loop as `SourceFailed` with its error, since its author meant something the loop cannot know. Without the file there is nothing to do.
- The backlog source offers the first task, in file order, that the store has never seen for the repository: a task taken once is never taken again, whatever its job became; the breakers, not retries, deal with a task that fails.

**Recurring tasks.** The `recurring` list of the same file, a `TimeProvider` schedule: a task never taken is due at once, then `everyMinutes` after it was last taken. The source offers the task due the longest, and otherwise the time the next one is due, so a loop with nothing else to do waits, as `Waiting`, until then.

**Follow-ups.** The module offers agents the executed [harness tool](#harness-tools) `propose_follow_up`, whose input is an `instruction` and an optional `reason`. `FollowUpDesk` handles its calls and answers each one through `IAgents.ReturnAsync`, after `FollowUpPolicy` decided:

| Refusal | When |
| --- | --- |
| `MalformedInput` | The input is not a JSON object with a non-blank `instruction` of at most 4,000 characters; the result is an error |
| `NoJob` | The session runs no job, or the job is unknown |
| `NotAutonomous` | The session's effective [autonomy](#autonomy-levels), `IPermissionAudit.AutonomyOf`, is not `autonomous`: only an agent nobody watches proposes work for nobody to watch |
| `UnreadableRules` | The job's `.avala/jobs.json` cannot be read from its base commit or its section is invalid |
| `NotAllowed` | Its `autopilot` section does not say `"followUps": "accept"` |

An accepted proposal is stored as a `Proposed` task of the `follow-up` source for the job's repository, and the agent is told it is queued; the follow-up source offers proposals oldest first, to whatever loop runs on that repository next. Every decision is published as `FollowUpDecided`. A proposed follow-up runs as a job like any other and is approved only on its own clean evidence.

### The loop

`IAutopilot.StartAsync(LoopRequest)` starts a loop on a repository, with the connection, autonomy and attempts per round its jobs are submitted with, and its `LoopLimits`. One loop runs per repository at a time; another start is `AlreadyRunning`, a blank repository `EmptyRepository`, limits out of range `InvalidLimits`. Each loop is a `LoopRunner` whose state belongs to its own `SerialExecutor`: the bus's events, the commands and the timers all reach it as work queued there.

- **One job after another.** The loop submits the next task's job through `IJobs.SubmitAsync` and takes no other task until that job settles. Different repositories' loops run side by side, and every submission goes through the machine's [admission](#running-jobs), so the limit of running jobs holds across loops.
- **When a job settles.** `AwaitingReview`: the job is judged, approved automatically or left for a person. `NeedsHelp` after its retries ran out: the iteration failed. `NeedsHelp` from a hold: the loop waits for the `JobHeld` that follows, which says why; a hold for `LimitNearlyReached` pauses the loop, any other is a failure. `Failed`: a failure. A permission or a form left to a person (`PermissionDecided` or `FormDecided` with `LeftToHuman`): the job waits for that person, and the loop moves on. A job a person approved or discarded meanwhile ends its iteration too. A submission rejected is an iteration that failed.
- **Iterations.** Each settled task is an `IterationRecord`: its number, task and job, its `IterationOutcome` (`ApprovedAutomatically`, `AwaitingReview`, `AwaitingAnswer`, `NeedsHelp`, `Failed`, `NotSubmitted`, `ApprovedByPerson` or `Discarded`), the exceptions that kept it for a person, the hold reason, the rejection, its failure signature, whether it changed nothing, and its cost from `IUsage.OfJob`, published as `LoopIterated`.

**Circuit breakers**, checked before every task is taken; the first that trips ends the loop as `BreakerTripped`, published as `BreakerTripped` with what it measured against its cap:

| `Breaker` | Trips when | Default |
| --- | --- | --- |
| `Iterations` | The loop has taken as many tasks as `Iterations` | 50 |
| `FailuresInARow` | The last `FailuresInARow` iterations all failed: `NeedsHelp`, `Failed` or `NotSubmitted` | 3 |
| `SameFailure` | The last `SameFailure` iterations failed with the same failure signature | 2 |
| `NothingChanged` | The last `NothingChanged` iterations' jobs have an empty diff against their base commit | 2 |
| `SpendPerLoop` | What the loop's jobs spent in a currency, as `IUsage.OfJob` adds it up, reaches its cap in `SpendPerLoop` | No cap |
| `SpendPerWindow` | What the machine spent in a currency over the trailing `Window`, from the stored history through `IUsageHistory.WithinAsync`, so it counts across restarts and other loops, reaches its cap in `SpendPerWindow` | No cap, a day |
| `UsageLimit` | A limit window at or above `PauseAtLimit` reports no reset time, so there is nothing to wait for | 0.9 |

The **failure signature** comes from the verification evidence: the first check of the last report that neither passed nor was skipped, as its name, status and exit code, such as `tests Failed exit 1`; `InvalidDeclaration` when the declaration was invalid; the hold reason for a hold; the rejection for a submission refused; and the job's outcome otherwise. The same failure is the same signature in a row: the same check failing the same way, or the same hold.

**Which connection.** A loop's jobs follow the same [precedence](#job-flow-coordinator) as any job: the connection the loop request names, else the repository's default, else [capacity](#choosing-a-connection-by-capacity), chosen by Jobs when each job launches.

**Pausing across a usage limit.** Before taking a task, the loop reads limit windows from `IUsage.ByConnection`: those of the connection its request names, or, when it names none, those of every connection of the machine default's provider in `IConnections`' catalog, since any of them may take the next job. It stops taking tasks only when every connection it watches has a window at or above `PauseAtLimit` that still counts, its `ResetsAt` ahead or not reported: one connection near its limit does not stop a loop while another has capacity. It then pauses until the earliest reset among those windows, and when none of them reports a reset time the `UsageLimit` breaker trips. A pause is published as `LoopPaused` with `UsageLimit`, the window and the time it resets, on a `TimeProvider` timer; when it rings the loop publishes `LoopResumed` and takes its next task, instead of stopping. A job held as `LimitNearlyReached` by [Budgets](#budgets) pauses the loop the same way, until the reset of the window of its connection used most among those whose reset is still ahead, and is a failure when none is; on the reset the loop continues that job through `IJobs.ContinueAsync` with `The usage limit window has reset. Go on where you left off.` A reading whose window has reset no longer counts, for the loop and for Budgets.

**Commands and state.** `PauseAsync`, `ResumeAsync` and `StopAsync` act on a loop by its `LoopId`: a pause takes no new task, and lets the job underway settle; a resume takes the next one; a stop ends the loop as `Stopped` and leaves the job underway as it is. A command on an unknown loop is `UnknownLoop`, a pause of a loop already paused `NotRunning`, a resume of a loop that is not paused `NotPaused`, any command on an ended loop `LoopEnded`. `IAutopilot.Loops()` lists every loop's `LoopState`: its repository, `LoopStatus` (`Running`, `Waiting`, `Paused` or `Ended`), when it started, its iterations, the job underway, until when it waits or pauses and why, and how it ended: `Drained`, `Stopped`, `BreakerTripped` with the breaker, or `SourceFailed` with the error.

**The digest.** `IAutopilot.DigestOf(loop)` answers what a loop did as data: its state, its iterations, every automatic approval decision with its evidence summary, the breakers that tripped, the pauses, and what it spent per currency. What was approved alone is the approvals that succeeded; what waits for a person, and why, is the iterations that ended `AwaitingReview`, `AwaitingAnswer` or `NeedsHelp` with their exceptions and hold reasons. The replay of any job remains in its recording, when recording is on.

### The module

| Folder | Holds | Layer |
| --- | --- | --- |
| `Loops` | `LoopRecord`, the immutable record of one loop: its request, status, the task underway, iterations, approvals, breakers tripped and pauses, with its state and digest; `Breakers`, the breakers' rules; `FailureSignatures` | Domain |
| `Evidence` | `JobEvidence`, the facts gathered about a job; `CleanEvidence`, the exceptions and the summary; `AutopilotRules` | Domain |
| `Backlogs` | `BacklogDeclaration`, its tasks and recurring tasks, and `RecurringSchedule`, which says what is due | Domain |
| `Looping` | `LoopRunner`; `LoopRegistry`, behind `IAutopilot`; `LoopFeed`, the handler of `JobProgressed`, `JobHeld`, `PermissionDecided` and `FormDecided`; `LoopSteps`, `LoopGauges`, which reads spending and limits, and `LoopJournal` with `LoopBook`, which keep the loops' snapshots and publish their events | Application |
| `Approving` | `EvidenceGatherer`, `AutoApprover`, `JobWork`, `RunEvidenceQuery`, behind `IRunEvidence`, and the `IAutopilotRules` port | Application |
| `Sourcing` | `TaskSources`, `BacklogSource`, `RecurringSource`, `FollowUpSource`, and the `IBacklogFile` and `ITaskLedger` ports | Application |
| `FollowUps` | `FollowUpTool`, the definition of `propose_follow_up`; `FollowUpPolicy`; `FollowUpDesk`, the handler of `SessionOpened`, `JobSessionStarted` and `AgentActivity` | Application |
| `RepositoryFiles` | `AutopilotRulesReader` behind `IAutopilotRules` and `BacklogFileReader` behind `IBacklogFile`, through `IBaseFiles` | Infrastructure |
| `Storage` | `SqliteTaskLedger` behind `ITaskLedger`, in `autopilot.db` | Infrastructure |

- The domain decides and rejects nothing, so it has no aggregate; the runner refuses the commands that do not fit a loop's status. `AutopilotError` is the module's single error enum, in its contracts.
- Loops, their digests and the approval decisions live in memory: a loop does not survive a restart, and its jobs go on through recovery like any job. The marks and the proposals are stored, so a new loop never takes a backlog task again. Verification reports live in memory too, so a job judged after a restart has no verification and waits for a person, the safe way round.

## Delegation

**Accepted**

An orchestrating agent hands pieces of its work to sub-agents. Unlike a harness's native sub-agents, each one is a full Avala job, a child of the orchestrator's job: its own worktree, started from the orchestrator's current work, on a connection Avala chooses, governed, supervised, budgeted and verified like any job. When it ends, its verified work is brought into the orchestrator's worktree, and its summary, the files it changed and its verification evidence are the result of the orchestrator's call: the orchestrator receives evidence, not a sub-agent's word. The Delegation module is a plugin built on the contracts of Agents, Jobs, Workspaces, Verification, Permissions, Budgets and Observability; no module knows it.

### The tool

The module offers every agent that accepts tools the executed [harness tool](#harness-tools) `delegate`, whose input is an `instruction` and an optional `autonomy`, `supervised` or `autonomous`, and nothing else: the orchestrator never names a connection, a harness or a model. The input is malformed when it is not a JSON object with a non-blank `instruction` of at most 4,000 characters, has another field, or an autonomy outside the two. Several calls at once delegate in parallel; each call is answered when its child ends, which may take long, and Supervision does not count that wait as silence.

### The rules file

The `delegation` section of `.avala/jobs.json`, read from the [rules commit](#rules-from-the-base-commit) of the calling job's worktree by `DelegationRulesReader`, so a delegation tree is governed by the rules its root started from:

```json
{
  "delegation": {
    "connections": ["work", "personal"],
    "routing": "roundRobin",
    "maxDepth": 1,
    "maxChildren": 2
  }
}
```

- Without the file or the section, the repository does not delegate: every call is refused as `NotDeclared`. Delegation is a choice a repository makes, like follow-ups.
- `connections` is optional: 1 to 16 distinct, non-blank connection names of the machine, the [connections](#connections) children are routed to, which constrains the candidates. Without it, a child names no connection and follows the [precedence](#job-flow-coordinator) of any job: its repository's default, else capacity among all the usable connections of the machine default's provider. Whether a name is a usable connection is checked when the child is submitted.
- `routing` is `capacity`, the default, or `roundRobin`. Capacity asks the [selector](#choosing-a-connection-by-capacity) to choose among the listed connections, with the parent's worktree for the thresholds, and keeps the choice, readings compared, on the child's `DelegationRecord.Choice`; without a selector it takes the first listed. `leastUsed`, the earlier name of a policy that ranked the listed connections the same way without skipping those at their threshold, is accepted as `capacity`. Round robin gives a parent's n-th child, counted from the catalog so it holds across restarts, the connection at position n modulo the list's length, whatever their capacity, for a repository that wants its children spread evenly.
- `maxDepth` is a whole number from 1 to 8, 1 by default: a root job is at depth 0 and its children at depth 1, so the default lets jobs delegate but not their children.
- `maxChildren` is a whole number from 1 to 16, 2 by default: how many children of one parent may run at once, counted as the children whose result the parent still waits for.
- Parsed strictly: at most 16 KiB, unknown fields, duplicate fields, values of the wrong type and nesting deeper than the format are rejected as `TooLarge`, `UnknownField`, `Malformed`, `InvalidConnections`, `UnknownRouting`, `InvalidDepth` or `InvalidChildren`; a file that cannot be read is `Unreadable`. Every call of a job whose rules are rejected is refused with that error.

### Deciding a call

`DelegationDesk`, the handler of `SessionOpened`, `JobSessionStarted`, `AgentActivity`, `JobProgressed` and `JobHeld`, receives every `ToolCalled` of `delegate` and has `DelegationPolicy` decide it, in this order; the first refusal is the call's result, an error, and nothing is submitted:

| Refusal | When |
| --- | --- |
| `MalformedInput` | The input does not fit the tool's schema |
| `NoJob` | The session runs no job, or the job is unknown |
| A rules error, or `NotDeclared` | The job's rules cannot be read or declare no delegation |
| `DepthExceeded` | The child would be deeper than `maxDepth`, which is how a recursion stops |
| `TooManyChildren` | The parent already waits for `maxChildren` children |
| `AutonomyLoosened` | The call asks for `autonomous` while the caller's effective autonomy, `IPermissionAudit.AutonomyOf`, is not autonomous |
| `NotSubmitted` | `IJobs.SubmitAsync` rejected the child, such as a listed connection that is unknown or unusable; the rejection is kept with the refusal |

- **Inherited autonomy.** The child is submitted with the autonomy the call asked for, or else the caller's effective autonomy, supervised when the caller has none. Permissions then caps the child at its repository's level, which comes from the same rules commit, so a child is never looser than its parent and may be stricter.
- **The child job.** `IJobs.SubmitAsync` with the parent's repository, the instruction, the parent, the routed connection, if any, and the autonomy. When the child's report is written, the connection it ran on comes from the catalog for a child that was routed none. Jobs [starts it](#job-flow-coordinator) from a checkpoint of the parent's worktree with the parent's rules, without an admission slot. Budgets [carves](#carves) its budget out of the parent's once it is submitted and the parent's budget is known.
- Every decision is published: `ChildDelegated` once the child is submitted, `DelegationRefused` with the refusal, which is also the call's error result.

### Reporting back

The desk keeps the children it waits for, the parent session's call each one answers, and the last message each child's agent wrote, its summary. When a child settles, `ChildReporter` takes over on its own serial executor, so the bus keeps delivering while it integrates and gathers:

| The child | Outcome | What happens |
| --- | --- | --- |
| `AwaitingReview`: it passed its gates, verified | `Integrated` | `IJobs.ApproveAsync` brings its work into the parent's worktree, see [approving a child job](#review-and-approval); the child ends `Approved` |
| The same, when the merge conflicts | `Conflict` | The child stays `AwaitingReview` for a person, and the conflicting files from `IWorkspaceChanges.ConflictsAsync` are reported |
| The same, refused otherwise | `NotIntegrated` | The child stays `AwaitingReview`; the refusal is reported, such as `ParentNotRunning` |
| `NeedsHelp` after a hold | `Held` | The `JobHeld` that follows names the reason, such as `BudgetExceeded`; the child waits for a person |
| `NeedsHelp` with its retries spent | `RetriesExhausted` | The child waits for a person |
| `Failed`, `Discarded` | `Failed`, `Discarded` | |

The result of the call, a `ChildReport` written as JSON for the agent, is never an error: the job, the outcome and status, the connection and autonomy, the summary, the files changed with their counts, the last verification's outcome and checks with their exit codes, what the child spent and its carve, the commit that integrated it, the conflicting files, the hold reason and the refusal. It is returned through `IAgents.ReturnAsync`, kept in `DelegationBook` and published as `ChildReported`.

- **A parent that ends** approved, discarded or failed has the children it still waits for discarded, so nothing keeps working for a job that is gone.
- **A parent whose turn ended** before a child reported, such as one held meanwhile, no longer waits for the call: the result is kept and published, and `ReturnAsync` is refused. The child's work is not integrated into a parent that is not `Running` or `NeedsHelp`.
- **Serial integration.** Integrations run one at a time on the reporter's executor, and each runs in the parent's job queue, so two children of one parent never write its worktree at once and a parent's own checkpoint never races them.

### The module

| Folder | Holds | Layer |
| --- | --- | --- |
| `Policy` | `DelegationRules`: the declared connections, routing, depth and fan-out, the routing of a child to a connection and the refusals of depth, fan-out and autonomy | Domain |
| `Delegating` | `DelegationTool`, the definition of `delegate`; `DelegationInput`, the parsed input; `DelegationPolicy`; `ConnectionRouter`, which routes a child by capacity through the selectors or in turn; `Delegator`, which submits a child; `DelegationDesk`, the handler; and the `IDelegationRules` port | Application |
| `Reporting` | `ChildReporter`, which integrates a settled child and reports it; `ChildEvidence`, its diff, verification and summary; `ChildSpending`, its spending and carve | Application |
| `Records` | `DelegationBook`, behind `IDelegations`; `DelegationJournal`, which answers the call, keeps the record and publishes it; `ToolAnswers`, the results as JSON | Application |
| `RepositoryFiles` | `DelegationRulesReader` behind `IDelegationRules`, through `IBaseFiles`, and `DelegationRulesParser` | Infrastructure |

- The domain decides and rejects nothing, so it has no aggregate. `DelegationError` is the module's single error enum, in its contracts.
- Delegation records live in memory: a delegation does not survive a restart. The tree survives in Jobs, the carves in Budgets, and the children go on through recovery like any job; their parent's new session is not told their results.

## Workbench

**Accepted**

The user interface is a module like any other, built view model first, see [Delivery](#delivery): phase 9 builds and tests its view models against the simulator, with plain placeholder views, and phase 10 designs the views from the approved [brief](ui-brief.md).

### Where the view models live

- **One module for the screens.** A screen of the approved design combines several modules: the conversation of a job reads Agents, Jobs, Permissions, Canvas and Verification. A module depends only on the SDK and other modules' contracts, so a screen can only be composed outside the modules whose data it shows. The Workbench is that module: it depends on the SDK, the SDK's UI contracts and the contracts it reads, and no module depends on it. The placeholder jobs page of Jobs is gone; Jobs keeps its plugin entry in `Avala.Jobs.UI`.
- **Two projects.** `Avala.Workbench` holds the application layer, the projections and the commands, and the view models, without Avalonia. `Avala.Workbench.UI` holds the views and the plugin entry, which registers the main window's view model as the shell's page.
- **Layers.** The view models use the application folders only, never infrastructure, and the module has none. Each view model takes at most four dependencies and delegates every command to a service of the application layer.
- **A view model per kind of entry.** Every entry of the conversation, every sidebar row and every card is a view model with its own view, resolved through the view registry: one view model and one template per kind, never one view that knows every kind.
- **Placeholder views.** Every `XViewModel` has an `XView`. Until phase 10 they are plain Avalonia controls bound to what the view model exposes, so the application runs, and the architecture rule holds, while the visual language is designed.

### The job board

`BoardKeeper` is one handler of `StartupCompleted`, `JobSubmitted`, `JobProgressed`, `JobHeld`, `JobApproved`, `JobSessionStarted`, `AgentActivity`, `CanvasUpdated`, `PermissionDecided`, `FormDecided` and `AttemptVerified`. One handler is one mailbox, so it sees all of them in the order they were published: a session is tied to its job before its first activity arrives, and a decision follows the request it decides. It keeps every job as a `BoardJob` from the moment the application starts, whether or not a screen shows it, so a conversation opened late shows everything since startup.

- **What it asks Jobs.** `IJobCatalog.HistoryAsync` when a job is submitted, when a job it has not seen yet progresses or starts a session, and on every `JobProgressed`, to read the job's attempts; `ListAsync` once, on `StartupCompleted`, for the jobs of earlier runs.
- **What it publishes.** `JobBoard` holds the immutable dictionary of jobs the keeper replaces whole, read with `Volatile`. A view model watches it through `ChangesAsync`, a channel of one pending signal per watcher: a change while one is pending is dropped, and the watcher reads the latest board when it wakes, so a burst of streamed text costs the interface one update. No lock: the keeper's mailbox is the only writer.
- **`BoardJob`.** The job's `JobSummary` with its latest status, the number of its attempts, its `HoldReason` until it runs again, its latest `VerificationReport`, its `ApprovalDelivery`, its `Transcript` and its `Revision`. It derives the sidebar's group, its one secondary fact and its pending decisions.
- **The revision.** A counter the keeper moves on every event that changes what the review sheet or the inspector read about the job: `JobProgressed`, `JobHeld`, `JobApproved`, `JobSessionStarted`, `AttemptVerified`, `PermissionDecided`, `FormDecided`, `PermissionAnswered`, `AutonomyApplied`, `UsageRecorded`, `BudgetIntervened`, `BudgetCarved` for the parent and the child, and `ChildDelegated` and `ChildReported` for the parent and the child. Streamed text, tool output and canvases change the transcript but not the revision, so an open panel reloads its queries once per fact, not once per token. Each of these events is published after the query it concerns includes it, and the keeper sees them in order in its one mailbox.

| Group | Jobs |
| --- | --- |
| Needs you | Any job with a decision waiting for a person, and every `NeedsHelp` job |
| Running | `Draft`, `Preparing`, `Running` and `Checking` |
| Ready for review | `AwaitingReview` |
| Done | `Approved`, `Discarded` and `Failed` |

| Fact | When |
| --- | --- |
| Asks permission, by kind: wants to run a command, to edit a file, to reach the web, to use a tool | A permission left to a person is waiting |
| Asks a question, to approve a plan, for input | A form left to a person is waiting, by its purpose |
| `2 of 4` | A running job with a plan |
| Working, verifying, starting | Running without a plan, checking, preparing |
| Held, with the reason; needs help | `NeedsHelp` after a hold, or after its retries ran out |
| Verified on attempt N, no checks declared, ready for review | Awaiting review, by its latest verification |
| Merged or approved, discarded, failed | Ended |

A decision waits for a person only once the policy said so: a permission whose `PermissionDecided` was `LeftToHuman` or `Undelivered`, or a form whose `FormDecided` carries no answer, as long as nothing resolved or answered it and its turn has not ended. A request the policy answers itself never counts, so the sidebar never flashes for an edit the policy allowed a moment later. Without the Permissions plugin nothing counts as waiting; the plugin is part of every composition.

### The conversation projection

`Transcript`, in the `Timeline` folder, is an immutable record of a job's entries in the order they happened, each with a stable key, and the functions that apply an event to it. It performs no I/O and takes the time from its caller, so it is tested without a bus or a clock.

| Entry | From | Holds |
| --- | --- | --- |
| `PromptEntry` | The job's attempts, from the catalog | The attempt's number, origin and outcome, and what was sent: the instruction for the first attempt, the feedback, hint or review for the others, nothing for a recovery |
| `RestartEntry` | A job of an earlier run | Marks where what the board saw itself begins |
| `MessageEntry` | `ItemStarted` of a message | The streamed text, appended in order, and its outcome |
| `ReasoningEntry` | `ItemStarted` of reasoning | The streamed text, and how long the agent thought, from `ItemStarted` to `ItemCompleted` as the keeper's `TimeProvider` measured them |
| `ToolEntry` | `ItemStarted` of any other kind, or `ToolCalled` | Kind, title, output appended in order, outcome; for an executed harness tool, its input and the result it returned |
| `PlanEntry` | `PlanUpdated` | The latest steps of the turn's plan, replaced in place, with done and total |
| `CanvasEntry` | `CanvasStarted`, then `CanvasUpdated` | Title, media type, and the content and status of the latest throttled snapshot; the raw chunks are ignored, so the view never renders more often than the Canvas module publishes. Its view model hands each snapshot to a shared canvas surface, which keeps them as versions |
| `PermissionEntry` | `PermissionRequested`, `PermissionDecided`, `PermissionResolved` | The request's session, item, kind, title and target, the policy's decision, the resolution, whether its turn ended |
| `FormEntry` | `FormRequested`, `FormDecided`, `FormAnswered`, `ItemCompleted` | The form, the policy's decision, the answer, the outcome, whether its turn ended |
| `TurnEndEntry` | `TurnCompleted` | The turn's outcome, its duration, and the tokens and costs it reported |

- Content for an item that never started is ignored, as is any event the projection does not show, such as limits and resume tokens.
- What the person or the harness sent the agent is not an agent event, so the user's side of the conversation comes from Jobs: every attempt starts with a message, and its guidance is that message. The board refreshes the attempts on every `JobProgressed`, which Jobs publishes after storing the attempt and before sending its message, so a prompt always precedes the turn it starts.
- The conversation shows a permission card only when the request went to a person. The requests the policy answered stay in the transcript for the inspector.
- A canvas is shown from the board's snapshots, not from `ICanvases.InSession`: the board follows `CanvasUpdated` from startup, so it already holds every canvas the query would return.

### Jobs that ran before the application started

The agent's events are not stored, so a job of an earlier run cannot be replayed. When the application starts, the board adds every job of the catalog with one prompt per attempt, each with its origin, what was sent and its outcome, then a `RestartEntry`; whatever the job does next follows it, a recovery included. A job of an earlier run that is recovered before the catalog is read joins the same way.

**Proposed**, not built: storing the stream. The keeper would append every entry-changing event of a session to a file per session in the data folder, or rows of a `workbench.db`, and fold them through `Transcript` at startup, so a conversation survives a restart and a replay can digest what happened while a person was away. Its retention would follow the job's worktree. The recordings of the Recording module are not that store: they are opt-in, written for the simulator to replay a provider, and carry no decisions or snapshots.

### The composer

`JobSteering` gives the composer's commands to Jobs:

- **Send.** To a job that needs a person, `IJobs.ContinueAsync`; to a job awaiting review, `IJobs.SendBackAsync`, a new round with the message as feedback. A running job takes no message, and `NotHeld` is returned without asking Jobs: steering an agent in the middle of a turn is not in the core, and a message sent straight to its session would start a turn Jobs does not count as an attempt.
- **Interrupt.** `IJobs.HoldAsync` with `HoldReason.Interrupted`. Interrupting the agent directly through `IAgents.InterruptAsync` would fail the job, since an interrupted turn of a running job fails it. The hold interrupts the turn, keeps the session, and the job waits as `NeedsHelp` until a person continues it with a message.
- **Stop.** `IJobs.HoldAsync` with `HoldReason.Stopped`: the agent's session is stopped rather than interrupted, the job waits as `NeedsHelp` with its worktree and its work intact, and a message continues it, in a new session that resumes the conversation when the provider can. Only a running job can be stopped. Stop used to discard the job, which with the default retention deleted its worktree at once, too destructive for a button always in view; discarding now lives only in the [review sheet](#review), behind a confirmation.
- A rejection is shown as text in the composer; the draft stays when sending was refused.

### Answering in place

- **A permission card** answers through `IPermissionAnswers.AnswerAsync`, `Allow` or `Deny`, with the optional note for the agent and "don't ask again". Its commands are enabled only while the request waits for a person; a refusal, such as `NotAwaitingAnswer`, is shown on the card.
- **A form card** renders any `AgentForm`: one field view model per field, one choice view model per option. The recommended options start selected, so answering with them is one command; choosing another option of a single choice unselects the first, and text typed for a single choice that accepts it is sent in place of an option. Answering is enabled once every field is complete, by the rules `IAgents.AnswerAsync` checks; declining sends the note as the form's message.

### Review

The review sheet opens on demand for the selected job when it awaits review or is held, over the dimmed page, and closes when another job is selected, from its close button, with Escape or once its outcome is stamped. Its header names where the job stands, its title, and its repository, connection, autonomy and sessions, from the catalog's history. It answers one question, can this work be trusted, in this order:

- **The verdict first**, from the job's verifications: verified on attempt N of M, drawn with a check, no checks declared, not verified with the attempt that failed, or an invalid declaration; under it, the checks of the last attempt that passed or failed.
- **Then only the exceptions**, "worth a look", from `IRunEvidence`, so an exception means on the sheet exactly what it means to Autopilot's automatic approval: each earlier or last attempt that failed, with only its failing checks and the tail of their output; each denial by the policy or by a person and each declined form; each assumption the policy made; each continuation after a hold; each rule file edited; a diff that could not be read. An earlier attempt that failed and was fixed by a retry does not keep a job from automatic approval, but it is still worth a look, so the sheet lists it. Each is one folded row, a short title and a fact, marked red for a failed attempt, amber for a hold, a rule file or an unreadable diff, and a ring otherwise; only the first starts open, and what the reviewer opened stays open when the sheet reloads.
- **One quiet line**: how many other decisions the rules allowed, the cost and the tokens, from `IUsage.OfJob`.
- **The diff**, folded under its file count and line totals: the files with their counts from `IWorkspaceChanges.DiffAsync`, and a file's hunks on demand from `FileDiffAsync`.
- **The commands**: approve, send back with feedback, and discard, through `IJobs`. Approve and send back are enabled only while the job awaits review; send back first opens the feedback box; discard takes two steps, a request and a confirmation, and works for any job that has not ended. One command runs at a time, and none once the sheet is closed, so a second click delivers nothing twice. A refusal is shown in place and the commands stay: a merge conflict lists the conflicting files, read from `ConflictsAsync`, a base checkout with uncommitted changes asks to commit or stash them. An outcome closes the sheet with a stamp: how the job was delivered, sent back or discarded, and a job approved or discarded elsewhere closes it too.

`ReviewReader` gathers the facts, `ReviewDesk` gives the commands, and `ReviewExceptions` turns the evidence into the verdict and the typed exceptions, which `ReviewPhrases` words.

### Decisions

The decisions popover, opened from the toolbar's count of pending decisions and pointing at it, lists every permission and form that waits for a person across all jobs, the same `Transcript.Awaiting` the sidebar counts, oldest first, with the request, the job's title, what it asks and how long it has waited since the policy left it to a person. Each item holds the card view model of the conversation, so an answer from the popover is the same answer, and a card answered elsewhere leaves the popover on the next board change while the selection stays on its decision. Only the selected item is open: a permission's whole command, a form's context and the numbered options of its first choice field, and a link that opens the job's conversation. Keyboard first, with the keys in its footer: J and K (or the arrows) move, 1 to n choose an option, Enter answers (allows a permission, submits a form), Backspace denies (denies a permission, declines a form), Shift+Enter opens a note for the agent sent with the answer, and Escape closes the popover. A note belongs to the selected decision and is dropped when the selection moves; digits and Backspace typed in it never choose nor deny. With nothing waiting it says "Nothing needs you".

### Inspector

The inspector, closed by default, shows the selected job in short sections, each a folded row with a one-fact summary, such as "4 of 4 passed", "1 denied" in amber or "USD 0.84 of USD 5", opened on demand, each a small view model loaded when the inspector opens and reloaded when the job's revision moves:

| Section | From |
| --- | --- |
| Evidence | `IVerifications.OfJob`: the verdict and one line per verified attempt with its checks |
| Decisions and assumptions | `IPermissionAudit.OfJob`, `AnswersOfJob` and `FormsOfJob`: what the rules allowed, what a person answered, what was denied, and each assumption |
| Usage and caps | `IUsage.OfJob`, the caps of the latest session's `IBudgets.BudgetOf`, the interventions of `IBudgets.OfJob`, and the carve of a child job from `CarveOf` |
| Autonomy and connection | The latest session's `IPermissionAudit.AutonomyOf`, and the connection of the catalog |
| Worktree | The branch, the base branch and commit, and the path, from `IWorkspaces.FindAsync`, and the port lease of the worktree from `IResources.Leases` |
| Delegation | The parent from the catalog, and each child from `IJobCatalog.ChildrenAsync` with its status, connection and the outcome its `IDelegations.OfParent` record reports, and the refused delegations |

`JobRecords` reads the catalog, the worktree, the lease and the delegations, `JobAudit` the in-memory audits, and `JobInspection` joins them.

Each section is content of the shell's `Inspector` region, registered with `AddToRegion` in this order, so another module can add its own section beside them. The region's context is the job in focus, a `JobId`, which each section receives through `IRegionAware<JobId>`; no section subscribes to the selection. The jobs page sets that context while the inspector is open, the page is active and a job is selected, and clears it otherwise, and the shell shows the inspector only while it has a context. `InspectedJob` is what each section loads with: it follows the board while the section is active, requests a load when the focus changes or the focused job's revision moves, applies a load only if it answers the latest request for the job still in focus, drops one queued before a deactivation, and raises `Presented` after each change it shows. The sections share one read per job and revision through `InspectedFacts`, so opening the inspector costs one read, not six.

### Navigation

The shell lists the pages plugins register, `IPage`, and activates the selected one through `IActivatable`. The main window is composed of the shell's regions: the job list, `SidebarViewModel`, is content of the `Sidebar` region and follows the board whichever page is shown, a sub-agent's job nested under its orchestrator's row rather than in a group of its own, moved there if it was seen first; `ToolbarViewModel`, in the `Toolbar` region beside the logo, counts the decisions waiting across jobs, opens their popover (Ctrl+D, Escape closes it) and asks for the new job page (Ctrl+N); the jobs page, `WorkbenchViewModel`, is the first page of the `Content` region and holds the conversation of the selected job and the review sheet; the inspector's sections fill the `Inspector` region. Choosing a job in the sidebar publishes `JobSelected` over the `IMessenger`; the jobs page opens that job's conversation, keeps it and its review when the same job is chosen again, and asks the shell to show it with `PageRequested`. The inspector, closed by default, is toggled on the page, which sets the `Inspector` region's context to the selected job. Each of these view models follows the board while active through a `BoardFeed`, updates on the UI thread through `IUiDispatcher` and raises `Presented` after each change it shows; the view models are touched on that thread only.

The jobs page heads the open conversation with its title, where it runs, `Place` (the repository's folder and the connection), and a status pill, `Pill`, whose dot pulses while the agent works and falls still when the job is held. The conversation follows its live edge: while the reader is at the end it scrolls to what arrives, and once they scroll up nothing arriving moves it. A reply grows chunk by chunk, each new chunk fading in behind a caret; a thought shows live dots and a sweeping "Thinking" until it ends as "Thought for Ns", and opens on demand; a tool is one line with its kind's glyph, a spinner while it runs and its outcome only when it failed, and opens to its input and output; an open line stays open while its entry keeps updating. A permission or form card waiting for a person says what the agent wants in amber, answers with Ctrl+Enter and refuses with Ctrl+Backspace from anywhere inside it, keeps its note out of the way until asked for, and folds into a quiet line with its verdict once answered. The composer's placeholder says what a message does for the job's status, `Placeholder`.

- **Loading the panels.** The review sheet and the inspector load their facts off the UI thread and apply them through `IUiDispatcher`. Each load is requested on the UI thread with the board revision it answers, and applies only if no newer request was made since and the page is still active: work queued for the UI thread before a deactivation is dropped, as the board updates are. Opening a panel loads it at once; the follow loop then reloads an open panel whose job's revision moved, one load after another, after the sidebar and conversation are shown.

The other pages follow the main window in the shell's list: New job, Overview, Usage, Resources and Settings.

### The global pages

Each global page is an `IPage` and `IActivatable`, a view model of its own folder over a reader of the application layer that turns contracts into immutable records, and item view models that are immutable too, rebuilt whole on every change.

- **Following.** `Pulse` is one handler of the events that change what the pages show and the board does not: `UsageRecorded`, `ResourcesSampled`, `OrphansFound`, `OrphansReaped`, `WorktreeReclaimed`, `WorktreesReconciled`, `ChildDelegated`, `DelegationRefused`, `ChildReported`, `BudgetCarved`, `BudgetIntervened` and `SupervisorIntervened`. Its `ChangesAsync` merges them with the board's changes into one channel of one pending signal per watcher, as the board does. `LiveFeed` is what an active page follows with: on every signal it reads its state on the thread pool, then shows it on the UI thread through `IUiDispatcher`, and a state queued for the UI before the page was deactivated is dropped. A command that changes nothing the bus reports asks the feed to refresh.
- **What the pages learn from sessions.** `SessionBook` handles `SessionOpened` and `JobSessionStarted` and keeps, for each session, its connection, provider and account, the latest session of each connection and of each job. It gives a connection its account and provider, a job its connection while it runs, and both the caps of their latest session through `IBudgets.BudgetOf`. Only sessions opened since the application started are known; a connection or job of an earlier run shows no account and no caps until it opens a session again.
- **Overview.** Two views of one page. Connections: every declared connection, then any other that reported usage, with its provider, its account, whether it is the default, its cost and limit windows from `IUsage.ByConnection`, and the jobs of the board that are preparing, running or checking on it. Delegation: the root jobs that have children, from `IJobCatalog.ListAsync` and `IDelegations`; the latest is shown until another is selected, its tree from `IJobCatalog.TreeAsync` flattened with each child's depth, connection, harness (the provider of its latest session), latest activity (the outcome reported to its parent, or the board's fact), spend from `IUsage.OfJob` against its carve from `IBudgets.CarveOf`, and the delegations its tree refused.
- **Usage.** Per connection: cost, tokens, the reports that had no cost, the caps of its latest session and each limit window with its reset time and the threshold at which those caps hold a job. Over time: today in the local zone, from `IUsageHistory.DailyAsync`, and the last seven days, from `WithinAsync`, each with tokens by type and turns. Per job of the board that spent or was held: cost against the caps of its latest session and its carve. And every intervention of Budgets and Supervision across those jobs, latest first; both modules restore their interventions at startup, so jobs of earlier runs count.
- **Resources.** The machine's memory, CPU, processes, disk and ports from `IResources.Global`; each process tree of the latest sample with the job, connection and provider it is attributed to; the latest report of each tree that left orphans, cleaned up through `IOrphans.ReapAsync` of its job while left running; the stale worktrees of the latest `WorktreesReconciled`, none once it cleaned, looked for again through `IWorktreeHousekeeping.ReconcileAsync` and cleaned through `CleanAsync`; and the port leases and conflicts. `ResourceIndicatorViewModel` is the small indicator the sidebar can host: memory in use and the leftovers, orphans left running plus stale worktrees.
- **Settings.** The repository, chosen among those of the board's jobs or typed, read only: its declared autonomy and form strategy, its rules in decision order with their origin, its budget caps, top-level and per connection, its checks, and the sections of `.avala/jobs.json`, see [a repository's current rules](#rules-from-the-base-commit); each file with its status, the commit it was read from and whether the checkout's copy differs, and "Edit in repository", which opens the checkout's file. The machine, in the data folder: the connections, each with whether it was declared in `connections.json`, discovered on the machine or implicit, and the file's status, listed and opened for editing rather than edited in place, since a connection's credential source and reference are not something a form should guess; the silence window, edited and written through `ISupervision.ChangeSilenceAsync`, a refusal shown by its `SupervisionError`; and the resource settings, shown.
- **New job.** The repository, proposed from the latest job, the instruction, the connection, the repository's default or one of `IConnections.CatalogAsync`, and "Supervised", the only autonomy a job can ask for that its repository may not already declare, since a job can tighten its autonomy but never loosen it. It submits through `IJobs.SubmitAsync` and shows a `JobRejection` as text, keeping the instruction.

### Opening a file

"Edit in repository" and "Open connections.json" ask the platform to open a file in the application that edits it. `IFileOpener` in the SDK is that port, `OpenAsync(path)` answering the path or a `FileOpenError`: `NotFound` for a file that does not exist, `Unavailable` when nothing can open it, `Refused` when the platform declined. The host implements it with Avalonia's launcher on the main window, which answers `Unavailable` without one, as in the host simulation tests; `CompositionRoot.Create` takes another implementation for a test that needs one, and unit tests use a fake. The Workbench's `SettingsFiles` resolves a rule file inside the repository's checkout and a machine file inside the data folder; the checkout's copy is the one a person edits, and a change applies to the jobs that start from the commit that holds it.

### The module

| Folder | Holds | Layer |
| --- | --- | --- |
| `Timeline` | `Transcript`, its entries and their keys | Application |
| `Board` | `BoardJob`, `JobFacts`, the groups and facts of the sidebar, `JobBoard`, and `BoardKeeper`, the handler | Application |
| `Steering` | `JobSteering`, the composer's commands, and what a status accepts | Application |
| `Replies` | `HumanReplies`, the answers to permissions and forms, and `FieldChoice`, the answer to one field | Application |
| `Navigation` | `WorkbenchViewModel`, the main window's page | ViewModels |
| `Sidebar` | `SidebarViewModel`, `JobRowViewModel`, `ToolbarViewModel` and the wording of the facts | ViewModels |
| `Conversation` | `ConversationViewModel`, `ComposerViewModel`, one view model per kind of entry, and `Conversations`, which opens one per job | ViewModels |
| `Cards` | `PermissionCardViewModel`, `FormCardViewModel`, `FormFieldViewModel` and `FormChoiceViewModel`, which the decisions popover reuses | ViewModels |
| `Reviewing` | `ReviewReader`, `ReviewDesk`, `ReviewExceptions`, the verdict and the typed exceptions of a run | Application |
| `Inspection` | `JobRecords`, `JobAudit` and `JobInspection`, the facts of the inspector | Application |
| `Review` | `ReviewViewModel`, `ReviewExceptionViewModel`, `ChangedFileViewModel`, `HunkViewModel`, `ReviewPhrases` and `Amounts` | ViewModels |
| `Decisions` | `DecisionsViewModel`, the popover, and `DecisionViewModel`, one waiting decision | ViewModels |
| `Inspector` | The six section view models of the `Inspector` region, `InspectedJob`, which loads a section for the job in focus, and `InspectorPhrases` | ViewModels |
| `Following` | `Pulse`, the handler whose signals the global pages follow, `LiveFeed`, which reads and shows a page's state while it is active, and `SessionBook` | Application |
| `Fleet` | `FleetReader`, the connections and the agents on them, and `DelegationReader`, the orchestrators and their trees | Application |
| `Spending` | `JobSpending`, a job's spend, caps, carve and interventions, `UsageReader` and `UsageWindows` | Application |
| `RepositoryRules` | `RulesReader`, a repository's current rule files | Application |
| `Machine` | `MachineSettings`, the settings of the data folder and the silence window's change, and `SettingsFiles`, which opens a file through `IFileOpener` | Application |
| `Upkeep` | `ResourceReader`, `Leftovers`, the handler of the latest reconciliation, and `Housekeeping`, the clean-up commands | Application |
| `Submitting` | `JobLaunch`, a new job's submission | Application |
| `Presenting` | The wording of amounts, times and caps, and the replacement of a list's items, shared by the pages | ViewModels |
| `Overview`, `Usage`, `Settings`, `Resources`, `NewJob` | Each global page's view model and its items | ViewModels |

`JobScreens`, in `Navigation`, opens a job's conversation and review sheet through the `Conversations` and `Reviews` factories, and `JobFocus` holds the page's links to the shell, the selection message, the page request and the inspector's context, so the jobs page keeps three dependencies.

Every view model implements its `IXViewModel`, which its view binds to and its parent holds, and has a `DesignXViewModel` with the sample work of the [brief](ui-brief.md), in a `DesignViewModels.cs` of its folder. `Presenting` also holds `BoardFeed`, the board follower of the sidebar, the decisions popover, the jobs page and the inspector's sections, and `SampleJobs`, the identifiers of the sample jobs.

- The module has no domain: the board and the projection are facts that already happened, like Observability's, so it has no aggregate and no error enum. It shows the errors of the modules it calls.
- The board lives in memory, every job's transcript since startup. Dropping the transcripts of ended jobs, and the canvases of their sessions, arrives with the first measure of their size.

## Data the harness produces

The user interface is designed from the data the harness produces, so every module that produces data lists it here: its integration events on the bus and its queries, with their shape, when and how often they are produced, and their cardinality.

### Verification

| Data | Kind | Shape | When | Cardinality |
| --- | --- | --- | --- | --- |
| `AttemptVerified` | Integration event | `Report`: a `VerificationReport` | Once per evaluation of the gate, after the checks of an attempt ran and before Jobs moves the job on, so it precedes the `JobProgressed` of the retry, the review or the request for help | One per finished turn of every job that reaches the gates, including recovery attempts. A repository without checks produces one too |
| `IVerifications.OfJob(JobId)` | Query | `IReadOnlyList<VerificationReport>` in the order the attempts were verified; empty for an unknown job | At any time, from memory | One report per evaluation of that job since the application started |
| `IRepositoryChecks.OfRepositoryAsync(repository)` | Query | `RepositoryChecks`: `File` (`ChecksFileStatus`: `Absent`, `Applied`, `Rejected`), `Checks`, each a `CheckDeclared` with `Name`, `Command` (the command line) and `Timeout`, and the `Option<FileOrigin>` of the repository's current commit | On demand, through `IBaseFiles.ReadCurrentAsync` | One answer per call |

`VerificationReport` and its parts, in `Avala.Verification.Contracts`:

| Type | Fields |
| --- | --- |
| `VerificationReport` | `Job`: `JobId`; `Attempt`: the attempt number Jobs gave the gate; `Outcome`: `VerificationOutcome`; `Declaration`: the `Option<FileOrigin>` of `.avala/checks.json`, the base commit it was read from and whether the worktree's copy differs, absent when it could not be read; `Checks`: `IReadOnlyList<CheckEvidence>` in declaration order, empty when none were declared or the declaration is invalid; `Verdict`: the `GateVerdict` returned to Jobs, whose `Feedback` is the text sent back to the agent on `Retry` and empty on `Pass`; `VerifiedAt`: `DateTimeOffset` |
| `VerificationOutcome` | `Passed`, `Failed`, `NoChecksDeclared`, `InvalidDeclaration` |
| `CheckEvidence` | `Name`; `Command`: the command line as run, arguments containing spaces or quotes quoted; `Status`: `CheckStatus`; `ExitCode`: `Option<int>`, absent unless the process exited; `Duration`: `TimeSpan`, zero when skipped; `OutputTail` and `ErrorTail`: at most 4,000 characters each, plus the `[...]` marker |
| `CheckStatus` | `Passed`, `Failed`, `TimedOut`, `NotFound`, `Skipped` |

Live progress of a check while it runs is not published yet: the job's `JobProgressed` with `Checking` marks the whole evaluation.

### Permissions

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `PolicyLoaded` | Event | `SessionPolicy`: session, `PolicyFileStatus` (`Absent`, `Applied` or `Rejected`), `Option<PolicyError>`, the effective rules in decision order, and `Origin`, the `Option<FileOrigin>` of the policy file: the base commit it was read from and whether the worktree's copy differs, absent outside a job's worktree or when the commit could not be read | When a session opens, after its policy file was read and before any of its activity is handled | One per session |
| `PermissionDecided` | Event | `PolicyDecision`: session, turn, item, `Option<JobId>`, item kind, target as matched, `PolicyAnswer`, `Option<PolicyRule>` that decided (none for the default), `DecisionDelivery` (`Answered`, `LeftToHuman` or `Undelivered`) and the time from `TimeProvider` | For every permission request the `Turn` aggregate accepted, after the answer was sent. It may follow the `PermissionResolved` that its answer caused | One per permission request |
| `IPermissionAudit.PolicyOf` | Query | `Option<SessionPolicy>` | Any time; none until the session opened | One per session |
| `IPermissionAudit.OfSession` | Query | The session's `PolicyDecision`s in decision order | Any time | Zero or more per session |
| `IPermissionAudit.OfJob` | Query | The `PolicyDecision`s of every session of a job, recovery included, by time | Any time; a session counts once `JobSessionStarted` tied it to the job | Zero or more per job |
| `AutonomyApplied` | Event | `SessionAutonomy`: `Session`, `Job`, `Declared` (the repository's `Autonomy`: `Supervised` or `Autonomous`), `Requested` (the job's `Option<Autonomy>`), `Effective` and `Refused`, true when the job asked for more than its repository declares | On every `JobSessionStarted`, after `PolicyLoaded` and before the session's first activity | One per session of a job |
| `FormDecided` | Event | `FormDecision`: session, turn, item, `Option<JobId>`, the `AgentForm` as asked, the `Autonomy` it was decided at, `Option<FormAnswer>` the policy gave (none when left to a human), `Assumptions` (one per field answered automatically), `DecisionDelivery` and the time | For every form the `Turn` aggregate accepted, after the automatic answer was sent or the form was left to a human | One per form |
| `PermissionAnswered` | Event | `HumanAnswer`: session, `Option<JobId>`, item, item kind and target as matched, the `PermissionAnswer`, the `Option<string>` message for the agent, the `Option<PolicyRule>` session rule it created and the time | When a human answered a request left to them through `IPermissionAnswers`, once the answer reached the agent | Zero or one per request left to a human |
| `IPermissionAnswers.AnswerAsync(session, PermissionReply)` | Command answer | `Result<HumanAnswer, PolicyError>`: the answer as recorded, or `NotAwaitingAnswer`. `PermissionReply` has the item, `Allow` or `Deny`, an optional `Message` and `DontAskAgain` | When a human answers | One per human answer |
| `IPermissionAudit.AutonomyOf` | Query | `Option<SessionAutonomy>`; none until the session's job started it | Any time | One per session |
| `IPermissionAudit.SessionRulesOf` | Query | The session rules a human created, in the order they were created | Any time | Zero or more per session |
| `IPermissionAudit.FormsOfSession`, `FormsOfJob` | Query | The `FormDecision`s of a session, or of every session of a job by time | Any time | Zero or more per session and job |
| `IPermissionAudit.AnswersOfJob` | Query | The `HumanAnswer`s given to a job's requests, in the order given | Any time | Zero or more per job |
| `IRepositoryPolicies.OfRepositoryAsync(repository)` | Query | `RepositoryPolicy`: `File` (`PolicyFileStatus`), `Option<PolicyError>`, `Rules` in decision order without session rules, `Origin` (the `Option<FileOrigin>` of the repository's current commit), `Autonomy` and `Strategy` | On demand, through `IBaseFiles.ReadCurrentAsync` | One answer per call |

A `PolicyRule` carries its origin (`BuiltIn`, `Repository` or `Session`), name, `Option<ItemKind>`, `Option<string>` target pattern (an exact target for a session rule), `RuleScope` (`Anywhere`, `Workspace` or `OutsideWorkspace`) and answer, so a decision explains itself without another query. `SessionPolicy` also carries the repository's declared `Autonomy` and `FormStrategy` (`Recommended` or `BestJudgment`), and `PolicyDecision` the `Autonomy` it was decided at. An `Assumption` has the field's `Field` identifier, its `Prompt`, its `Basis` (`RecommendedOption`, `FirstOption`, `AgentJudgment` or `Confirmed`) and the labels `Chosen`, empty when the agent was told to decide.

### Agents: forms

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `AgentActivity` of `FormRequested` | Event | `Session`, `Turn`, `Item`, `Form`: an `AgentForm` with `Purpose` (`Permission`, `Question`, `PlanApproval`, `Other`), `Title`, `Context` and `Fields`; each `FormField` has `Id`, `Header`, `Prompt`, `Kind` (`SingleChoice`, `MultipleChoice`, `FreeText`, `Confirmation`), `Options` (each a `FormOption` with `Label`, `Description` and `Recommended`) and `AcceptsFreeText` | When a provider that declares `AsksQuestions` asks, inside a turn, once the `Turn` aggregate accepted the form as well formed | Zero or more per turn, one waiting at a time |
| `AgentActivity` of `FormAnswered` | Event | `Session`, `Turn`, `Item`, `Answer`: a `FormAnswer` with `Item`, `Fields` (each a `FieldAnswer` with `Field`, `Chosen` labels, `Option<string>` `Text` and `Confirmed`), `Declined` and an `Option<string>` `Message` | When the provider reports the answer it received, from a human or from the policy; then `ItemCompleted` closes the form | One per answered form |
| `IAgents.AnswerAsync(session, FormAnswer)` | Command answer | `Result<ItemId, AgentError>`: the item answered, or `SessionClosed`, `Unsupported`, `NoPendingForm`, `InvalidAnswer` | When a human or the policy answers | One per answer |
| `PermissionDecision.Message` | Field of a command | `Option<string>` sent to the agent with the answer, the reason for a `Deny` and what to do instead | With `IAgents.RespondAsync` | One per answer |

### Jobs: autonomy

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `JobRequest.Autonomy` | Field of a command | `Option<Autonomy>`, the level the job asks for, stored with the job | At submission | One per job |
| `JobSessionStarted.Autonomy` | Field of an event | The job's `Option<Autonomy>` | With every `JobSessionStarted` | One per session of a job |

### Agents: connections

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `IConnections.CatalogAsync()` | Query | `ConnectionCatalog`: `File` (`ConnectionFileStatus`: `Absent`, `Applied` or `Rejected`), `Option<ConnectionError>`, `Connections` in declaration order, each a `DeclaredConnection` with `Name`, `Provider` (the provider's identifier) and the `Option<string>` name of its credential `Source`, never its reference or secret, and its `Origin` (`ConnectionOrigin`: `Declared`, `Discovered` or `Implicit`); and the `Option<ConnectionName>` `Default`, none when the file is rejected | Any time; reads `connections.json` and runs the discoveries the first time | One per application: the declared and discovered connections, the implicit ones of providers that discovered none without a file, none when the file is rejected |
| `IConnectionDiscovery.DiscoverAsync()` | Extension point | `IReadOnlyList<DiscoveredConnection>`: `Name`, `Provider`, `Credential` (`CredentialReference`: `Source` and `Reference`, never a secret) and `Settings` | Once per application, when connections are first needed | One per account a provider plugin finds on the machine |
| `IConnections.CheckAsync(Option<ConnectionName>)` | Query | `Result<ConnectionInfo, ConnectionError>`: the connection's `Name` and the `ProviderInfo` of its provider, the default one when none is named; or why it cannot be used | On demand: resolves the credential each time, so a folder created or a variable set since is seen | One answer per call |
| `SessionOpened.Connection` | Field of an event | The `ConnectionName` the session opened on | When a session opens; fixed for the session | One per session |
| `OpenedSession.Connection` | Field of a command answer | The `ConnectionName` the session opened on | With every `IAgents.OpenAsync` that succeeds | One per session |
| `JobRequest.Connection` | Field of a command | `Option<ConnectionName>`, the connection the job asks for; none takes the repository's default from `.avala/jobs.json`, then capacity, then the machine's | At submission; checked then, a rejection being `UnknownConnection` or `UnusableConnection` | One per job |
| `ConnectionChosen` | Event | `Job`, and `Choice`: a `ConnectionChoice` with `Connection`, `Reason` (`ChoiceReason`: `MostCapacity`, `AllAtLimit`), `At`, and `Compared`, each candidate a `CandidateCapacity` with `Connection`, `Used`, the most used counting `Window` (`Option<UsageLimit>`), `Threshold` and `Available` | When a job that names no connection, and whose repository names none, launches with a selector registered, before its session opens | Zero or one per launch |
| The job's connection | Stored with the job | The `ConnectionName` its first session opened on, or the one `IJobs.ContinueOnAsync` moved it to | Fixed when the job starts; reused by recovery and continuation; changed only by `ContinueOnAsync` | One per job |
| `IUsage.ByConnection()` | Query | `IReadOnlyList<ConnectionUsage>`: `Connection`, its `Provider` and its `UsageSummary`, limits included, ordered by connection name | Any time, from memory, earlier runs restored at startup | One per connection that ever opened a session |

The job's connection is not part of an event of Jobs: a view reads it from `IJobCatalog`, or finds it through the `SessionOpened` of the job's sessions, which `JobSessionStarted` ties to the job.

### Workspaces: base files

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `WorkspaceInfo.BaseCommit` | Field of `IWorkspaces` answers | The full SHA of the commit the worktree was created from | Fixed when the workspace is prepared; stored with it | One per workspace |
| `IBaseFiles.ReadAsync(worktree, path)` | Query | `Result<BaseFile, WorkspaceFailure>`: `Path`, `Origin` (`FileOrigin`: `Commit` and `EditedInWorktree`) and `Content`, an `Option<string>` absent when the commit holds no file there; `UnknownWorkspace` for a folder that is no worktree, `GitFailed` when git fails | On demand, at most three git commands each time | One answer per call |
| `IWorkspaces.FindAtAsync(folder)` | Query | `Result<WorkspaceInfo, WorkspaceFailure>`: the workspace whose worktree is the folder, or `UnknownWorkspace` | On demand | One answer per call |
| `IWorkspaces.ReconcileAsync()`, `CleanAsync(found)` | Query and command | `WorktreeReconciliation`: `Strays`, folders under the worktree root no workspace knows, in path order, and `Missing`, the workspaces whose folder is gone; cleaning answers what it cleaned | On demand; Resources reconciles at startup | One answer per call |

`FileOrigin` is the provenance every rule file reports: Verification in `VerificationReport.Declaration`, Permissions in `SessionPolicy.Origin` and Budgets in `SessionBudget.Origin`.

### Jobs: holds

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `JobHeld` | Event | `Hold`: a `JobHold` with `Job`, `Session`, `Reason` (`HoldReason`: `Stalled`, `SessionLost`, `BudgetExceeded`, `LimitNearlyReached`, `InvalidBudget`, `MemoryExceeded`, `Interrupted`, `Stopped`) and `Halt` (`SessionHalt`: `Interrupted`, `Idle`, `Stopped`, `AlreadyClosed`) | Each time a module holds a running job, or Jobs holds one whose session ended on its own, right after the job's `JobProgressed` with `NeedsHelp` and once its session was halted | Zero or one per run of a job: a held job runs again only after a human hint |
| `JobResumable` | Event | `Job`, `Session` whose resume token Jobs stored | Each time the job's current session issues a resume token, once the token is stored. Never for a session the job no longer uses | Zero or more per session; the simulator issues one per turn |
| `IJobs.ContinueAsync(JobId, message)` | Command answer | `Result<JobContinuation, JobRejection>`: `Job`, the `Session` the job continues in and `Conversation` (`ContinuedIn`: `SameSession`, `ResumedConversation`, `NewConversation`); or `NotHeld`, `EmptyMessage`, `UnknownJob`, `WorkspaceUnavailable`, `UnknownConnection`, `UnusableConnection`, `AgentUnavailable` | When a human answers a job that needs help. A success is followed by `JobProgressed` with `Running`, and by `JobSessionStarted` when the session is new | One per human answer |
| `IJobs.ContinueOnAsync(JobId, ConnectionName, message)` | Command answer | `Result<JobContinuation, JobRejection>`: `Job`, the new `Session` and `NewConversation`; or `NotHeld`, `SameConnection`, `EmptyMessage`, `UnknownJob`, `WorkspaceUnavailable`, `UnknownConnection`, `UnusableConnection`, `AgentUnavailable` | When a person moves a held job to another connection. A success is followed by `JobProgressed` with `Running` and `JobSessionStarted`, and the old session's end | One per move |

The other events of Jobs, `JobSubmitted`, `JobProgressed` and `JobSessionStarted`, predate this catalog and keep their shapes. The resume token itself stays inside Jobs: it is the provider's opaque text and means nothing to a view.

### Agents: session ends

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `SessionEnded` | Event | `Session`, `Ending` (`SessionEnding`: `Closed` or `Crashed`) | When a session's event stream ends on its own, before the `TurnFinished` of the turn it left live, if any. Never when the harness stops the session | Zero or one per session |
| `SessionOpened.Account` | Field of an event | `Option<AgentAccount>`: `Id`, opaque to the harness, and `Label` for people | When a session opens; fixed for the session | One per session |
| `SessionResumable` | Event | `Session`, `Token`: the `ResumeToken` the provider issued | Right after the `AgentActivity` of the `ResumeTokenIssued` it reports, only for a provider that declares `CanResume` | Zero or more per session, as the provider issues them |
| `AgentActivity` of `ResumeTokenIssued` | Event | `Session`, `Turn`, `Token` | Inside a turn, when the provider issues a token | As above |

### Observability: usage recorded

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `UsageRecorded` | Event | `Session`, `Option<JobId>` of its job | After every `UsageReported` and `LimitReported` the tracker recorded, once `IUsage` includes it | One per usage or limit report |
| `IUsage.ByAccount()` | Query | `IReadOnlyList<AccountUsage>`: `Provider`, `Account` and its `UsageSummary`, ordered by provider then account identifier | Any time, from memory, earlier runs restored at startup | One per provider and account that ever reported usage; sessions without an account are left out |
| `IUsageHistory.WithinAsync(from, to)` | Query | `UsagePeriod`: `From`, `To`, `Usage` (a `UsageSummary`), `ByProvider` and `ByConnection`, of the facts recorded from `from`, included, to `to`, excluded | On demand, from `observability.db` | One answer per call |
| `IUsageHistory.DailyAsync(first, last, zone)` | Query | `IReadOnlyList<UsagePeriod>`, one per calendar day of `zone` from `first` to `last`, each from local midnight to the next, empty days included | On demand, from `observability.db` | One per day asked |
| `observability.db` | File in the data folder | `UsageSessions`: the session, its provider's identifier and name, its account's identifier and label, its connection and its job, empty text or an empty identifier when unknown; `UsageFacts`: the session, the time in UTC ticks, the kind (`Usage`, `Limit` or `Turn`), the five token counts, the cost's amount and currency, the limit's window, fraction used and reset time (`-1` when none), and the turn's outcome and duration in ticks | One session row when a session opens, updated when its job is known; one fact per usage report, limit reading and counted turn | Grows with the history; never compacted yet |
### Supervision

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `SilenceNoticed` | Event | `Job` | When the silence alarm of a running job rings. It is how the alarm reaches the watchdog's mailbox, which confirms it after every event published before it, so it does not mean the job was held | At most one pending per job; a job active for a long run gets one each time its window elapses without its last activity having moved |
| `SupervisorIntervened` | Event | `Intervention`: a `SupervisionIntervention` | After a hold succeeded, following the hold's `JobProgressed` and `JobHeld` | One per intervention |
| `ISupervision.OfJob(JobId)` | Query | `IReadOnlyList<SupervisionIntervention>` in the order they happened; empty for an unknown job | Any time, from memory; earlier runs restored at startup from `supervision.db` | Zero or more per job |
| `supervision.db` | File in the data folder | `Interventions`: the job, the session, the hold reason and halt, the silence measured and the window in ticks, and the time in UTC ticks | One row per intervention, written before `SupervisorIntervened` | Zero or more per job |
| `ISupervision.SettingsAsync` | Query | `SupervisionSettings`: `Silence` window, `File` (`SettingsFileStatus`: `Absent`, `Applied` or `Rejected`) and `Option<SupervisionError>` | Any time; reads the settings file the first time | One per application |
| `ISupervision.ChangeSilenceAsync(silence)` | Command answer | `Result<SupervisionSettings, SupervisionError>`: the settings now applied, or `InvalidSilence`, `Unwritable` | When a person changes the window on the settings page; writes `supervision.json` | One per change |

`SupervisionIntervention` carries `Hold`, the `JobHold` Jobs returned, always `Stalled`; `Silence`, a `SilenceMeasure` with the measured `Silent` time and the `Window`; and `At`, from `TimeProvider`. A lost session is not an intervention of Supervision: it shows as the `JobHeld` with `SessionLost` that Jobs publishes.

### Budgets

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `BudgetLoaded` | Event | `Budget`: a `SessionBudget` with `Session`, `File` (`BudgetFileStatus`: `Absent`, `Applied` or `Rejected`), `Option<BudgetError>`, `Caps` and `Origin`, the `Option<FileOrigin>` of the budget file, as for the policy | When a session opens, after its budget file was read and before the session is tied to its job | One per session |
| `BudgetIntervened` | Event | `Intervention`: a `BudgetIntervention` | After a hold succeeded, following the hold's `JobProgressed` and `JobHeld` | One per intervention |
| `IBudgets.BudgetOf(SessionId)` | Query | `Option<SessionBudget>`; none until the session opened | Any time | One per session |
| `IBudgets.OfJob(JobId)` | Query | `IReadOnlyList<BudgetIntervention>` in the order they happened | Any time, from memory; earlier runs restored at startup from `budgets.db` | Zero or more per job |
| `budgets.db` | File in the data folder | `Interventions`: the job, the session, the hold reason and halt, the breach's measure, subject, measured value, cap and error (empty when none), and the time in UTC ticks; `Carves`: the parent, the child, the carved costs as a JSON list of currency and amount, the carved tokens (`-1` when uncapped), the share and the time in UTC ticks | One row per intervention, written before `BudgetIntervened`; one row per child job, written before `BudgetCarved` | Zero or more per job |

`BudgetCaps` has `CostPerJob`, a list of `Cost` caps, one per currency; `TokensPerJob`, an `Option<long>`; `HoldAtLimit`, an `Option<double>`; and `MemoryPerJobMegabytes`, an `Option<long>`. A session's `Caps` are those of its connection: the file's `connections` section for it when there is one, the top-level caps otherwise. `BudgetIntervention` carries `Hold`, the `JobHold`; `Breach`; and `At`. A `BudgetBreach` states the measured facts: `Measure` (`Cost`, `Tokens`, `Limit`, `Declaration` or `Memory`), `Subject` (the currency, `tokens`, the limit window, the budget file or `megabytes`), `Measured` and `Cap` as decimals (spent against cap, the limit fraction used against the threshold, or megabytes used, to one decimal, against the cap; both 0 for a declaration) and `Error`, the `Option<BudgetError>` of an invalid declaration.

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `JobQueued` | Event | `Job`, `Running` (the slots taken when it asked), `Limit` | When a submitted job finds every slot taken, before it waits | Zero or one per job |
| `JobAdmitted` | Event | `Job` | When a queued job gets its slot, before it launches | One per `JobQueued` |
| `IBudgets.MachineAsync()` | Query | `MachineBudget`: `RunningJobs` (`Option<int>`, one when the file is rejected), `File` (`BudgetFileStatus`) and `Option<BudgetError>` | Any time; reads `budgets.json` the first time | One per application |
| `IRepositoryBudgets.OfRepositoryAsync(repository)` | Query | `RepositoryBudget`: `File`, `Option<BudgetError>`, the top-level `Caps`, `Connections` (each a `ConnectionCaps` with `Connection` and `Caps`, in name order) and the `Option<FileOrigin>` of the repository's current commit; no caps when absent or rejected | On demand, through `IBaseFiles.ReadCurrentAsync` | One answer per call |

### Canvas

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `CanvasFormat` | Declaration, a service | `MediaType`, `Name` for people and the model, `Guidance` for the model | Registered by a renderer plugin's core registration at startup; the Rendering plugin declares `image/svg+xml` (SVG) then `text/markdown` (Markdown) | One per offered media type |
| The `canvas` harness tool | Declaration, a `HarnessTool` | Its schema's `mediaType` is an `enum` of every declared `MediaType` in registration order; its description lists them with their names and guidance | Built once, from the declared formats | One per application |
| `CanvasUpdated` | Integration event | `CanvasSnapshot`: `Canvas` (`CanvasId`), `Session`, `Title`, `MediaType`, `Content` (the full content so far), `Status` (`CanvasStatus`) and `IsOffered`, false for a media type the tool did not offer | When a canvas starts, at most once per 100 ms while it streams, and at once when it closes | One or more per canvas |
| `ICanvases.InSession(SessionId)` | Query | The current `CanvasSnapshot` of each canvas of the session, in start order | Any time, from memory | Zero or more per session |

### SDK: opening a file

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `IFileOpener.OpenAsync(path)` | Command answer | `Result<string, FileOpenError>`: the path opened, or `NotFound`, `Unavailable`, `Refused` | When a person asks to edit a rule file or a machine file | One per request |

### Jobs: discard

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `IJobs.DiscardAsync(JobId)` | Command answer | `Result<JobId, JobRejection>`: the job discarded, or `NotDiscardable`, `UnknownJob` | When a human discards a job. A success is followed by `SessionStopped` when the job had a session, then `JobProgressed` with `Discarded` | One per discard |

### Jobs: review and catalog

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `IJobCatalog.ListAsync()` | Query | `IReadOnlyList<JobSummary>`: `Job`, `Repository`, `Instruction`, `Submitted`, `Status` (`JobStatus`), `Option<ConnectionName>` `Connection`, `Option<Autonomy>` `Autonomy`, `Option<WorkspaceId>` `Workspace` and `Option<JobId>` `Parent`, in submission order | On demand, from a snapshot of `jobs.db` | One per job ever submitted |
| `IJobCatalog.HistoryAsync(JobId)` | Query | `Option<JobHistory>`: `Summary`; `Sessions`, each a `SessionRecord` with `Session` and the numbers of its `Attempts`, in the order the sessions started; `Attempts`, each an `AttemptRecord` with `Number`, `Origin` (`AttemptOrigin`: `Initial`, `Retry`, `Hint`, `SendBack`, `Recovery`), `Outcome` (`AttemptOutcome`: `Running`, `AwaitingCheck`, `Passed`, `Rejected`, `Interrupted`), `Option<string>` `Guidance` and `Option<SessionId>` `Session`; none for an unknown job | On demand, from a snapshot of `jobs.db` | One per job |
| `IJobs.ApproveAsync(JobId)` | Command answer | `Result<JobApproval, JobRejection>`: `Job` and `Delivery`, an `ApprovalDelivery` with `Strategy`, `Branch` and `Option<string>` `Commit`; or `NotAwaitingReview`, `UnknownJob`, `WorkspaceUnavailable`, `InvalidJobFile`, `UnknownApprovalStrategy`, `NoBaseBranch`, `MergeConflict`, `BaseCheckoutDirty`, `BaseMoved`, `DeliveryFailed`, or any rejection of a plugin's strategy | When a human approves. A success is followed by `SessionStopped` when the session was open, `JobProgressed` with `Approved`, then `JobApproved` | One per approval attempt |
| `JobApproved` | Event | `Approval`: the `JobApproval` | Once the approved job is stored and announced | Zero or one per job |
| `IJobs.SendBackAsync(JobId, feedback)` | Command answer | `Result<JobContinuation, JobRejection>`, as `ContinueAsync`; or `NotAwaitingReview`, `EmptyMessage`, `UnknownJob`, `WorkspaceUnavailable`, `UnknownConnection`, `UnusableConnection`, `AgentUnavailable` | When a human sends a job back. A success is followed by `JobProgressed` with `Running`, and by `JobSessionStarted` when the session is new | One per round asked for |
| `.avala/jobs.json` `approval` | Field of a rule file | The name of the approval strategy, `keep` by default; the file may also hold an `autopilot` object, see [Autopilot](#the-rules-file) | Read from the base commit at each approval | One per repository |

### Workspaces: changes

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `WorkspaceInfo.BaseBranch` | Field of `IWorkspaces` answers | `Option<string>`: the branch the base reference named when the workspace was prepared; none for a detached `HEAD` | Fixed when the workspace is prepared; stored with it | One per workspace |
| `IWorkspaceChanges.DiffAsync(WorkspaceId)` | Query | `Result<WorkspaceDiff, WorkspaceFailure>`: `Workspace`, `BaseCommit`, `Head` (the branch's latest checkpoint) and `Files`, each a `FileChange` with `Path`, `Kind` (`Added`, `Modified`, `Deleted`), and `Option<int>` `Added` and `Removed`, absent for a binary file, in path order | On demand, two git commands | One answer per call |
| `IWorkspaceChanges.FileDiffAsync(WorkspaceId, path)` | Query | `Result<FileDiff, WorkspaceFailure>`: `Path`, `Binary` and `Hunks`, each a `DiffHunk` with `OldStart`, `OldLines`, `NewStart`, `NewLines`, `Section` and `Lines`, each a `DiffLine` with `Kind` (`Context`, `Added`, `Removed`) and `Text`; `FileUnchanged` for a file the job did not change | On demand, one git command | One answer per call |
| `IWorkspaceChanges.ConflictsAsync(WorkspaceId)` | Query | `Result<IReadOnlyList<string>, WorkspaceFailure>`: the files that conflict with the base branch's current tip, empty when it merges cleanly; `NoBaseBranch` without one | On demand | One answer per call |
| `IWorkspaceChanges.MergeAsync(WorkspaceId, message)` | Command answer | `Result<MergedWork, WorkspaceFailure>`: `Workspace`, `BaseBranch`, `Option<string>` `Commit` (none when the base already held the work) and `Option<string>` `Checkout` updated; or `NoBaseBranch`, `MergeConflict`, `BaseCheckoutDirty`, `BaseMoved`, `UnknownWorkspace`, `GitFailed` | When the `merge` strategy delivers | One per approval |

### Runtime: startup

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `StartupCompleted` | Event, in the SDK | No field | Once every startup task ran, recovery and the restores of stored history included | One per start of the application |

### Agents: process trees

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `SessionOpened.ProcessTree` | Field of an event | `Option<ProcessTreeId>`: the tree the session's processes run in, from the SDK | When a session opens; fixed for the session | One per session |
| `SessionStopped` | Event | `Session` | When the harness stopped a session, once the provider's session is disposed: a hold that stops it, a job that ended, or shutdown, when the bus has already stopped | Zero or one per session |

### Resources

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `ResourcesSampled` | Event | `Sample`: a `ResourceSample` with `At`; `Trees`, each a `TreeUsage` with `Tree`, `Home` (the folder it opened for), `Processes`, `CpuLoad` and its `Option` `Session`, `Job`, `Connection` and `Provider`; `Worktrees`, each a `FolderUsage` with `Path`, `Bytes` and `Option<JobId>` `Job`; and `DataFolderBytes` | Every `sampleSeconds`, once the sample is kept; the disk figures are measured every `diskSeconds` and repeated between | One per interval for the life of the application |
| `ProcessUsage` | Part of a sample or a report | `Id`, `Name`, `MemoryBytes` (working set), `CpuTime` (since the process started), `Ports` it listens on | With every sample and orphan report | One per live process of a tree |
| `IResources.Latest`, `Global()`, `OfJob(JobId)`, `OfSession(SessionId)`, `ByConnection()`, `ByProvider()` | Query | The latest sample, or a `ResourceUsage`: `Processes`, `MemoryBytes`, `CpuTime`, `CpuLoad`, `Ports`, `DiskBytes`; by connection and provider in name order | Any time, from the latest sample; nothing before the first one | One answer per call |
| `OrphansFound` | Event | `Report`: an `OrphanReport` with `Tree`, `Processes`, `Disposal` (`Killed` or `LeftRunning`), `Survivors` (the identifiers still alive after a kill), `At`, and its `Option` `Session` and `Job` | When a session ends with processes still alive in its tree, after they were killed by policy | Zero or one per session |
| `OrphansReaped` | Event | `Report`, as above, `Killed` | When `IOrphans.ReapAsync` kills orphans left running | Zero or one per tree left running |
| `IOrphans.Audit()`, `OfJob(JobId)`, `ReapAsync(JobId)` | Query and command | The reports in the order they were made; the reaped reports or `NothingToReap` | Any time | Zero or more per job |
| `PortsLeased`, `PortsReleased` | Events | `Lease`: a `PortLease` with `Worktree`, `First`, `Last` | When a worktree's first tree opens; when its job ends | One of each per worktree |
| `PortConflictObserved` | Event | `Conflict`: a `PortConflict` with `Port`, the `Lease` it belongs to, `At` and the `Option` `Process` and `Tree` that hold it | When a sample first finds a leased port held outside its worktree | Once per port, lease and holder |
| `IResources.Leases()`, `Conflicts()` | Query | The current leases; every conflict observed | Any time | Zero or more |
| `WorktreeReclaimed` | Event | `Reclaimed`: a `ReclaimedWorktree` with `Job`, `Path`, the job's final `Status` and `At` | When retention removed an ended job's worktree | Zero or one per worktree |
| `WorktreesReconciled` | Event | `Found`: a `WorktreeReconciliation` with `Strays` (folders) and `Missing` (`WorkspaceInfo`s), and `Cleaned` | At startup, and on every reconciliation or cleaning command | One per startup and command |
| `IResources.SettingsAsync()` | Query | `ResourceSettings`: `Sampling`, `DiskSampling`, `Orphans` (`OrphanPolicy`), `Ports` (`PortRange`), `Retention` (`WorktreeRetention`, an `Option<TimeSpan>` per final status) and `Reconcile`, with `File` and `Option<ResourceError>` | Any time; reads `resources.json` the first time | One per application |

### Recording

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `recordings/<yyyyMMddTHHmmssZ>-<session>.json` | File in the data folder | A recording in the [format](#format) `avala-recording` version 1 | Only when `recording.json` enables recording. Written whole when a turn completes, the stream ends, the session stops and the application shuts down | One per session started while recording is on, kept until someone deletes it |
| `Replay diverged` | Item of a replayed turn | `ItemStarted` of kind `Other` with the item `replay-divergence`, one `ItemProgressed` with the reason, `ItemCompleted` as `Failed`, then `TurnCompleted` as `Failed` | When a replayed session meets an input or a turn its recording does not hold | At most one per replayed session, which closes after it |

The module publishes no event and answers no query: a recording is a file for people and tests, and a replay is an ordinary simulated session.

### Agents: executed tools

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `AgentActivity` of `ToolCalled` | Event | `Session`, `Turn`, `Item`, `Tool` (the tool's name) and `Input`, the call's arguments as JSON text | When a provider that declares `AcceptsTools` calls a tool of the `Executed` surface it was given, once the `Turn` aggregate opened its item | Zero or more per turn |
| `AgentActivity` of `ToolReturned` | Event | `Session`, `Turn`, `Item`, `Result`: a `ToolResult` with `Item`, `Content` and `IsError` | When the provider reports the result it received; `ItemCompleted` then closes the call | One per answered call |
| `IAgents.ReturnAsync(session, ToolResult)` | Command answer | `Result<ItemId, AgentError>`: the item answered, or `SessionClosed`, `Unsupported`, `NoPendingCall` | When the module that offers the tool answers its call | One per call |

### Workspaces: current files

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `IBaseFiles.ReadCurrentAsync(repository, path)` | Query | `Result<BaseFile, WorkspaceFailure>`: the file of the commit the repository's `HEAD` points at, its `Origin` with that commit and whether the checkout's copy differs, and its `Content`; `NotAGitRepository` outside a repository, `GitFailed` when git fails | On demand, at most five git commands | One answer per call |

### Autopilot

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `IAutopilot.StartAsync(LoopRequest)` | Command answer | `Result<LoopId, AutopilotError>`: the new loop, or `EmptyRepository`, `InvalidLimits`, `AlreadyRunning`. `LoopRequest` has `Repository`, `Option<ConnectionName>` `Connection`, `Option<Autonomy>` `Autonomy`, `AttemptsPerRound` and `Limits`, a `LoopLimits` with `SpendPerLoop` and `SpendPerWindow` (lists of `Cost` caps), `Window`, `FailuresInARow`, `SameFailure`, `NothingChanged`, `Iterations` and `PauseAtLimit` | When a person starts a loop | One per loop |
| `IAutopilot.PauseAsync`, `ResumeAsync`, `StopAsync(LoopId)` | Command answers | `Result<LoopId, AutopilotError>`: the loop, or `UnknownLoop`, `NotRunning`, `NotPaused`, `LoopEnded` | When a person pauses, resumes or stops a loop | One per command |
| `IAutopilot.Loops()` | Query | `IReadOnlyList<LoopState>`: `Loop`, `Repository`, `Status` (`LoopStatus`: `Running`, `Waiting`, `Paused`, `Ended`), `Started`, `Iterations`, the `Option<JobId>` `Current` underway, and the options `Until`, `Pause` (`PauseReason`: `Command`, `UsageLimit`), `Ending` (`LoopEnding`: `Drained`, `Stopped`, `BreakerTripped`, `SourceFailed`), `Breaker` and `Error` | Any time, from memory, in start order | One per loop started since the application started |
| `IAutopilot.DigestOf(LoopId)` | Query | `Option<LoopDigest>`: `State`, `Iterations` (each an `IterationRecord`: `Number`, `Task`, `Option<JobId>` `Job`, `Outcome`, `Ended`, `Exceptions`, `Option<HoldReason>` `Hold`, `Option<JobRejection>` `Rejection`, `Option<FailureSignature>` `Failure` with its `Source` and `Detail`, `ChangedNothing` and `Cost`), `Approvals` (each an `AutoApproval`), `Breakers` (each a `BreakerTrip`), `Pauses` (each a `LoopPause`) and `Spent` per currency | Any time, from memory | One per loop |
| `IRunEvidence.OfJobAsync(JobId)` | Query | `Option<RunEvidence>`: `Job`, `Summary` (an `EvidenceSummary`), `Exceptions` of the run (`VerificationNotPassed`, `Denial`, `Assumption`, `RuleFileEdited`, `Held`, `ChangesUnknown`), `Verifications`, `Denials` (`PolicyDecision`s), `DeniedAnswers` (`HumanAnswer`s), `Declined` and `Assumed` (`FormDecision`s), `RuleFiles` (paths), `Holds` (the `Hint` attempts) and `AllowedByRules`; none for a job the catalog does not know | On demand, from the in-memory audits, the catalog and one diff | One answer per call, for any job |
| `LoopStarted`, `LoopEnded` | Events | `State`: the `LoopState` | When a loop starts; when it drains, is stopped, trips a breaker or meets a source it cannot read | One of each per loop |
| `LoopTaskTaken` | Event | `Loop`, `Iteration`, `Task` (a `SourcedTask`: `Source`, `Key`, `Repository`, `Instruction`) and the `Job` submitted for it | Once the job is submitted and the task marked taken | One per task taken |
| `AutoApprovalDecided` | Event | `Decision`: an `AutoApproval` with `Loop`, `Job`, `Approved`, `Evidence` (an `EvidenceSummary`: `Attempts`, `Option<VerificationOutcome>` `Verification`, `ChecksPassed`, `PermissionsAllowed`, `FormsDecided`, `FilesChanged`), `Exceptions` (each an `ExceptionReason`), `At`, and the options `Delivery` (an `ApprovalDelivery`) and `Refusal` (a `JobRejection`) | When a loop's job reaches `AwaitingReview`, after the approval was delivered or refused | One per review of a loop's job |
| `LoopIterated` | Event | `Loop`, `Iteration`: the `IterationRecord` | When the iteration's job settled, after the task was marked | One per task taken or refused |
| `LoopWaiting` | Event | `Loop`, `Until` | When nothing is to do until a recurring task is due | Zero or more per loop |
| `LoopPaused`, `LoopResumed` | Events | `Loop` and a `LoopPause` with `Reason`, `At`, `Option<DateTimeOffset>` `Until` and `Option<string>` `Window`; `Loop` and `At` | On a pause command or a limit window at its threshold; on a resume command or the window's reset, once a held job was continued | Zero or more per loop |
| `BreakerTripped` | Event | `Loop`, `Trip`: a `BreakerTrip` with `Breaker`, `Subject` (a currency, `iterations`, `failures`, a failure signature or a limit window), `Measured`, `Cap` and `At` | Right before the `LoopEnded` it causes | Zero or one per loop |
| `FollowUpDecided` | Event | `Decision`: a `FollowUpDecision` with `Session`, `Option<JobId>` `Job`, `Instruction`, `At`, and the `Option<SourcedTask>` `Task` queued or the `Option<FollowUpRefusal>` `Refusal` (`MalformedInput`, `NoJob`, `NotAutonomous`, `UnreadableRules`, `NotAllowed`) | When an agent calls `propose_follow_up`, once it was answered | One per call |
| `.avala/backlog.json` | Rule file | `tasks` and `recurring`, see [Job sources](#job-sources) | Read from the repository's current base each time a loop asks for a task | One per repository |
| `.avala/jobs.json` `autopilot` | Field of a rule file | `approve` and `followUps` | Read from the base commit of each job judged and of each job that proposes a follow-up | One per repository |
| `autopilot.db` | File in the data folder | `Tasks`: the repository, the source, the task's key and instruction, its `TaskState` (`Proposed`, `Taken`, `Approved`, `WaitingForPerson`, `Failed`), its job (an empty identifier when none), when it was last taken (`-1` when never) and when it last changed, in UTC ticks | One row per task a loop took and per follow-up proposed, updated by every mark | Grows with the backlogs; never compacted yet |

### Jobs: parent and child jobs

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `JobRequest.Parent` | Field of a command | `Option<JobId>`, the job a new job is a child of; refused with `UnknownParent`, `ParentNotRunning`, or `InvalidRequest` for another repository | At submission | One per job |
| `JobSubmitted.Parent` | Field of an event | `Option<JobId>`, the job's parent | With every `JobSubmitted` | One per job |
| `JobSummary.Parent` | Field of a query answer | `Option<JobId>` | With every summary of `IJobCatalog` | One per job |
| `IJobCatalog.ChildrenAsync(JobId)` | Query | `IReadOnlyList<JobSummary>`: the job's children in submission order, with their status, connection, autonomy and workspace | On demand, from a snapshot of `jobs.db` | Zero or more per job |
| `IJobCatalog.TreeAsync(JobId)` | Query | `Option<JobTree>`: `Job`, the `JobSummary`, and `Children`, a `JobTree` each, in submission order; none for an unknown job | On demand, from a snapshot of `jobs.db` | One per job; a view of a delegation tree reads it from the root |
| `IJobs.ApproveAsync(JobId)` of a child | Command answer | As for any job, delivered by the `merge` strategy into the parent's branch and worktree; or `ParentNotRunning`, `UnknownParent`, `MergeConflict`, `BaseCheckoutDirty`, `BaseMoved` | When Delegation integrates a verified child, or a person approves one | One per approval attempt |
| `jobs.db` `Parent` | Column of `Jobs` | The parent's identifier, an empty identifier for a root job | Written with the job | One per job |

A view follows each child's status through `JobProgressed`, its connection through the summary or `ChildDelegated`, and what it spent against its carve through `IUsage.OfJob` and `IBudgets.CarveOf`, updated by `UsageRecorded`.

### Workspaces: rules commit

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `WorkspaceRequest.Rules` | Field of a command | `Option<string>`: a reference to the commit the workspace reads its rule files from; none for its base commit. A reference that does not resolve is `GitFailed` | When a workspace is prepared; Jobs names the parent's rules commit for a child | One per workspace |
| `WorkspaceInfo.RulesCommit` | Field of `IWorkspaces` answers | The full SHA of the commit `IBaseFiles.ReadAsync` reads from and `FileOrigin.Commit` names; the base commit unless the request named another | Fixed when the workspace is prepared; stored as the `Rules` column of `workspaces.db` | One per workspace |

### Budgets: carves

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `BudgetCarved` | Event | `Carve`: a `BudgetCarve` with `Parent`, `Child`, `Cost` (one `Cost` per currency the parent caps), `Option<long>` `Tokens`, `Share` and `At` | When a child job was submitted and its parent's budget is known, once its carve is stored | One per child job |
| `IBudgets.CarveOf(JobId)` | Query | `Option<BudgetCarve>` of a child; none for a root job | Any time; earlier runs restored at startup from `budgets.db` | One per child job |
| `BudgetCaps.CarvePerChild` | Field of a session's caps | `Option<double>`, the share each child is carved, from `carvePerChild` | With `BudgetLoaded` | One per session |

### Delegation

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `ChildDelegated` | Event | `Delegation`: a `DelegationRecord` with `Session` and `Item`, the call; `Instruction`; `At`; `Option<JobId>` `Parent`; `Depth`, the child's; `Option<JobId>` `Child`; `Option<ConnectionName>` `Connection`, the routed one, none when the section lists no connections, and in `ChildReported` the one the child ran on; `Option<ConnectionChoice>` `Choice`, the readings compared when it was routed by capacity; `Option<Autonomy>` `Autonomy`; and the options `Refusal` (`DelegationError`), `Rejection` (`JobRejection`) and `Report` (`ChildReport`), none yet | When a call's child job is submitted | One per child job |
| `DelegationRefused` | Event | `Delegation`, with its `Refusal` and, for `NotSubmitted`, the `Rejection` | When a call is refused, once its error result was returned | Zero or more per job |
| `ChildReported` | Event | `Delegation`, with its `Report`: a `ChildReport` with `Child`, `Outcome` (`ChildOutcome`: `Integrated`, `Conflict`, `NotIntegrated`, `Held`, `RetriesExhausted`, `Failed`, `Discarded`), `Status`, `At`, `Option<string>` `Summary`, `Files` (each a `FileChange`), `Option<VerificationReport>` `Verification`, `Spent` per currency, `Tokens`, `Option<BudgetCarve>` `Carve`, `Option<ApprovalDelivery>` `Delivery`, `Conflicts`, `Option<HoldReason>` `Hold` and `Option<JobRejection>` `Refusal` | When a child settled and was integrated or not, once the result was returned to its parent's call | One per child job that settled while the delegation was known |
| `IDelegations.All()`, `OfParent(JobId)`, `OfChild(JobId)` | Query | The `DelegationRecord`s in the order the calls were made, the latest version of each | Any time, from memory | One per call since the application started |
| The `delegate` call's result | Tool result | JSON: `job`, `outcome`, `status`, `connection`, `autonomy`, `summary`, `files` (`path`, `change`, `added`, `removed`, `binary`), `verification` (`outcome`, `checks` with `name`, `status`, `exitCode`), `spent`, `tokens`, `carve`, `integrated` (`branch`, `commit`), `conflicts`, `hold`, `refusal`; or, as an error, `refused` and `reason` | When the child settles, or at once when refused | One per call |
| `.avala/jobs.json` `delegation` | Field of a rule file | `connections`, `routing`, `maxDepth`, `maxChildren`, see [Delegation](#the-rules-file-1) | Read from the rules commit of the caller's worktree at every call | One per repository |

### Interface: UI messages

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `JobSelected` | UI message, Workbench `Contracts.Presentation` | `Job`, the `JobId` chosen | When a person chooses a job in the sidebar, again if it is chosen again | One per choice |
| `PageRequested` | UI message, SDK `Presentation` | `Page`, the `IPage` to show | When a page asks the shell to show it, such as the jobs page after `JobSelected` | One per request |

## Delivery

**Accepted**

The application is built view model first: every screen is built and tested as view models with no user interface. Avalonia views come last, as a thin layer bound to view models that already work.

## Persistence

**Accepted**

- EF Core with the SQLite provider, with no server.
- One `DbContext` and one database file per module, under the data folder: `jobs.db`, `workspaces.db`, `observability.db`, `supervision.db`, `budgets.db` and `autopilot.db`. Separate files isolate modules for real, and each module creates its schema on its own. No module reads another module's data.
- The schema is created with `EnsureCreated`. The schema changes so far, the `Base`, `BaseBranch` and `Rules` columns of a workspace, the `Resume`, `Autonomy`, `Connection`, `Submitted` and `Parent` columns of a job, the `Session` column of its attempts and the `Carves` table of `budgets.db`, arrived before any release could create jobs, so they ship without a migration: a data folder created by an earlier build must be deleted, or its `jobs.db`, `workspaces.db` and `budgets.db` at least, since `EnsureCreated` adds neither a column nor a table to a database that exists. The new databases of Observability, Supervision and Budgets are created when missing. Migrations arrive with the first schema change after a release; with stored history now worth keeping, that release is the moment to start them.
- **Rows that are not aggregates.** A store of facts, such as usage facts and interventions, maps a plain row type in its `Storage` folder, converted to and from the module's records there, instead of an aggregate. Times are stored as UTC ticks, so SQLite compares and orders them as numbers.
- Stores are internal interfaces of each module's application layer, implemented in its `Storage` folder.
- EF Core is referenced only from the infrastructure layer, enforced by the layer rules. Inheriting from `DbContext` is allowed, like inheriting from Avalonia types.
- Connection pooling is off.

### Mapping without changing the domain

- EF Core rebuilds aggregates through their private constructors, private setters and collections backed by private fields.
- Identifiers and single-value objects use value converters.
- A value object with several fields, such as `WorkspaceLocation`, is stored as one JSON column.
- Owned collections, such as attempts and checkpoints, get a generated technical key that exists only in persistence.
- An absent `Option` is stored as a sentinel that can never be a real value, such as `Guid.Empty` or empty text, so the database holds no nulls.

### SQLite and blocking

SQLite has no asynchronous I/O. The asynchronous methods of its provider, such as `SaveChangesAsync`, run synchronously, and the banned API analyzer cannot see it because their signatures are asynchronous. Called from the UI thread, they freeze it.

Database work therefore never runs on the UI thread. Each store keeps one long-lived `DbContext` owned by a `SerialExecutor`, the SDK's channel consumer: every operation is queued there, runs alone and goes through `Task.Run`. No lock is involved, and the context is touched by one operation at a time.

Jobs run in parallel, each in its own queue, so the aggregate of one job may change in memory while the store saves another. Automatic change detection is therefore off: saving an aggregate detects the changes of that aggregate and its owned entities only, so a save never reads, nor stores, another job's half-made changes.

### Data folder

The host registers `AvalaPaths` from the SDK. Its data folder is `AVALA_DATA_PATH` when set, otherwise `Avala` under the local application data folder. It locates the database files, the worktree root, the settings files `supervision.json`, `recording.json`, `connections.json`, `resources.json` and `budgets.json`, the `connections` folder that keeps the login of each connection by default, and the `recordings` folder the recorder writes and the simulator replays from. The composition root receives it, so the host tests point it at a temporary folder.

### Startup tasks

Modules register `IStartupTask` implementations, such as `JobRecovery` and the books that restore stored history. The runtime runs them in `RuntimeHost.RunAsync`, one after the other, after the event bus has started, so the events they publish are dispatched, and then publishes `StartupCompleted` from the SDK. Work submitted meanwhile is not held back, so a startup task never assumes it runs before the first request.

## Architecture rules to add

**Accepted**

### Markers and layers

- The SDK defines marker interfaces: `IAggregateRoot`, `IDomainEvent` and `IIntegrationEvent`. They identify building blocks without base classes.
- Every module core is organized in folders named after what the code does, never after layers. The layer map of the architecture tests assigns each of its namespaces to `Domain`, `Application`, `Infrastructure` or `ViewModels`, and the rules below speak of those layers. See [the architecture](../architecture.md#screaming-architecture).

### Domain encapsulation

1. Aggregates expose no public or internal setters. State changes only through their operations.
2. Aggregates expose no mutable collection, only read-only views such as `IReadOnlyList<T>`.
3. Aggregate constructors are private. Aggregates are created through factory methods that return a `Result`.
4. Every public operation of an aggregate returns a `Result`. No `void` operation changes state.
5. Aggregates reference other aggregates by identifier only, never by object.
6. Every aggregate has an `Id` of a strongly typed identifier: a `readonly record struct` whose name ends in `Id`.

### Domain purity

7. `Domain` depends only on the base class library, the SDK, Stateless and identifiers from other modules' `Contracts`. Never on `Application`, `Infrastructure`, view models, Avalonia or dependency injection.
8. No `Domain` method returns `Task` or `ValueTask`. The domain performs no I/O.
9. `Domain` contains no `throw`. Expected failures travel in a `Result`.
10. Every `Result` in `Domain` uses its own module's error enum, and every module has exactly one.
11. Stateless is used only in `Domain`. `Fire` is never called directly: transitions go through a single helper that checks `CanFire` first, and the banned API analyzer rejects every other call.

### Value objects and events

12. Value objects are `readonly`.
13. Events are immutable: `init` only, never `set`.
14. Domain events live in `Domain`. Integration events live only in `Contracts`.

### Module boundaries

15. Layers inside a module, enforced with ArchUnitNET: `Application` depends on `Domain`, `Infrastructure` implements interfaces of `Application`, view models use `Application` and never `Infrastructure` or `Domain` directly.
16. `InternalsVisibleTo` targets only the module's own projects and its test project.
17. `Contracts` hold no logic: only interfaces, records, enums and structs.
18. No plugin references the host.
19. No provider name appears outside its own plugin. Added with the first provider, since it has nothing to check before.
20. Every module exposes exactly one plugin entry, in its core or its UI, so modules without UI, such as providers, fit.

### Rules that cannot pass vacuously

Every rule is tested against a fixtures assembly inside the architecture tests that violates it on purpose. If a rule stops detecting its fixture, its test fails. A rule therefore keeps working even while no production code exercises it.
