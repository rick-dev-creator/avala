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
| Jobs | Job lifecycle, attempts, attempt budget, the job flow coordinator, holding a job for a typed reason, including one whose session was lost, continuing a held job, the job's resume token, the autonomy a job asks for and the connection it runs on | `JobId`, `Autonomy`, integration events, `IJobs`, `ICompletionGate` |
| Agents | Sessions, turn integrity, provider registry, the connections sessions open on and their credential sources, the harness tools and resume tokens handed to providers by capability, the forms agents ask humans to fill, and the decorators every provider is started through | `IAgents`, `IAgentProvider`, `IAgentSession`, `IAgentProviderDecorator`, `IConnections`, `ICredentialSource`, `ConnectionName`, `ConnectionEnvironment`, `AgentEvent`, `AgentCapabilities`, `HarnessTool`, `ResumeToken`, `AgentAccount`, `AgentForm`, `FormAnswer`, integration events |
| Workspaces | Working copies, branches, checkpoints, and the files of the commit a job started from | `IWorkspaces`, `IBaseFiles`, integration events |
| Canvas | Offers the canvas tool, accumulates the canvases agents stream and publishes throttled snapshots | `CanvasId`, `CanvasUpdated`, `ICanvases` |
| Timeline | Read model of everything that happened in a job, for the activity view | Queries |
| Observability | Tokens, cost, limits and turns by provider, account, connection, session and job, and their metrics | `IUsage` and its summaries |
| Verification | Runs the checks a repository declares as a completion gate and keeps the evidence of every attempt | `AttemptVerified`, `VerificationReport`, `IVerifications` |
| Permissions | Answers permission requests and forms through an explicit policy at the job's level of autonomy, takes a human's answers with their session rules, and records why each decision was made | `PolicyLoaded`, `PermissionDecided`, `AutonomyApplied`, `FormDecided`, `PermissionAnswered`, `IPermissionAudit`, `IPermissionAnswers` |
| Supervision | Holds a job whose agent stays silent, and records every intervention | `SilenceNoticed`, `SupervisorIntervened`, `ISupervision` |
| Budgets | Holds a job that reaches a cap on cost or tokens, or a provider limit threshold, and records every intervention | `BudgetLoaded`, `BudgetIntervened`, `IBudgets` |
| Recording | Records every provider session, when the data folder asks for it, as a file the simulator replays | None: it implements `IAgentProviderDecorator`, and its files are the [recording format](#session-recording-and-replay) |

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

    public static Result<Job, JobError> Create(JobId id, Instruction instruction, AttemptBudget budget, RepositoryPath repository, Option<Autonomy> autonomy, Option<ConnectionName> connection);
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

Attempts have no state machine of their own. The job lifecycle already decides when an attempt starts, completes, passes or is rejected, so a second machine would be a second source of truth for the same facts. An attempt is an entity inside the `Job` aggregate, and only the job changes it.

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
}
```

- Activation starts an `await foreach`. Deactivation cancels the token and ends the subscription, so no subscription outlives its screen.
- Each subscription is a channel of its own: it receives its events in publishing order, and no handler can delay it. A subscriber therefore may see an event before the handlers of that event ran; one that needs a handler's result waits for the event that handler publishes afterwards.
- Events arrive off the UI thread. The SDK defines `IUiDispatcher` and the host implements it, so view models stay unaware of Avalonia.

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
| `JobLauncher` | Prepares the workspace, opens the agent session, starts the job, stores it, announces `JobSessionStarted` and only then sends the instruction. It also relaunches a job after a restart, and continues a held job when a human sends it a message, resuming the job's conversation when it can |
| `PrepareJob` | Handles `JobSubmitted` by queueing the launch of the job |
| `CheckTurn` | Handles `TurnFinished` by queueing the evaluation of the turn, `SessionEnded` by queueing a hold as `SessionLost`, and `SessionResumable` by queueing the record of the job's resume token, then returns at once |
| `EvaluateTurn` | Runs in the job's queue: checkpoints the workspace, evaluates the gates, then passes the job, retries with feedback to the same session, or asks for help when the budget is spent |
| `CompletionGates` | Combines every registered gate into one verdict |
| `JobRecovery` | An `IStartupTask` that launches `Preparing` jobs and recovers `Running` or `Checking` jobs, each in its queue |
| `HoldJob` | Behind `IJobs.HoldAsync`, in the job's queue: holds a running job for a typed reason, stores it, halts its agent session and publishes `JobHeld`, see [Holding a job](#holding-a-job) |

- **One queue per job.** Evaluating a turn runs real builds and tests through the gates and can take minutes, so it never runs inside a handler. `CheckTurn` only finds the job of the session and queues the work, so the bus keeps delivering while the checks run. Different jobs proceed in parallel; the work on one job runs one piece at a time, in the order it was queued: its launch, the evaluation of each turn and every hold. A hold that arrives while a turn of the same job is being checked waits for that check and then finds a job that is no longer `Running`.
- The job stores its session before the instruction is sent, so a fast agent cannot finish a turn the job does not know yet.
- A message is only ever sent to a `Running` job's session, so a job held between storing its session and sending the instruction is never told.
- A turn that ends interrupted or failed fails the job, unless the job was held first: a held job is no longer `Running`, so `EvaluateTurn` ignores the end of the turn the hold interrupted.
- **A lost session holds the job.** When the current session of a running job ends on its own, `SessionEnded` reaches `CheckTurn` before the failed `TurnFinished` that `AgentSessions` publishes after it, and both go to the job's queue in that order. The job is held as `SessionLost` and its session stopped, so the failed turn finds a held job and a human decides. `SessionEnded` of a session the job no longer uses is ignored.
- Handlers are idempotent. `EvaluateTurn` acts only on a job that is still `Running` in the session that finished, so a repeated `TurnFinished` changes nothing.
- Recovery opens a new session in the existing workspace and calls `job.Recover`, which interrupts the attempt that was underway and starts a `Recovery` attempt. It asks to resume the job's conversation with its stored resume token: when the session resumed it, the agent is told that the harness restarted and to continue where it left off; otherwise the new session starts over with the instruction, as before.
- **Resume tokens.** `SessionResumable` reaches `CheckTurn` in the same mailbox as `SessionEnded`, and both are queued on the job in that order, so a session that issues a token and then dies has its token stored before the job is held. The token is stored with the job and announced with `JobResumable`.
- **Connection.** `JobRequest.Connection` optionally names the [connection](#connections) a job runs on. `SubmitJob` checks a named connection through `IConnections.CheckAsync` and rejects the request with `UnknownConnection` or `UnusableConnection`, storing and announcing nothing. A job that names none takes its repository's default: `JobLauncher` reads `.avala/jobs.json` from the job's [base commit](#rules-from-the-base-commit) once the workspace exists, `{ "connection": "work" }`, through the `IRepositoryDefaults` port that `JobFileReader` implements in the `JobFiles` folder; without the file, or without the field, the session opens on the machine's default connection. The file is parsed strictly, at most 16 KiB with no other field, and a file that is invalid or cannot be read fails the job as `ConnectionUnavailable` before any session opens, as does a connection that turns out unknown or unusable when the session opens, since the repository's preference is only known after submission. `Job.Start` records the connection the session actually opened on, so the job keeps it even if the default changes later: it is stored with the job, and recovery and `IJobs.ContinueAsync` open their new session on it. A recovery whose connection is gone fails the job as `ConnectionUnavailable`; a continuation whose connection is gone is rejected with `UnknownConnection` or `UnusableConnection` and the job stays held.
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
}
```

