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
| Jobs | Job lifecycle, attempts, attempt budget, the job flow coordinator | `JobId`, integration events, `IJobs`, `ICompletionGate` |
| Agents | Sessions, turn integrity, provider registry | `IAgents`, `IAgentProvider`, `IAgentSession`, `AgentEvent`, `AgentCapabilities`, integration events |
| Workspaces | Working copies, branches, checkpoints | `IWorkspaces`, integration events |
| Canvas | Accumulates the canvases agents stream and publishes throttled snapshots | `CanvasId`, `CanvasUpdated`, `ICanvases` |
| Timeline | Read model of everything that happened in a job, for the activity view | Queries |
| Observability | Tokens, cost, limits and turns by provider, session and job, and their metrics | `IUsage` and its summaries |
| Verification | Runs the checks a repository declares as a completion gate and keeps the evidence of every attempt | `AttemptVerified`, `VerificationReport`, `IVerifications` |
| Permissions | Answers permission requests through an explicit policy and records why each decision was made | `PolicyLoaded`, `PermissionDecided`, `IPermissionAudit` |

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

    public static Result<Job, JobError> Create(JobId id, Instruction instruction, AttemptBudget budget, RepositoryPath repository);
    public Result<JobSubmitted, JobError> Submit();
    public Result<AttemptStarted, JobError> Start(WorkspaceId workspace, SessionId session);
    public Result<AttemptStarted, JobError> Recover(SessionId session);
    public Result<AttemptCompleted, JobError> CompleteTurn();
    public Result<AttemptPassed, JobError> Pass();
    public Result<AttemptRetried, JobError> Retry(Feedback feedback);
    public Result<HelpRequested, JobError> RequestHelp();
    public Result<AttemptStarted, JobError> Hint(Feedback guidance);
    public Result<AttemptStarted, JobError> SendBack(Feedback feedback);
    public Result<JobApproved, JobError> Approve();
    public Result<JobDiscarded, JobError> Discard();
    public Result<JobFailed, JobError> Fail(FailureReason reason);
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
| `TurnLifecycle` | Agents | Idle, Working, AwaitingPermission, Completed, Interrupted, Failed |
| `WorkspaceLifecycle` | Workspaces | Creating, Ready, Disposed |
| `CanvasLifecycle` | Canvas | Streaming, then Completed, Failed, Cancelled, Abandoned or Expired inside the superstate Closed |

The generated [job lifecycle diagram](../diagrams/job-lifecycle.md) is the reference. A test fails when it no longer matches the code.

The job references its workspace and its agent session by identifier only. Both are absent until the job starts. `Recover` brings a job that was `Running` or `Checking` when the application stopped back to `Running`: it interrupts the attempt that was underway, records the new session and starts a `Recovery` attempt with a fresh round of retries.

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
- `Avala.Runtime` implements the bus with `System.Threading.Channels`: one asynchronous queue and one dispatcher, which preserves event order and never blocks the publisher.
- A failing handler is isolated and logged. The other handlers still run.
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
- Events arrive off the UI thread. The SDK defines `IUiDispatcher` and the host implements it, so view models stay unaware of Avalonia.

### Consistency without an outbox

**Accepted**

- Stored state is the truth. Events are notifications.
- On startup `JobRecovery` inspects every active job and resumes it from its state, so a lost event is recovered.
- Handlers are idempotent: receiving an event twice has the effect of receiving it once.

## Job flow coordinator

The coordinator replaces the orchestrator. It is a set of small stateless classes in the application layer of Jobs, grouped by use case in the folders `Submission`, `Launching`, `TurnChecks`, `Recovery` and `Ledger`: the state of the flow is the `Job` aggregate itself. Each class keeps four or fewer dependencies. **Accepted**