- `Job.Hold(reason)` moves a `Running` job to `NeedsHelp` and concludes its underway attempt as `Interrupted`. No new state: a held job needs a human exactly like one whose retries ran out, and a hint resumes either. Any other state returns `CannotHold`, which `IJobs` reports as `NotRunning`; an unknown job is `UnknownJob`.
- `HoldReason` is `Stalled`, `SessionLost`, `BudgetExceeded`, `LimitNearlyReached` or `InvalidBudget`.
- `HoldJob` stores the held job first, which publishes `JobProgressed` with `NeedsHelp`, and only then halts the session, so the end of the interrupted turn finds a job that is no longer `Running`. It then publishes `JobHeld` with a `JobHold`: job, session, reason and how the session was halted.
- Halting: `SessionLost` stops the session, since it is already gone. Every other reason interrupts the turn through `IAgents.InterruptAsync` and keeps the session open for a human: `Interrupted` when a turn was interrupted, `Idle` when none was running. A provider that cannot interrupt, or an interruption that fails otherwise, stops the session instead: `Stopped`. A session that is no longer open is `AlreadyClosed`.
- The hold reason is not persisted: the stored job is `NeedsHelp` with an interrupted attempt, and the reason lives in `JobHeld` and in the audit of the module that held it. Persisting it arrives with the first schema migration.

**Continuing a held job.** `IJobs.ContinueAsync` is how a human answers a job that needs help, whatever held it: it hints the job with the human's message, in the job's queue, and returns a `JobContinuation` with the session the job continues in and how (`ContinuedIn`).

- `SameSession`: the job's session is still open, as after a `Stalled` or budget hold that interrupted the turn, or after the retries ran out. The message goes to that session.
- `ResumedConversation`: the session is gone, because it was lost or the application restarted since. A new session opens in the job's workspace with the job's resume token, and the provider resumed the conversation, so the message alone is sent.
- `NewConversation`: the session is gone and the conversation could not be resumed: the provider cannot resume, there is no token, or the provider rejected it. The new session starts over, and gets the instruction followed by the message.
- Jobs decides whether the session is open by asking `IAgents.IsOpen`, never by the hold reason, which is not stored. A new session is opened before the job changes, on the job's connection, so a job whose workspace is gone (`WorkspaceUnavailable`), whose connection is gone (`UnknownConnection`, `UnusableConnection`) or whose agent cannot start (`AgentUnavailable`) stays held. A job that is not `NeedsHelp` is `NotHeld`, an empty message `EmptyMessage`, an unknown job `UnknownJob`.

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

- `SessionOptions` holds harness concepts only: working directory, permission mode, the `Resume` token of a conversation to resume, the harness `Tools` the agent may call, and the `Connection` environment the session runs with, see [Connections](#connections). Paths and protocols belong to each provider's own settings, and how a provider applies a connection's configuration folder, key and settings is its own business.
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
- When a session's event stream ends on its own, `AgentSessions` publishes `SessionEnded` with `Crashed` when the stream failed and `Closed` when it completed. It publishes it before closing the turn left live, if any, as `Failed`, so a consumer learns the session is gone before it sees that turn fail. A stream that completes while a turn is live no longer leaves the turn open forever. Stopping a session through `StopAsync`, including at shutdown, publishes nothing, so a restart is never mistaken for a lost session and recovery still finds its jobs running.

### Agnostic events

**Accepted**

Every provider translates its protocol into one closed set of events. Each event carries its `SessionId` and `TurnId`.

| Event | Meaning |
| --- | --- |
| `TurnStarted`, `TurnCompleted` | A turn begins, and ends as finished, interrupted or failed |
| `ItemStarted` | Work begins: a message, reasoning, a file edit, a command, a search, a web request, an MCP call, a subagent |
| `CanvasStarted` | A canvas begins, with a title and a media type such as `text/html`, `image/svg+xml`, `text/vnd.mermaid` or `text/markdown` |
| `ItemProgressed` | More content for an open item or canvas, appended in order |
| `ItemCompleted` | An item or canvas ends as succeeded, failed, cancelled, abandoned or expired |
| `PermissionRequested`, `PermissionResolved` | An open item waits for a decision, and gets it |
| `FormRequested`, `FormAnswered` | A form opens an item that waits for a human's or the policy's answer, and gets it; `ItemCompleted` closes it, see [Human-input forms](#human-input-forms) |
| `PlanUpdated` | The agent's plan and the status of each step |
| `UsageReported` | Tokens used: input, output, cache reads, cache writes and reasoning, plus the cost when the provider reports it |
| `LimitReported` | A usage limit: its window, the fraction used and when it resets |
| `ResumeTokenIssued` | The opaque token that resumes this session's conversation from here, see [Resuming a conversation](#resuming-a-conversation) |

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

public enum ToolSurface { Canvas }
```

- **Shaped like MCP.** A tool is a name, a description for the model and its input as a JSON Schema in text, exactly what an MCP server lists. The real adapter transports the tools through MCP, as a server it gives its agent; the contract does not depend on it, and no MCP server exists yet.
- **Contributed, not known.** A module that offers a tool registers its `HarnessTool` in the container. `SessionStarter` gives every registered tool to providers that declare `AcceptsTools`, and none to the others. Agents never knows which module offered a tool.
- **The surface says how a call is reported.** The adapter knows its own protocol, so it translates a call of an injected tool into agnostic events; the surface of the tool tells it which ones. `Canvas`: a call is a canvas item whose identifier is the call's. `CanvasStarted` opens it with the call's `title` and `mediaType`, `ItemProgressed` streams its `content`, in chunks when the provider streams partial input or at once otherwise, and `ItemCompleted` closes it as succeeded, or failed when the input cannot be read. The adapter answers the call to the agent itself; nothing else needs to run.
- Tools whose calls the harness must execute and answer, with their own surface, arrive when the first one is needed.

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

- `connections` is required and holds at least one connection. `name` is required: 1 to 64 ASCII letters, digits, `-`, `_` and `.`, starting with a letter or a digit, unique in the file. `provider` is the identifier of a provider plugin and is required. `credential` is optional; without it the provider uses its own default configuration. Its `source` is required and its `reference` optional, but never blank. `settings` is optional, an object of strings the provider interprets; it is not for secrets.
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
| `Connections` | `ConnectionRegistry`, which implements `IConnections` and resolves a connection for `SessionStarter`; the declarations and the implicit ones; and the `IConnectionFile` port | Application |
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

Every provider plugin must pass the same check: start a session, send a turn and audit every event through the `Turn` aggregate, allowing every permission the turn requests and filling every form it asks with its recommended or first options. It reports items left open, rejected events, a missing `TurnStarted`, turns that never end and turns that end other than `Finished`, since nothing in the check fails or interrupts them; a replay that diverges ends its turn `Failed`, so the kit reports it too. When the session was asked to `AskEveryTime`, it also reports every file edit and command that goes ahead, by progressing or succeeding, without having asked permission first. A scripted provider and the simulator exercise the kit today: every well-behaved scenario of the simulator passes in `AskEveryTime`, the mode Agents uses, and its `left-open` and `hang` scenarios are reported, which proves the kit and the simulator against each other. The kit also runs on every committed [regression recording](#regression-fixtures), replayed by the simulator, so a recorded session of a real provider is checked by the same kit as the provider itself. The kit lives with the Agents tests until the first real provider needs it, when it moves to a shared testing project.

Every addition to the provider contract arrives with a check of the kit, the simulator implementing it and, through the simulator, a host simulation test:

| Check | Requires |
| --- | --- |
| Every turn | A resume token only from a provider that declares `CanResume`; a canvas only from a session that was given a canvas tool; a form only from a provider that declares `AsksQuestions`, and only well formed, since the `Turn` aggregate rejects the others; an account that does not change during the session |
| `CheckFormsAsync` | A provider that declares `AsksQuestions`, given an instruction that asks, asks a form, refuses an answer to an item that has no open form, accepts the answer to its form, and refuses a second answer to it once the turn ended. A provider that does not declare it runs the turn check, which forbids forms |
| `CheckDenialAsync` | Every permission is denied with a message, the "no, do this instead" answer: the turn still conforms, a permission was asked, and no denied item progresses or succeeds afterwards |
| `CheckResumeAsync` | A provider that declares `CanResume` issues a token during the turn, and a new session started with it is accepted and runs a conforming turn. A provider that does not declare it only runs the turn check, which forbids tokens |
| `CheckCanvasToolAsync` | A provider that declares `AcceptsTools`, given a canvas tool and an instruction that draws, reports the call as a canvas that completes. A provider that does not is given no tool, and runs the turn check |
| `CheckConnectionsAsync` | Two sessions of the same provider, started with the environments of two different [connections](#connections), stay isolated: each runs a conforming turn, they share no session, no account when both report one, and no resume token, and a token issued on the first connection is not accepted by a session started on the second |

### Simulator

**Accepted**

`Avala.Simulator` is a provider plugin that plays a Claude Code session without a model, so the harness runs end to end for demos and for catching bugs without spending tokens. It depends only on `Agents.Contracts` and the SDK, the same contracts a real provider uses, and registers itself as an `IAgentProvider`.

- Scenarios are declarative data in its domain, the `Scenarios` folder: one ordered script per turn, made of reasoning, message deltas, file edits, commands with output, permission requests, plan updates, usage with cost, a usage limit, streamed canvases and the end of the turn. A session advances to the next script with every turn, so a scenario can change its behavior after feedback.
- The first message of a session chooses the scenario with a tag such as `[simulate: fix-after-feedback]`. Without a tag, or with an unknown name, the scenario is `reply`. Later messages never change it. `[replay: <recording>]` and `[replay as recorded: <recording>]` choose a [recorded session](#replay) instead.
- File edits write real files into the session's working directory through a port of `Playback`, implemented in `FileSystem`, which creates missing folders.
- It honors the permission mode like Claude Code: in `AskEveryTime` every file edit and every command asks first, naming the file's full path or the command line, and an edit is written only once allowed; in `AllowEdits` edits go ahead and only the commands a scenario marks as asking do ask; in `AllowAll` nothing asks. A denied edit or command is cancelled and the turn finishes; a denial with a message is answered first with a reply that repeats it, `Understood, I will not go on: <message>`, so a test sees the message reach the agent.
- **Forms.** A scenario step asks a form as declarative data and waits for `AnswerAsync`; an answer for any other item is `NoPendingForm`. It then reports the answer, closes the item, cancelled when declined, and replies with what it goes on with, such as `Going with Database: PostgreSQL`. A declined form, or a confirmation left unconfirmed, ends the turn there, like a denial.
- Events can be spaced by a delay measured with `TimeProvider`. It is zero by default and in tests; the plugin entry uses a short pace for in-app demos, and a constructor overload takes another.
- It declares every capability, `AsksQuestions` included, and an interruption ends the running turn as `Interrupted`.
- **Resume.** A session's conversation is its scenario and the number of turns it played. Every turn issues, right after `TurnStarted`, a resume token that encodes both with the conversation's identifier and a fingerprint of the session's account, so a token survives a restart of the application without any storage. A session started with it on the same account continues the same conversation with its next script; a token it never issued, or one issued on another account, is rejected with `CannotResume`, as a harness rejects a conversation its configuration folder does not hold.
- **Canvas tool.** Given a tool whose surface is `Canvas`, a canvas of a scenario is a call of that tool, reported as the canvas events the contract defines. Without one, the simulator writes the same content as a message, like an agent that has no canvas.
- **Account.** The simulated equivalent of a login: a session reports the account of its connection's credential. A configuration folder is the account `simulated-login:<folder>`, labelled `Simulated account (<folder name>)`; an API key is `simulated-key:<fingerprint>`, labelled `Simulated API key`, and never shows the key; a connection without a credential reports the fixed account `simulated-account`, labelled `Simulated account`. Two connections of the simulator therefore run on distinct accounts.
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
| `canvas` | Draws an SVG and a Mermaid diagram through the canvas tool, in chunks |

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
- Each entry has `at` and exactly one of: `event`, an agnostic event; `file`, the content an edit left; `send`, `respond`, `answer` and `interrupt`, the harness's inputs, with `refused` when the provider returned an `AgentError`; `end`, with `crashed`; `stop`.
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
- The fixtures are `edit`, `fix-after-feedback`, two turns with a failed then a passed verification, and `question-autonomous`, a form answered by the policy.

## Canvas

**Accepted**

The harness can paint charts, diagrams, screens and designs while the agent writes them, for every provider.

- A canvas is an item of the turn: `CanvasStarted` opens it with its media type, `ItemProgressed` streams its content and `ItemCompleted` closes it. It inherits every integrity rule of items.
- The harness offers the canvas to every agent that accepts tools as a [harness tool](#harness-tools): the Canvas module registers its definition, `canvas` with a `title`, a `mediaType` among those it renders and the `content`, on the `Canvas` surface, in its `Drawing` folder. Agents passes it to providers without knowing Canvas, and each adapter reports the tool's calls as canvas events. Providers that stream partial output deliver the canvas in chunks; the others deliver it at once. The real adapter transports the tool through MCP; the simulator's `canvas` scenario draws through it today.
- The Canvas module accumulates each canvas, throttles updates and publishes snapshots. View models and renderers, plugins registered by media type, arrive with the user interface.
- Canvas content is untrusted: it renders in an isolated surface with no network access by default.

### Canvas module

**Accepted**

The module subscribes to `AgentActivity` with an `IHandle<T>`, like every other consumer of agent events, so it receives only events the `Turn` aggregate has already accepted. It works for every provider without knowing any.

| Folder | Holds | Layer |
| --- | --- | --- |
| `Canvases` | The `CanvasDocument` aggregate, `CanvasLifecycle`, the error enum and the domain events | Domain |
| `Drawing` | `CanvasTool`, the definition of the canvas tool the module offers to agents as a `HarnessTool` | Application |
| `Gallery` | The documents of every session, changed only from the feed's mailbox, and the `ICanvases` query, which reads an immutable list of their snapshots the gallery replaces on every change | Application |
| `Streaming` | `CanvasFeed`, the handler that applies canvas events to the gallery | Application |
| `Throttling` | `SnapshotThrottle`, which decides when a snapshot is published. Its cadences belong to a `SerialExecutor`: the feed's changes and the flushes its timers schedule run there one at a time | Application |

- A `CanvasDocument` is identified by its `CanvasId`, the turn and the item that carry it. It opens from `CanvasStarted`, which needs a media type, appends every `ItemProgressed` chunk in arrival order and closes on `ItemCompleted` in the state of its outcome: completed, failed, cancelled, abandoned or expired. It rejects content and completions of another item with `ForeignItem`, and anything after it closed with `AlreadyClosed`. The gallery rejects a canvas that starts twice with `AlreadyOpen`, and reports content for an item that never started as a canvas as `UnknownCanvas`, which is how the feed ignores messages, reasoning and tools.
- The generated [canvas lifecycle diagram](../diagrams/canvas-lifecycle.md) shows the states: `Streaming`, then one of the closed states inside the superstate `Closed`.
- `CanvasUpdated` carries a `CanvasSnapshot`: the canvas, its session, title, media type, the full content so far and its status. A snapshot holds the full content rather than a delta, so a consumer that misses one loses nothing.
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
| `Tracking` | `UsageTracker`, the handler of `SessionOpened`, `JobSessionStarted` and `AgentActivity`; `UsageBook`, the in-memory book that implements `IUsage`; and the `IUsageMetrics` port | Application |
| `Metrics` | `UsageMeter`, the `Avala.Observability` meter behind `IUsageMetrics` | Infrastructure |

- The domain is a projection of facts that already happened, so it has no aggregate and nothing to reject: records that return their next version, no error enum.
- Each `UsageReported` adds to the totals. A report without a cost adds its tokens and counts as unpriced, so a dashboard can tell a partial cost from a complete one. Costs add up per currency.
- A turn lasts from its `TurnStarted` to its `TurnCompleted`, measured with `TimeProvider` when the tracker receives each event. A turn counts once: a repeated start or end changes nothing.
- A limit belongs to the account a connection runs on, not to a session: each window keeps its latest reading, and `ByConnection` keeps the limits of two connections of one provider apart.
- `IUsage` in `Avala.Observability.Contracts` answers by provider, by account, by connection, by session and by job, with a `UsageSummary`: tokens, costs, unpriced reports, a `TurnTally` and limits. A job adds up every session it ran, recovery included.
- An account belongs to its provider: `ByAccount` groups sessions by provider and account, so two providers that use the same identifier stay apart, and leaves out sessions whose provider reported no account. The metrics carry no account tag, to keep their cardinality bounded.
- `ByConnection` groups sessions by the connection they opened on, with its provider, ordered by connection name, and leaves out sessions that were never announced.
- After it records a `UsageReported` or a `LimitReported`, the tracker publishes `UsageRecorded` with the session and its job. A consumer that reacts to spending, such as Budgets, handles it and reads `IUsage`, which already includes the report. Handling `AgentActivity` directly would not do: handlers run concurrently, so such a consumer could read the aggregates before the tracker applied the report.
- The tracker is the only writer of `UsageBook`. The book holds an immutable dictionary of sessions that the tracker replaces on every change, so `IUsage` answers from a consistent snapshot on any thread.
- The aggregates live in memory and start empty with the application. Persisting them, or rebuilding them from stored history, is left for when the dashboards need history across restarts.

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

A repository declares the rules of its jobs in three files: its checks in `.avala/checks.json`, its permission policy in `.avala/permissions.json` and its budget in `.avala/budget.json`. A fourth, `.avala/jobs.json`, names the connection its jobs prefer, read the same way by Jobs, see [the job flow coordinator](#job-flow-coordinator). The agent works in the job's worktree, so a rule read from the worktree is a rule the agent can rewrite. Every rule is therefore read from the job's base commit, the commit its worktree was created from, and nothing the agent changes during the job can loosen the rules that judge it.

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
- The reports live in memory and start empty with the application, like the usage aggregates. Persisting them arrives with the review screens that need them across restarts.

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
- Decisions live in memory for the life of the application, like the usage aggregates.

## Supervision

**Accepted**

An unattended agent must not hang forever or die silently. The Supervision module watches every running job and holds it through `IJobs.HoldAsync`, with the facts it measured, when its agent goes silent. A session that dies is Jobs' own concern: it holds the job as `SessionLost`, see [the job flow coordinator](#job-flow-coordinator).

### Rules

- **Silence.** A job is watched while it is `Running`, from its `JobProgressed`. Every accepted event of the job's current session, the one its latest `JobSessionStarted` named, restarts the silence; events of a session the job no longer uses do not. When the job stays silent for the whole window, it is held as `Stalled` with the silence measured and the window, and Jobs interrupts the turn.
- **Human time.** While a permission request or a form of the job's session waits for an answer, the job is never silent: the watch pauses on `PermissionRequested` and `FormRequested`, and restarts the window on `PermissionResolved`, `FormAnswered` or the end of the turn. This is the same rule as the `Turn` aggregate's expiry, where an item waiting for permission never expires. Checks running in `Checking` are not watched either: Verification bounds them with its own timeouts.
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

| `SupervisionError` | Cause |
| --- | --- |
| `Unreadable` | The file exists but cannot be read |
| `TooLarge` | Over 16 KiB |
| `Malformed` | Not JSON, not an object, a value of the wrong type or a duplicate field |
| `UnknownField` | A field the format does not define |
| `InvalidSilence` | A window not greater than 0 or over a day |

### The module

| Folder | Holds | Layer |
| --- | --- | --- |
| `Watching` | `JobWatch`, the immutable record of one job: whether it runs, its current session, the permission it waits for and its last activity; it decides whether the job is armed, when its alarm is due and whether it is silent | Domain |
| `Supervising` | `Watchdog`, the handler of `JobProgressed`, `JobSessionStarted`, `AgentActivity` and `SilenceNoticed`, which keeps the watch of every job; `SilenceAlarms`, the per-job timers; `Intervener`, which holds through `IJobs` and records; `SupervisionBook`, the in-memory book behind `ISupervision`; and the `ISupervisionSettings` port | Application |
| `Settings` | `SettingsFile` behind `ISupervisionSettings`, and `SettingsParser` | Infrastructure |

- The domain decides and rejects nothing, so it has no aggregate. `SupervisionError` is the module's single error enum, in its contracts, since only reading the settings can fail and its outcome is public.
- Interventions live in memory for the life of the application, like the usage aggregates.

## Budgets

**Accepted**

An unattended agent must not spend without limit. The Budgets module holds a job through `IJobs.HoldAsync` when it reaches a cap its repository declares, and records what it spent against what it was allowed.

### Caps

- Caps are per job: they count every session of the job, recovery included, as `IUsage.OfJob` adds them up. The module reads spending from `IUsage` instead of adding reports up again.
- **Cost.** One cap per currency. A job is held as `BudgetExceeded` when what it spent in a currency reaches the cap of that currency. Only priced reports count: with a provider that reports no cost, cap tokens instead.
- **Tokens.** One cap on every token the provider reported: input, output, cache reads, cache writes and reasoning. Counting all of them holds earlier rather than later.
- **Limits.** A threshold between 0 and 1. A job is held as `LimitNearlyReached` when a usage limit window of its session's connection reaches the threshold, whichever session on that connection reported it, since a limit belongs to the account a connection runs on: a work subscription near its limit never holds a job on a personal one.
- **By connection.** The `connections` section of the file gives the jobs on a named connection their own caps, which replace the top-level ones for those jobs, so an API key billed per token can be capped while a subscription is not. The loader reads the caps of the connection `SessionOpened` names.
- A cap is reached when the measure is equal to it or above it. The first breach found holds the job, in that order: cost, tokens, limit.

### When it checks

- When `UsageRecorded` names a job, after Observability recorded a usage or limit report; when `JobSessionStarted` ties a session to its job; when `BudgetLoaded` says the budget of a session tied to a job was read; and whenever `JobProgressed` says a job runs again, so a job already over its budget is held as soon as a retry, a hint or a recovery starts it, before it spends more.
- The loader and the enforcer are separate handlers, so the enforcer may learn that a job runs before the budget file of its session was read. It then has nothing to enforce yet, and `BudgetLoaded`, published once the loader kept the budget, makes it check again. The enforcer keeps which session each job runs in and the status of each job in its own mailbox; the loader is the only writer of the budgets in `BudgetBook`, which holds them in an immutable dictionary.
- Only a `Running` job is checked. A hold interrupts the turn, so an agent that reports usage as it goes is stopped mid-turn; one that reports only at the end of its turns can overshoot by one turn.

### Budget file

A repository declares its caps in `.avala/budget.json`, read when the session opens from the job's [base commit](#rules-from-the-base-commit), like the policy file. Without the file, or for a session outside any job's worktree, nothing is capped: the built-in default has no caps and no limit threshold.

```json
{
  "costPerJob": { "USD": 5.00 },
  "tokensPerJob": 2000000,
  "holdAtLimit": 0.9,
  "connections": {
    "team-api": { "costPerJob": { "USD": 2.00 } }
  }
}
```

- Every field is optional. `costPerJob` maps a currency, as the provider reports it, to an amount greater than 0. `tokensPerJob` is a whole number greater than 0. `holdAtLimit` is greater than 0 and at most 1.
- `connections` maps a [connection](#connections) name to caps of the same three fields, which replace the top-level caps for the jobs on that connection; a connection it does not name gets the top-level caps. A section names a connection of the machine that runs the job, so a name no connection has caps nothing. A blank name is `Malformed`, and a section is validated like the top level, without a `connections` of its own.
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

### The module

| Folder | Holds | Layer |
| --- | --- | --- |
| `Caps` | `Breaches`: the evaluation of a session's budget against a job's spending and its provider's limits, the reason each breach holds a job for, and the built-in caps | Domain |
| `Enforcement` | `BudgetLoader`, the handler of `SessionOpened`; `BudgetEnforcer`, the handler of `BudgetLoaded`, `JobSessionStarted`, `JobProgressed` and `UsageRecorded`; `BudgetHolds`, which holds through `IJobs` and records; `BudgetBook`, the in-memory book behind `IBudgets`; and the `IBudgetFiles` port | Application |
| `BudgetFiles` | `BudgetFileReader` behind `IBudgetFiles`, which reads the file through `IBaseFiles` from Workspaces, and `BudgetFileParser` | Infrastructure |

- The domain decides and rejects nothing, so it has no aggregate. `BudgetError` is the module's single error enum, in its contracts.
- Budgets and interventions live in memory. Spending lives in Observability's memory too, so a restart starts every job's spending from zero; persisting usage is deferred with the dashboards.
- **Trust.** The budget file comes from the base commit, so an agent cannot raise its own caps: neither a running session nor a recovered one, which reads the same commit again, sees an edit made in the worktree. `BudgetLoaded` carries the file's origin, with whether the worktree's copy differs.

## Data the harness produces

The user interface is designed from the data the harness produces, so every module that produces data lists it here: its integration events on the bus and its queries, with their shape, when and how often they are produced, and their cardinality.

### Verification

| Data | Kind | Shape | When | Cardinality |
| --- | --- | --- | --- | --- |
| `AttemptVerified` | Integration event | `Report`: a `VerificationReport` | Once per evaluation of the gate, after the checks of an attempt ran and before Jobs moves the job on, so it precedes the `JobProgressed` of the retry, the review or the request for help | One per finished turn of every job that reaches the gates, including recovery attempts. A repository without checks produces one too |
| `IVerifications.OfJob(JobId)` | Query | `IReadOnlyList<VerificationReport>` in the order the attempts were verified; empty for an unknown job | At any time, from memory | One report per evaluation of that job since the application started |

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
| `IConnections.CatalogAsync()` | Query | `ConnectionCatalog`: `File` (`ConnectionFileStatus`: `Absent`, `Applied` or `Rejected`), `Option<ConnectionError>`, `Connections` in declaration order, each a `DeclaredConnection` with `Name`, `Provider` (the provider's identifier) and the `Option<string>` name of its credential `Source`, never its reference or secret; and the `Option<ConnectionName>` `Default`, none when the file is rejected | Any time; reads `connections.json` the first time | One per application: the implicit connections without a file, none when the file is rejected |
| `IConnections.CheckAsync(Option<ConnectionName>)` | Query | `Result<ConnectionInfo, ConnectionError>`: the connection's `Name` and the `ProviderInfo` of its provider, the default one when none is named; or why it cannot be used | On demand: resolves the credential each time, so a folder created or a variable set since is seen | One answer per call |
| `SessionOpened.Connection` | Field of an event | The `ConnectionName` the session opened on | When a session opens; fixed for the session | One per session |
| `OpenedSession.Connection` | Field of a command answer | The `ConnectionName` the session opened on | With every `IAgents.OpenAsync` that succeeds | One per session |
| `JobRequest.Connection` | Field of a command | `Option<ConnectionName>`, the connection the job asks for; none takes the repository's default from `.avala/jobs.json`, then the machine's | At submission; checked then, a rejection being `UnknownConnection` or `UnusableConnection` | One per job |
| The job's connection | Stored with the job | The `ConnectionName` its first session opened on | Fixed when the job starts; reused by recovery and continuation | One per job |
| `IUsage.ByConnection()` | Query | `IReadOnlyList<ConnectionUsage>`: `Connection`, its `Provider` and its `UsageSummary`, limits included, ordered by connection name | Any time, from memory | One per connection that opened a session since the application started |

The job's connection is not yet part of an event of Jobs: a view finds it through the `SessionOpened` of the job's sessions, which `JobSessionStarted` ties to the job.

### Workspaces: base files

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `WorkspaceInfo.BaseCommit` | Field of `IWorkspaces` answers | The full SHA of the commit the worktree was created from | Fixed when the workspace is prepared; stored with it | One per workspace |
| `IBaseFiles.ReadAsync(worktree, path)` | Query | `Result<BaseFile, WorkspaceFailure>`: `Path`, `Origin` (`FileOrigin`: `Commit` and `EditedInWorktree`) and `Content`, an `Option<string>` absent when the commit holds no file there; `UnknownWorkspace` for a folder that is no worktree, `GitFailed` when git fails | On demand, at most three git commands each time | One answer per call |

`FileOrigin` is the provenance every rule file reports: Verification in `VerificationReport.Declaration`, Permissions in `SessionPolicy.Origin` and Budgets in `SessionBudget.Origin`.

### Jobs: holds

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `JobHeld` | Event | `Hold`: a `JobHold` with `Job`, `Session`, `Reason` (`HoldReason`: `Stalled`, `SessionLost`, `BudgetExceeded`, `LimitNearlyReached`, `InvalidBudget`) and `Halt` (`SessionHalt`: `Interrupted`, `Idle`, `Stopped`, `AlreadyClosed`) | Each time a module holds a running job, or Jobs holds one whose session ended on its own, right after the job's `JobProgressed` with `NeedsHelp` and once its session was halted | Zero or one per run of a job: a held job runs again only after a human hint |
| `JobResumable` | Event | `Job`, `Session` whose resume token Jobs stored | Each time the job's current session issues a resume token, once the token is stored. Never for a session the job no longer uses | Zero or more per session; the simulator issues one per turn |
| `IJobs.ContinueAsync(JobId, message)` | Command answer | `Result<JobContinuation, JobRejection>`: `Job`, the `Session` the job continues in and `Conversation` (`ContinuedIn`: `SameSession`, `ResumedConversation`, `NewConversation`); or `NotHeld`, `EmptyMessage`, `UnknownJob`, `WorkspaceUnavailable`, `UnknownConnection`, `UnusableConnection`, `AgentUnavailable` | When a human answers a job that needs help. A success is followed by `JobProgressed` with `Running`, and by `JobSessionStarted` when the session is new | One per human answer |

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
| `IUsage.ByAccount()` | Query | `IReadOnlyList<AccountUsage>`: `Provider`, `Account` and its `UsageSummary`, ordered by provider then account identifier | Any time, from memory | One per provider and account that reported usage since the application started; sessions without an account are left out |
### Supervision

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `SilenceNoticed` | Event | `Job` | When the silence alarm of a running job rings. It is how the alarm reaches the watchdog's mailbox, which confirms it after every event published before it, so it does not mean the job was held | At most one pending per job; a job active for a long run gets one each time its window elapses without its last activity having moved |
| `SupervisorIntervened` | Event | `Intervention`: a `SupervisionIntervention` | After a hold succeeded, following the hold's `JobProgressed` and `JobHeld` | One per intervention |
| `ISupervision.OfJob(JobId)` | Query | `IReadOnlyList<SupervisionIntervention>` in the order they happened; empty for an unknown job | Any time, from memory | Zero or more per job |
| `ISupervision.SettingsAsync` | Query | `SupervisionSettings`: `Silence` window, `File` (`SettingsFileStatus`: `Absent`, `Applied` or `Rejected`) and `Option<SupervisionError>` | Any time; reads the settings file the first time | One per application |

`SupervisionIntervention` carries `Hold`, the `JobHold` Jobs returned, always `Stalled`; `Silence`, a `SilenceMeasure` with the measured `Silent` time and the `Window`; and `At`, from `TimeProvider`. A lost session is not an intervention of Supervision: it shows as the `JobHeld` with `SessionLost` that Jobs publishes.

### Budgets

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `BudgetLoaded` | Event | `Budget`: a `SessionBudget` with `Session`, `File` (`BudgetFileStatus`: `Absent`, `Applied` or `Rejected`), `Option<BudgetError>`, `Caps` and `Origin`, the `Option<FileOrigin>` of the budget file, as for the policy | When a session opens, after its budget file was read and before the session is tied to its job | One per session |
| `BudgetIntervened` | Event | `Intervention`: a `BudgetIntervention` | After a hold succeeded, following the hold's `JobProgressed` and `JobHeld` | One per intervention |
| `IBudgets.BudgetOf(SessionId)` | Query | `Option<SessionBudget>`; none until the session opened | Any time | One per session |
| `IBudgets.OfJob(JobId)` | Query | `IReadOnlyList<BudgetIntervention>` in the order they happened | Any time, from memory | Zero or more per job |

`BudgetCaps` has `CostPerJob`, a list of `Cost` caps, one per currency; `TokensPerJob`, an `Option<long>`; and `HoldAtLimit`, an `Option<double>`. A session's `Caps` are those of its connection: the file's `connections` section for it when there is one, the top-level caps otherwise. `BudgetIntervention` carries `Hold`, the `JobHold`; `Breach`; and `At`. A `BudgetBreach` states the measured facts: `Measure` (`Cost`, `Tokens`, `Limit` or `Declaration`), `Subject` (the currency, `tokens`, the limit window or the budget file), `Measured` and `Cap` as decimals (spent against cap, or the limit fraction used against the threshold; both 0 for a declaration) and `Error`, the `Option<BudgetError>` of an invalid declaration.

### Recording

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `recordings/<yyyyMMddTHHmmssZ>-<session>.json` | File in the data folder | A recording in the [format](#format) `avala-recording` version 1 | Only when `recording.json` enables recording. Written whole when a turn completes, the stream ends, the session stops and the application shuts down | One per session started while recording is on, kept until someone deletes it |
| `Replay diverged` | Item of a replayed turn | `ItemStarted` of kind `Other` with the item `replay-divergence`, one `ItemProgressed` with the reason, `ItemCompleted` as `Failed`, then `TurnCompleted` as `Failed` | When a replayed session meets an input or a turn its recording does not hold | At most one per replayed session, which closes after it |

The module publishes no event and answers no query: a recording is a file for people and tests, and a replay is an ordinary simulated session.

## Delivery

**Accepted**

The application is built view model first: every screen is built and tested as view models with no user interface. Avalonia views come last, as a thin layer bound to view models that already work.

## Persistence

**Accepted**

- EF Core with the SQLite provider, with no server.
- One `DbContext` and one database file per module, under the data folder: `jobs.db`, `workspaces.db`. Separate files isolate modules for real, and each module creates its schema on its own. No module reads another module's data.
- The schema is created with `EnsureCreated`. The first schema changes, the `Base` column of a workspace and the `Resume`, `Autonomy` and `Connection` columns of a job, arrived before any release could create jobs, so they ship without a migration: a data folder created by an earlier build must be deleted, or its `jobs.db` at least. Migrations arrive with the first schema change after a release.
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

The host registers `AvalaPaths` from the SDK. Its data folder is `AVALA_DATA_PATH` when set, otherwise `Avala` under the local application data folder. It locates the database files, the worktree root, the settings files `supervision.json`, `recording.json` and `connections.json`, the `connections` folder that keeps the login of each connection by default, and the `recordings` folder the recorder writes and the simulator replays from. The composition root receives it, so the host tests point it at a temporary folder.

### Startup tasks

Modules register `IStartupTask` implementations, such as `JobRecovery`. The runtime runs them in `RuntimeHost.RunAsync`, after the event bus has started, so the events they publish are dispatched.

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