| Class | Role |
| --- | --- |
| `JobLedger` | Stores a job, then publishes `JobProgressed` with its status |
| `SubmitJob` | Creates a job, submits it, stores it and publishes `JobSubmitted` |
| `JobLauncher` | Prepares the workspace, opens the agent session, starts the job, stores it, announces `JobSessionStarted` and only then sends the instruction. It also relaunches a job after a restart |
| `PrepareJob` | Handles `JobSubmitted` by launching the job |
| `CheckTurn` | Handles `TurnFinished`: checkpoints the workspace, evaluates the gates, then passes the job, retries with feedback to the same session, or asks for help when the budget is spent |
| `CompletionGates` | Combines every registered gate into one verdict |
| `JobRecovery` | An `IStartupTask` that launches `Preparing` jobs and recovers `Running` or `Checking` jobs |

- The job stores its session before the instruction is sent, so a fast agent cannot finish a turn the job does not know yet.
- A turn that ends interrupted or failed fails the job.
- Handlers are idempotent. `CheckTurn` acts only on a job that is still `Running`, so a repeated `TurnFinished` changes nothing.
- Recovery opens a new session in the existing workspace and calls `job.Recover`, which interrupts the attempt that was underway and starts a `Recovery` attempt.

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
    IAsyncEnumerable<IAgentEvent> Events { get; }
    ValueTask<Result<TurnId, AgentError>> SendAsync(UserTurn turn, CancellationToken cancellationToken);
    ValueTask<Result<ItemId, AgentError>> RespondAsync(PermissionDecision decision, CancellationToken cancellationToken);
    ValueTask<Result<TurnId, AgentError>> InterruptAsync(CancellationToken cancellationToken);
}
```

- `SessionOptions` holds harness concepts only: working directory and permission mode. Paths, tokens and protocols belong to each provider's own settings.
- Behavior depends on `AgentCapabilities`, never on a provider's name: partial output, reasoning, interruption, resumption, usage, cost and limits.

Other modules use agents through `IAgents`, in two steps:

```csharp
public interface IAgents
{
    ValueTask<Result<SessionId, AgentError>> OpenAsync(AgentRequest request, CancellationToken cancellationToken);
    ValueTask<Result<AgentTurn, AgentError>> SendAsync(SessionId session, string message, CancellationToken cancellationToken);
    ValueTask<Result<ItemId, AgentError>> RespondAsync(SessionId session, PermissionDecision decision, CancellationToken cancellationToken);
    ValueTask<Result<SessionId, AgentError>> StopAsync(SessionId session, CancellationToken cancellationToken);
}
```

- `OpenAsync` opens a session in the working directory of `AgentRequest` and returns its `SessionId`. `SendAsync` sends a message and returns the `AgentTurn` it started. Opening and sending are separate so the caller can store the session before any turn can finish: Jobs records it on the job first.
- `RespondAsync` answers the permission request of a live session and returns the item it unblocked. A session that is not open returns `SessionClosed`.
- `AgentSessions` implements `IAgents`. It announces every session it opens with `SessionOpened`, carrying the `ProviderInfo` of its provider, before pumping any of its events. It pumps the events of each session through the `Turn` aggregate and publishes the accepted ones as `AgentActivity`, plus `TurnFinished` when a turn ends.

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
| `PlanUpdated` | The agent's plan and the status of each step |
| `UsageReported` | Tokens used: input, output, cache reads, cache writes and reasoning, plus the cost when the provider reports it |
| `LimitReported` | A usage limit: its window, the fraction used and when it resets |

All work inside a turn shares one lifecycle: started, progressed, completed. Messages, tools and canvases therefore get the same integrity guarantees and the same rendering pipeline.

### Turn integrity

**Accepted**

The `Turn` aggregate applies every incoming event and returns a `Result`:

- Every started item completes, and no item starts twice or progresses before it starts or after it ends.
- Events from another session or turn are rejected, and nothing is accepted after the turn ends.
- When a turn ends with items still open, the turn closes them as `Abandoned` before forwarding the end, so the stream stays consistent for every consumer.
- An item silent for longer than the allowed patience expires. An item waiting for permission never expires: that is human time.
- Time is passed in by the caller, so the domain stays pure and tests never wait.

The generated [turn lifecycle diagram](../diagrams/turn-lifecycle.md) shows the states.

### Conformance kit

**Accepted**

Every provider plugin must pass the same check: start a session, send a turn and audit every event through the `Turn` aggregate, allowing every permission the turn requests. It reports items left open, rejected events, a missing `TurnStarted` and turns that never end. A scripted provider and the simulator exercise the kit today: every well-behaved scenario of the simulator passes, and its `left-open` and `hang` scenarios are reported, which proves the kit and the simulator against each other. The kit lives with the Agents tests until the first real provider needs it, when it moves to a shared testing project.

### Simulator

**Accepted**

`Avala.Simulator` is a provider plugin that plays a Claude Code session without a model, so the harness runs end to end for demos and for catching bugs without spending tokens. It depends only on `Agents.Contracts` and the SDK, the same contracts a real provider uses, and registers itself as an `IAgentProvider`.

- Scenarios are declarative data in its domain, the `Scenarios` folder: one ordered script per turn, made of reasoning, message deltas, file edits, commands with output, permission requests, plan updates, usage with cost, a usage limit, streamed canvases and the end of the turn. A session advances to the next script with every turn, so a scenario can change its behavior after feedback.
- The first message of a session chooses the scenario with a tag such as `[simulate: fix-after-feedback]`. Without a tag, or with an unknown name, the scenario is `reply`. Later messages never change it.
- File edits write real files into the session's working directory through a port of `Playback`, implemented in `FileSystem`.
- Events can be spaced by a delay measured with `TimeProvider`. It is zero by default and in tests; the plugin entry uses a short pace for in-app demos, and a constructor overload takes another.
- It declares every capability, and an interruption ends the running turn as `Interrupted`.
- `tests/Avala.Host.Tests` plays its scenarios inside the application composed from the published plugin folder, with its in-app pace, and observes the jobs and the canvas snapshots through the event feed.

| Scenario | Behavior |
| --- | --- |
| `reply` | Reasoning and a streamed reply |
| `edit` | A plan, a file written into the working directory, a test command and a reply |
| `fix-after-feedback` | The first turn writes a file marked `BROKEN`; the turn after feedback rewrites it fixed |
| `permission` | Asks permission for a command and waits for `RespondAsync`. Allowed, it runs the command and goes on; denied, it cancels the command and finishes the turn |
| `crash` | The event stream throws in the middle of the turn |
| `left-open` | Starts an item and finishes the turn without closing it |
| `hang` | `TurnStarted` and nothing else, until interrupted |
| `canvas` | Streams an SVG and a Mermaid diagram in chunks |

Every scenario that reaches its end reports usage with cost and a usage limit, so observability can be exercised.

## Canvas

**Accepted**

The harness can paint charts, diagrams, screens and designs while the agent writes them, for every provider.

- A canvas is an item of the turn: `CanvasStarted` opens it with its media type, `ItemProgressed` streams its content and `ItemCompleted` closes it. It inherits every integrity rule of items.
- The harness offers the canvas to every agent as a tool it injects through MCP. Providers that stream partial output deliver the canvas in chunks; the others deliver it at once. The tool arrives with the real Claude Code provider; until then the simulator's `canvas` scenario streams canvases through the same agnostic events.
- The Canvas module accumulates each canvas, throttles updates and publishes snapshots. View models and renderers, plugins registered by media type, arrive with the user interface.
- Canvas content is untrusted: it renders in an isolated surface with no network access by default.

### Canvas module

**Accepted**

The module subscribes to `AgentActivity` with an `IHandle<T>`, like every other consumer of agent events, so it receives only events the `Turn` aggregate has already accepted. It works for every provider without knowing any.

| Folder | Holds | Layer |
| --- | --- | --- |
| `Canvases` | The `CanvasDocument` aggregate, `CanvasLifecycle`, the error enum and the domain events | Domain |
| `Gallery` | The documents of every session behind one lock, and the `ICanvases` query | Application |
| `Streaming` | `CanvasFeed`, the handler that applies canvas events to the gallery | Application |
| `Throttling` | `SnapshotThrottle`, which decides when a snapshot is published | Application |

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
- An Observability module subscribes to the events on the bus and aggregates them by provider, session and job. Aggregating by account waits until an event carries the account, which arrives with the real providers.
- It publishes metrics through `System.Diagnostics.Metrics`, the .NET standard that OpenTelemetry collects, and later feeds view models for the in-app dashboards.

### Correlation

Agent events know only their session. Two integration events tie a session to the rest:

| Event | Published by | Carries |
| --- | --- | --- |
| `SessionOpened` | `AgentSessions`, before it pumps the session's events | `SessionId`, `ProviderInfo` |
| `JobSessionStarted` | `JobLauncher`, after storing the job and before sending the instruction, at launch and at recovery | `JobId`, `SessionId` |

The bus dispatches in publishing order, so both arrive before the first activity of the session. Observability does not rely on it: it keeps everything per session and groups sessions by provider and job only when queried, so a late correlation still lands in the right aggregate.

### The module

| Folder | Holds | Layer |
| --- | --- | --- |
| `Usage` | `SessionUsage`, an immutable record of one session: provider, job, tokens, cost per currency, unpriced reports, turns by outcome with their duration, and the latest reading of each limit window. The arithmetic on tokens and turns, and the rollup of several sessions into a summary | Domain |
| `Tracking` | `UsageTracker`, the handler of `SessionOpened`, `JobSessionStarted` and `AgentActivity`; `UsageBook`, the in-memory book that implements `IUsage`; and the `IUsageMetrics` port | Application |
| `Metrics` | `UsageMeter`, the `Avala.Observability` meter behind `IUsageMetrics` | Infrastructure |

- The domain is a projection of facts that already happened, so it has no aggregate and nothing to reject: records that return their next version, no error enum.
- Each `UsageReported` adds to the totals. A report without a cost adds its tokens and counts as unpriced, so a dashboard can tell a partial cost from a complete one. Costs add up per currency.
- A turn lasts from its `TurnStarted` to its `TurnCompleted`, measured with `TimeProvider` when the tracker receives each event. A turn counts once: a repeated start or end changes nothing.
- A limit belongs to the provider, not to a session: each window keeps its latest reading.
- `IUsage` in `Avala.Observability.Contracts` answers by provider, by session and by job, with a `UsageSummary`: tokens, costs, unpriced reports, a `TurnTally` and limits. A job adds up every session it ran, recovery included.
- The aggregates live in memory and start empty with the application. Persisting them, or rebuilding them from stored history, is left for when the dashboards need history across restarts.

| Instrument | Kind | Unit | Tags |
| --- | --- | --- | --- |
| `avala.agent.tokens` | Counter | `{token}` | `avala.provider`, `avala.token.type`: `input`, `output`, `cache_read`, `cache_write`, `reasoning` |
| `avala.agent.cost` | Counter | `{currency}` | `avala.provider`, `avala.currency` |
| `avala.agent.turns` | Counter | `{turn}` | `avala.provider`, `avala.turn.outcome` |
| `avala.agent.turn.duration` | Histogram | `s` | `avala.provider`, `avala.turn.outcome` |
| `avala.agent.limit.used` | Gauge | `1` | `avala.provider`, `avala.limit.window` |

The provider tag is the provider's identifier, and it is left out when the session's provider is unknown.

## Verification

**Accepted**

A job's completion depends on evidence, not on the agent's word. The Verification module is a plugin that registers an `ICompletionGate`: after every finished turn, once Jobs has checkpointed the worktree, it runs the checks the repository declares inside the job's worktree and turns their results into the gate's verdict. Jobs knows only the gate contract, never the module.

### Declaring checks

A repository declares its checks in `.avala/checks.json`, at the root of the repository and therefore of every worktree:

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

- **No declaration.** A worktree without `.avala/checks.json`, or with an empty `checks` array, passes, and the evidence says so: the report's outcome is `NoChecksDeclared`. A repository is never verified silently.
- **Invalid declaration.** A file that is not a JSON object with a `checks` array of objects, a check without a command, or a timeout out of range fails closed: nothing runs, the outcome is `InvalidDeclaration` and the verdict is `Retry` with feedback naming the file and the problem. The agent can fix it; if it does not, the attempt budget runs out and the job asks for help.
- **Failure.** A check fails when it exits with a code other than 0, when its command is not found, or when it outlasts its timeout. The checks after the first failure are not run and are recorded as `Skipped`, since a broken build makes the tests meaningless.
- **Timeout.** Each check runs with a cancellation token that fires after its timeout, measured with `TimeProvider`. The runner kills the process tree and the check is `TimedOut`. When the job flow itself is cancelled, at shutdown, the cancellation propagates and nothing is recorded.
- **Feedback.** A failure becomes `GateVerdict.Retry` with feedback naming the check, its command line, its exit code or the reason it stopped, its duration and the tails of its output and error streams. Jobs sends it back to the same agent session through the existing retry path, and asks for help once the attempt budget is spent. Verification adds no second path.
- **Evidence.** Every evaluation, whatever its outcome, produces one `VerificationReport`, kept in memory and published as `AttemptVerified` before the gate returns. Output tails keep the last 4,000 characters of each stream, prefixed by `[...]` when cut.
- **Trust.** The declaration is read from the worktree, so an agent could edit it. The report lists every command that ran, which the reviewer sees; reading the declaration from the job's base commit instead needs that commit in `CompletedAttempt` and is deferred.

### The module

| Folder | Holds | Layer |
| --- | --- | --- |
| `Checks` | `DeclaredCheck`, the parsing of the declaration with `JsonDocument`, the evidence of a check and its bounded tails, and `VerificationError` | Domain |
| `Verifying` | `ChecksGate`, the `ICompletionGate`; `CheckRunner`, which runs one check with its timeout; `AgentFeedback`, which writes the verdict; and the `ICheckDeclarations` port | Application |
| `Evidence` | `EvidenceBook`, the in-memory book behind `IVerifications`, and `EvidenceLedger`, which keeps a report and publishes it | Application |
| `FileSystem` | `RepositoryDeclarations`, which reads `.avala/checks.json` from the worktree | Infrastructure |

- The domain has no aggregate: it parses a declaration and describes facts. `VerificationError` is its single error enum, for the declaration it can reject: `MalformedDeclaration`, `MissingCommand` and `InvalidTimeout`.
- The reports live in memory and start empty with the application, like the usage aggregates. Persisting them arrives with the review screens that need them across restarts.

## Permissions

**Accepted**

Agents ask before they act. For agents to work unattended, the harness must answer those requests itself, and for its answers to be trusted, it must say why it gave each one. The Permissions module answers through an explicit policy and records every decision, including the ones it leaves to a human.

### Policy

- A policy is an ordered list of rules. Each rule may name an item kind, a target pattern and a scope, and gives one answer: `Allow`, `Deny` or `Ask`, which leaves the request to a human exactly as if no policy existed.
- The first rule that matches decides. When none matches, the answer is `Ask`: the safe default never grants anything.
- A rule matches on the agnostic facts of `PermissionRequested`: its item kind and its target. A missing kind or target matches any. No rule knows a provider.
- A target pattern matches the whole target, case-sensitively, with `*` for any run of characters, `/` included, and `?` for one character. The matcher is linear and backtracks only to the last `*`, so no pattern can make it slow.
- A rule scoped to the `workspace` matches only file edits whose path lies inside the session's working directory. The path is resolved against the working directory, so `src/../x` and an absolute path inside both count, and `../x` does not; the target an inside edit is matched and recorded with is its path relative to the working directory, with `/` separators. Other kinds have no path to scope, so a policy file that scopes them is rejected.
- The policy of a session is, in order: the built-in guards, the repository rules, the built-in defaults, then the default `Ask`.

| Rule | Origin | Matches | Answer |
| --- | --- | --- | --- |
| `policy-file-goes-to-a-human` | Built-in guard | Edits of `.avala/permissions.json` inside the workspace | `Ask` |
| Repository rules | `.avala/permissions.json` | Whatever they declare, in file order | Their own |
| `edits-inside-the-workspace` | Built-in default | Edits inside the workspace | `Allow` |
| Default | None | Anything else | `Ask` |

The guard comes first so that no repository rule can let an agent rewrite the policy that governs it. Editing inside the workspace is allowed by default because the workspace is the job's own disposable worktree, reviewed before anything is approved; a repository can still deny or ask for it with a rule of its own.

### Policy file

A repository declares its rules in `.avala/permissions.json`, read from the session's working directory when the session opens. A job's worktree is a checkout of the repository, so the committed file governs every job of that repository.

```json
{
  "rules": [
    { "name": "run the tests", "kind": "command", "target": "dotnet test*", "answer": "allow" },
    { "name": "no network", "kind": "web", "answer": "deny" },
    { "kind": "fileEdit", "within": "workspace", "target": "docs/*", "answer": "ask" }
  ]
}
```

- `kind` is an `ItemKind` name, case-insensitive: `message`, `reasoning`, `fileEdit`, `command`, `search`, `web`, `mcp`, `subagent` or `other`. `within` is `anywhere`, the default, or `workspace`. `answer` is `allow`, `deny` or `ask`, and is required. A rule without a `name` is named by its position, such as `rule 3`.
- The file is parsed strictly: unknown fields, duplicate fields, numbers for names, nesting deeper than the format needs and files over 64 KiB are rejected without being interpreted.
- An invalid file is reported with a `PolicyError`, and the session falls back to the built-in policy. A broken file can therefore only make the harness ask more, never allow more.

| `PolicyError` | Cause |
| --- | --- |
| `Unreadable` | The file exists but cannot be read |
| `TooLarge` | Over 64 KiB |
| `Malformed` | Not JSON, a value of the wrong type, a duplicate field or nesting too deep |
| `UnknownField` | A field the format does not define |
| `UnknownKind`, `UnknownScope`, `UnknownAnswer` | A value outside its list |
| `MissingAnswer` | A rule without an answer |
| `ScopeNeedsFileEdits` | A `workspace` scope on a kind other than `fileEdit` |

### The module

The module subscribes to the bus like every other consumer of agent events, so it receives only permission requests the `Turn` aggregate has already accepted, and answers through `IAgents.RespondAsync`.

| Folder | Holds | Layer |
| --- | --- | --- |
| `Policies` | `PermissionPolicy` with its built-in rules and first-match decision, rule matching as extension members on `PolicyRule`, `PermissionRequest`, `Verdict`, and `GovernedSession`, the immutable record of one session: working directory, policy, job and decisions | Domain |
| `Governance` | `SessionGovernor`, the handler of `SessionOpened` and `JobSessionStarted`; `GovernanceBook`, the in-memory book that implements `IPermissionAudit`; and the `IPolicyFiles` port | Application |
| `Answering` | `PermissionResponder`, the handler of `AgentActivity` that decides, answers and records; `RequestFacts`, which locates a request against the working directory | Application |
| `PolicyFiles` | `PolicyFileReader` behind `IPolicyFiles`, and `PolicyFileParser` | Infrastructure |

- The domain decides and has nothing to reject, so it has no aggregate. The single error enum of the module is `PolicyError`, in its contracts, since only reading a policy file can fail and its outcome is public.
- On `SessionOpened` the governor reads the policy file once, keeps the session's policy and publishes `PolicyLoaded`. Later edits of the file in the worktree do not change the policy of a running session.
- On `PermissionRequested` the responder decides with the session's policy, answers `Allow` or `Deny` through `IAgents.RespondAsync`, leaves `Ask` pending, and publishes `PermissionDecided` with the answer, the rule that decided and whether the answer reached the agent. A request from a session it never saw open is decided by the built-in policy with no workspace, so only the default applies.
- Requests left to a human stay pending exactly as before the module existed: the turn waits in `AwaitingPermission`, never expires, and anyone may still answer through `IAgents.RespondAsync`.
- Decisions live in memory for the life of the application, like the usage aggregates.

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
| `VerificationReport` | `Job`: `JobId`; `Attempt`: the attempt number Jobs gave the gate; `Outcome`: `VerificationOutcome`; `Checks`: `IReadOnlyList<CheckEvidence>` in declaration order, empty when none were declared or the declaration is invalid; `Verdict`: the `GateVerdict` returned to Jobs, whose `Feedback` is the text sent back to the agent on `Retry` and empty on `Pass`; `VerifiedAt`: `DateTimeOffset` |
| `VerificationOutcome` | `Passed`, `Failed`, `NoChecksDeclared`, `InvalidDeclaration` |
| `CheckEvidence` | `Name`; `Command`: the command line as run, arguments containing spaces or quotes quoted; `Status`: `CheckStatus`; `ExitCode`: `Option<int>`, absent unless the process exited; `Duration`: `TimeSpan`, zero when skipped; `OutputTail` and `ErrorTail`: at most 4,000 characters each, plus the `[...]` marker |
| `CheckStatus` | `Passed`, `Failed`, `TimedOut`, `NotFound`, `Skipped` |

Live progress of a check while it runs is not published yet: the job's `JobProgressed` with `Checking` marks the whole evaluation.

### Permissions

| Data | Kind | Shape | When and how often | Cardinality |
| --- | --- | --- | --- | --- |
| `PolicyLoaded` | Event | `SessionPolicy`: session, `PolicyFileStatus` (`Absent`, `Applied` or `Rejected`), `Option<PolicyError>`, and the effective rules in decision order | When a session opens, after its policy file was read and before any of its activity is handled | One per session |
| `PermissionDecided` | Event | `PolicyDecision`: session, turn, item, `Option<JobId>`, item kind, target as matched, `PolicyAnswer`, `Option<PolicyRule>` that decided (none for the default), `DecisionDelivery` (`Answered`, `LeftToHuman` or `Undelivered`) and the time from `TimeProvider` | For every permission request the `Turn` aggregate accepted, after the answer was sent. It may follow the `PermissionResolved` that its answer caused | One per permission request |
| `IPermissionAudit.PolicyOf` | Query | `Option<SessionPolicy>` | Any time; none until the session opened | One per session |
| `IPermissionAudit.OfSession` | Query | The session's `PolicyDecision`s in decision order | Any time | Zero or more per session |
| `IPermissionAudit.OfJob` | Query | The `PolicyDecision`s of every session of a job, recovery included, by time | Any time; a session counts once `JobSessionStarted` tied it to the job | Zero or more per job |

A `PolicyRule` carries its origin (`BuiltIn` or `Repository`), name, `Option<ItemKind>`, `Option<string>` target pattern, `RuleScope` (`Anywhere` or `Workspace`) and answer, so a decision explains itself without another query.

## Delivery

**Accepted**

The application is built view model first: every screen is built and tested as view models with no user interface. Avalonia views come last, as a thin layer bound to view models that already work.

## Persistence

**Accepted**

- EF Core with the SQLite provider, with no server.
- One `DbContext` and one database file per module, under the data folder: `jobs.db`, `workspaces.db`. Separate files isolate modules for real, and each module creates its schema on its own. No module reads another module's data.
- The schema is created with `EnsureCreated`. Migrations arrive with the first schema change.
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

Database work therefore never runs on the UI thread. Each store keeps one long-lived `DbContext`, used by one operation at a time behind a `SemaphoreSlim`, and runs every operation through `Task.Run`.

### Data folder

The host registers `AvalaPaths` from the SDK. Its data folder is `AVALA_DATA_PATH` when set, otherwise `Avala` under the local application data folder. It locates the database files and the worktree root. The composition root receives it, so the host tests point it at a temporary folder.

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
