# Core action plan

Goal: a minimal core that is usable every day within a couple of days, built on the [core design](../design/core.md). Every step ends with green architecture and unit tests.

## Phase 0: Spikes

Throwaway experiments that answer questions the design depends on. Results are written down here, and the code is deleted.

| Question | Why it matters | Done when |
| --- | --- | --- |
| How well does each agent implement ACP? | Sizes the Agents module and the first providers | Claude Code, Codex and Gemini tried through ACP, gaps listed |
| Which agents stream partial output? | Decides how the canvas streams | Partial output observed or ruled out per agent |
| Does Avalonia's WebView work on Linux, and under which license? | Decides how the canvas renders | Renders on Hyprland, license confirmed |
| Does Stateless export Mermaid diagrams? | Decides whether diagrams are generated | Answered: yes, through `Stateless.Graph.MermaidGraph` |

## Phase 1: Foundations

Status: done, except the rule on provider names, which arrives with the first provider.

1. Rename the sample module from Tasks to Jobs.
2. Add `Result<TValue, TError>` with `Match` and `TryGetValue` to the SDK, with unit tests.
3. Add `IIntegrationEvent`, `IEventBus`, `IHandle<T>`, `IEventFeed` and `IUiDispatcher` to the SDK.
4. Implement the bus and the feed in `Avala.Runtime` with `System.Threading.Channels`, with unit tests for ordering, isolation of failing handlers and cancellation.
5. Add the marker interfaces `IAggregateRoot` and `IDomainEvent` to the SDK.
6. Add Stateless to central package management, with the guarded transition helper, and ban direct `Fire` calls.
7. Add ArchUnitNET and the DDD and boundary rules listed in the [core design](../design/core.md#architecture-rules-to-add).
8. Add the fixtures assembly that violates every rule on purpose, and test each rule against it.
9. Change the plugin rule to one plugin entry per module.

Done when: the SDK contracts exist, the bus is tested, and every rule detects its fixture.

## Phase 2: Jobs domain

Status: done. Attempts turned out to need no state machine of their own, see the [core design](../design/core.md#state-machines).

1. `JobId`, `AttemptBudget` and the other value objects.
2. `JobError` and the error codes.
3. `JobLifecycle` with Stateless, guarded by `CanFire`, and its diagram generated from the code.
4. The `Job` aggregate returning events inside results.
5. Unit tests for every transition, every guard and every error code.

Done when: every allowed and forbidden transition is covered by a test, and no test needs infrastructure.

## Phase 3: Agents core

Status: done. The agnostic events also carry telemetry and streaming canvases.

1. `Agents.Contracts`: `IAgentProvider`, `IAgentSession`, the agnostic events, `AgentCapabilities`, `AgentError`.
2. `TurnLifecycle` with expiry driven by `TimeProvider`.
3. A fake provider for tests that replays scripted events.
4. The skeleton of the conformance kit.

Done when: `TurnLifecycle` rejects every malformed sequence in its tests, and the fake provider passes the kit.

## Phase 4: Workspaces

Status: done. Persistence with EF Core arrived in phase 5.

1. An asynchronous process runner for git, with no blocking calls.
2. `WorkspaceLifecycle` and the `Workspace` aggregate.
3. Worktree creation and disposal, plus a checkpoint per turn.
4. Integration tests against a real temporary git repository, since this behavior cannot be verified otherwise.

Done when: a workspace is created, checkpointed and disposed on Linux and Windows.

## Phase 5: Job flow

Status: items 1 to 5 are done, with `Option<T>`, the rule that keeps nullable types out of signatures and the persistence rules that keep EF Core in `Infrastructure` and database work off the UI thread. An end-to-end integration test of the flow is being finished. The Verification module, formerly item 6, moved to [phase 8b](#phase-8b-trust) with the other modules that let an agent run unattended.

1. The job flow coordinator as event handlers.
2. `ICompletionGate`, with every attempt passing when no gate is registered.
3. Recovery on startup from stored state.
4. Persistence for Jobs and Workspaces with EF Core and SQLite, one `DbContext` and one database file per module.
5. Keep database work off the UI thread, with an architecture rule that verifies it.

Done when: a job goes from submitted to awaiting review with the fake provider, survives a restart midway, and handlers stay idempotent.

## Phase 6: First provider

Status: items 1, 6, 7 and 8 are done, the contract part of item 5 and the recording part of item 3. The provider contract now covers what the Claude Code adapter needs beyond a turn, so the adapter only translates its protocol: each addition came with the simulator implementing it and a check of the conformance kit.

1. A simulated Claude Code provider that uses only the public agent contracts: declarative scenarios chosen by a tag in the first message, real file edits, permission requests answered through `IAgents.RespondAsync`, and failure scenarios that the conformance kit must report.
2. The Claude Code provider plugin.
3. It passes the conformance kit with recorded sessions, including its resume, canvas tool and account checks. The recording side is done, see item 8: what waits is recording the adapter's first sessions, with `recording.json` on and the secrets of the machine listed in `redact`, reading them, and committing the ones that matter to `tests/recordings` with their expectations, without `record`. From then on every such session is replayed by the host tests and checked by the conformance kit on each build, and a change of the adapter that alters what it emits shows up as a failed expectation, while a change of the harness that answers differently shows up as a divergence. Where the simulator's own scenarios disagree with the recordings, the scenarios are corrected.
4. The architecture rule that keeps provider names inside their own plugin.
5. The canvas tool the harness injects through MCP, translated into the canvas events the Canvas module already consumes. Done in the contract: [harness tools](../design/core.md#harness-tools) shaped like MCP tools, the `AcceptsTools` capability, the `Canvas` surface that says how a call becomes canvas events, and the canvas tool the Canvas module offers, which the simulator's `canvas` scenario draws through. What waits for item 2: the MCP server that transports the tools to Claude Code, and the adapter's translation of its tool calls.
6. The provider contract for the real adapters: [resume tokens](../design/core.md#resuming-a-conversation) under `CanResume`, stored by Jobs and used by recovery and by `IJobs.ContinueAsync`; [harness tools](../design/core.md#harness-tools) under `AcceptsTools`; and the session's [account](../design/core.md#accounts). Host simulation tests: a job recovered after a restart resumes its simulated conversation, a job held as `SessionLost` resumes when a human continues it, the canvas scenario reaches `CanvasUpdated` through the injected tool, and usage adds up by the simulator's account.
7. The contract for the harnesses' modals: [human-input forms](../design/core.md#human-input-forms), one closed provider-agnostic format each provider translates its questions, permission prompts and plan approvals into, asked with `FormRequested` and answered with `FormAnswered` inside an item the turn waits on in `AwaitingAnswer`, answered through `IAgentSession.AnswerAsync` and `IAgents.AnswerAsync`, under the `AsksQuestions` capability; and a `Message` on `PermissionDecision`, the "no, do this instead" answer. The simulator's `question` and `plan-approval` scenarios ask forms and its denials repeat their message; the kit checks forms only by capability and well formed, answers refused for forms that are not open, and denials honored. What waits for item 2: the Claude Code adapter's translation of `AskUserQuestion`, plan approval and its permission prompts into forms and permission requests.

8. [Session recording and replay](../design/core.md#session-recording-and-replay): `IAgentProviderDecorator` in the Agents contract, applied by `SessionStarter` to every provider; the Recording plugin, off unless `recording.json` in the data folder enables it, which records each session at the agnostic level, its options, capabilities, account, events, the harness's inputs and the content of its edits, with relative times and literal redaction, in the versioned `avala-recording` format under `recordings/`, written by the single reader of a channel; the simulator's `[replay: …]` and `[replay as recorded: …]` tags, which convert a recording into a scenario, honor its inputs and report every divergence by failing the turn and closing the session; and regression fixtures in `tests/recordings`, recorded from the `edit`, `fix-after-feedback` and `question` scenarios through the real application, replayed by the host tests against their expected outcomes, round-tripped from a fresh recording, and checked by the conformance kit. Deferred: a recording that spans the sessions of one job, so a resumed session replays as the continuation of the one before; replaying with the recorded provider's identity, capabilities and account instead of the simulator's; capturing deleted and binary files; and a view of recordings in the application.

Done when: a real job runs end to end with Claude Code.

## Phase 7: Observability

Status: done with the fake provider and the simulator, aggregation by account included: `SessionOpened` carries the account the provider reports, and `IUsage.ByAccount` groups by provider and account. One item waits for the real Claude Code provider of phase 6: checking that its turns and its account show up in the aggregates. The view models of the usage dashboards moved to phase 9.

1. `SessionOpened` from Agents and `JobSessionStarted` from Jobs, so a session's activity can be tied to its provider and its job.
2. The Observability module: aggregates tokens, cost per currency, unpriced reports, limits and turns by outcome with their durations, by provider, session and job, behind `IUsage` in its contracts.
3. Metrics through `System.Diagnostics.Metrics`: the `Avala.Observability` meter with tokens, cost, turns, turn durations and limits.
4. A simulation test of the real application: a simulated job's tokens, cost, turn and limit show up in the aggregates of its job, its provider and its account.
5. Aggregation by account: the account a provider reports at session open, on `SessionOpened`, and `IUsage.ByAccount`.

Done when: every turn of the fake and real providers shows up in the aggregates, with unit tests.

## Phase 8: Canvas

Status: done. The canvas tool moved to phase 6: the module now offers its definition as a harness tool, and its MCP transport arrives with the real provider. The canvas view model moved to phase 9 and the renderers to phase 10, with the rest of the user interface.

1. The Canvas module, a plugin of its own: the `CanvasDocument` aggregate with `CanvasLifecycle` and its generated diagram, accumulating each canvas from `CanvasStarted`, `ItemProgressed` and `ItemCompleted` and rejecting foreign, repeated and late content with typed errors.
2. Snapshots throttled per canvas with `TimeProvider`, published as `CanvasUpdated` with the full content so far and flushed at once on completion.
3. `Canvas.Contracts`: `CanvasId`, `CanvasSnapshot`, `CanvasUpdated` and the `ICanvases` query of a session's current canvases.
4. A host simulation test: the simulator's `canvas` scenario delivers its SVG and Mermaid canvases, drawn through the injected canvas tool, as snapshots that grow in order and end complete.

Done when: a canvas streamed by the simulator reaches the bus as snapshots in order, with unit tests. Met.

## Phase 8b: Trust

Agents you can trust without watching: an unattended job must prove its work, act within an explicit policy, never hang forever or die silently, and never spend without limit. Each concern is a plugin of its own that joins the job flow through a contract of Jobs, and every intervention leaves evidence queryable per job.

Status: done with the simulator. What waits is listed per module.

### Verification

Status: done. Deferred: live progress of a running check, persisting the reports, and a gate verdict that asks for help at once when the committed declaration is invalid, since the agent can no longer fix it and retries only spend the attempt budget.

1. The [Verification](../design/core.md#verification) module: a completion gate that runs the checks a repository declares in `.avala/checks.json` at the job's base commit, inside the worktree, each with its timeout, sends the failures back to the agent through the retry path, and publishes the evidence of every attempt as `AttemptVerified`, queryable through `IVerifications`.
2. A host simulation test: a job whose declared check fails reaches review only after the agent fixes it.

### Permissions

Status: done with the simulator. The policy is read from the job's base commit and sessions open in `AskEveryTime`, see [Rules the agent cannot edit](#rules-the-agent-cannot-edit); checking that the real Claude Code provider asks before every edit waits for phase 6, through the conformance kit.

1. `PermissionRequested` names its item kind and its target, `SessionOpened` its working directory, and the conformance kit reports requests that name no target or another kind than their item.
2. The [Permissions](../design/core.md#permissions) module: ordered rules with first-match semantics and a default `Ask`, scoped to the workspace for file edits, with a built-in guard that sends edits of the policy file to a human.
3. A repository policy in `.avala/permissions.json`, parsed strictly; an invalid file is reported with a typed `PolicyError` and falls back to the built-in policy.
4. `PermissionResponder` answers `Allow` and `Deny` through `IAgents.RespondAsync` and leaves `Ask` pending; every decision is published as `PermissionDecided` with the rule that made it, and is queryable by session and job through `IPermissionAudit`.
5. Host simulation tests with the `permission` scenario: an allowing policy lets the job finish unattended, a denying policy is answered `Deny`, and without a policy the request awaits a human, with the decision audited in each case.

### Holding a job

Status: done.

1. [`IJobs.HoldAsync`](../design/core.md#holding-a-job) with a typed `HoldReason`: a running job goes to `NeedsHelp`, its attempt is interrupted, its session is interrupted or stopped, and `JobHeld` says why. Jobs knows no caller.
2. `IAgents.InterruptAsync`, decided by the provider's `CanInterrupt` capability; the provider contract does not change.
3. `SessionEnded` from Agents when a session's stream closes or crashes on its own, before the live turn is closed as failed; a stream that closes mid-turn no longer leaves the turn open.
4. `UsageRecorded` from Observability, so spending is judged on aggregates that already include the last report.
5. [`IJobs.ContinueAsync`](../design/core.md#holding-a-job), the caller the hint lacked: a human continues a held job with a message, in its session when it is still open, otherwise in a new session that resumes the job's conversation when the provider can, or starts over with the instruction and the message.

### Supervision

Status: done with the simulator. Resuming a job held as `SessionLost` in a new session when a human hints it is done, through `IJobs.ContinueAsync`. Deferred: stopping a session whose agent ignores the interruption, after a grace period, and persisting the interventions.

1. The [Supervision](../design/core.md#supervision) module: a running job silent for the window, outside the time a permission waits for a human, is held as `Stalled`. A job whose session ends on its own is held as `SessionLost` by Jobs itself, since [concurrent delivery](#concurrency) would make a hold from Supervision race the evaluation of the failed turn.
2. The silence window from `supervision.json` in the data folder, 15 minutes by default, parsed strictly; a rejected file keeps the default and says why.
3. Alarms measured with `TimeProvider` and confirmed in the watchdog's mailbox, after every event published before they rang, so an active agent never looks silent.
4. Every intervention published as `SupervisorIntervened` with the silence measured, and queryable per job through `ISupervision`.
5. Host simulation tests: the `hang` scenario is interrupted and held as `Stalled`, `crash` is held as `SessionLost`, and `left-open` reaches review with no intervention.

### Budgets

Status: done with the simulator. Deferred: keeping spending across restarts, an override that lets a human raise the cap of a held job, and caps across jobs or per account.

1. The [Budgets](../design/core.md#budgets) module: per-job caps on cost per currency and on tokens, and a threshold on the provider's usage limits, from `.avala/budget.json`; no caps without the file.
2. The file parsed strictly; an invalid file is reported with a typed `BudgetError` and holds the job as `InvalidBudget` as soon as it runs.
3. Spending read from `IUsage` on every `UsageRecorded`, and again whenever a job starts running.
4. Every intervention published as `BudgetIntervened` with what was measured against the cap, and queryable per job through `IBudgets`.
5. Host simulation tests: a cost cap below the `permission` scenario's cost holds the job as `BudgetExceeded`, and a limit threshold below the simulator's reported limit holds it as `LimitNearlyReached`. Both scenarios report their spending and then wait for a human to answer a permission, so the hold never races the end of the turn.

### Human input and autonomy

Status: done with the simulator. Deferred: the Claude Code adapter's translation of its modals into forms, with phase 6; refusing a request to loosen autonomy at submission instead of when the session starts, which needs the repository's level before the workspace exists; persisting decisions, forms, human answers and session rules, so a recovered session starts without the session rules of the one it replaces; human answers to forms through Permissions, so they are audited like human answers to permissions rather than only seen as `FormAnswered`; carrying the assumptions into the review evidence beside the verification reports; checking that a provider's `FormAnswered` fits its form; confining what an autonomous command touches beyond running it in the worktree; and the view models and the generic form view, in phases 9 and 10.

1. [Autonomy levels](../design/core.md#autonomy-levels): the provider's mode stays `AskEveryTime`; `supervised` leaves unmatched requests and forms to a human, `autonomous` allows edits inside the worktree and commands, denies everything else instead of asking, and answers forms. The level is declared in `.avala/permissions.json` from the base commit, and a job may tighten it at submission through `JobRequest.Autonomy`, never loosen it: Jobs stores the request and announces it with every `JobSessionStarted`, and Permissions caps the session's level and publishes `AutonomyApplied`, refusals included.
2. Built-in guards at every level: edits of the policy file and edits outside the worktree go to a human under `supervised` and are denied under `autonomous`, as is anything the repository denies or leaves to a human.
3. [Forms](../design/core.md#forms) answered by the policy under `autonomous` with the repository's strategy, `recommended` or `bestJudgment`; a permission asked through a form is declined. Every decision is published as `FormDecided` with one assumption per field, queryable per job.
4. [Session rules](../design/core.md#session-rules): `IPermissionAnswers` delivers a human's answer with its message and turns "don't ask again" into an audited rule for the exact request, in place before the agent goes on, published as `PermissionAnswered`.
5. Supervision counts a form waiting for its answer as human time, like a permission.
6. Host simulation tests: a question answered by a human through `IAgents.AnswerAsync`; the same job under `autonomous` answered with the recommended option and its assumption audited; a permission denied with a message that reaches the agent; "don't ask again" answering the next identical request; an autonomous job running a command without a matching rule and denied an edit outside its worktree; a job submitted as `supervised` in an `autonomous` repository staying supervised, and a request to loosen a supervised repository refused.

### Rules the agent cannot edit

Status: done with the simulator.

1. [Rules from the base commit](../design/core.md#rules-from-the-base-commit): Workspaces resolves the base reference to a commit when it prepares a workspace, stores it and reports it as `WorkspaceInfo.BaseCommit`; `IBaseFiles` reads a file of that commit for a worktree with git, with whether the worktree's copy differs.
2. Verification, Permissions and Budgets read `.avala/checks.json`, `.avala/permissions.json` and `.avala/budget.json` through `IBaseFiles`, recovered sessions included, keeping their behaviors for absent and invalid files, and report the file's origin in `VerificationReport`, `SessionPolicy` and `SessionBudget`. A failed verification tells the agent that its changes to the checks do not apply.
3. Agents opens every session in `AskEveryTime`, so every edit and command reaches the policy; without the Permissions plugin every request waits for a human.
4. The simulator honors the permission mode: in `AskEveryTime` every edit and command asks first. The conformance kit reports an edit or a command that goes ahead without asking when the session was asked to `AskEveryTime`.
5. Host simulation tests: every action of the `edit` scenario meets the policy and is audited, and the `rewrite-checks` scenario, which empties the check declaration, is still verified by the checks of its base commit.

Done when: a simulated job proves its work, asks permission within a policy, runs unattended at the autonomy its repository allows with every automatic answer audited, and is held with its reason recorded when it hangs, loses its session or reaches its budget. Met.

### Concurrency

Status: done.

1. [One mailbox per handler](../design/core.md#event-bus): each handler sees its events in publishing order, a slow handler delays only itself, and stopping waits for every handler to end.
2. [One queue per job](../design/core.md#job-flow-coordinator): `CheckTurn` hands the evaluation of a turn to the job's queue and returns, so the checks of one job never delay the bus or another job; holds and launches go through the same queue.
3. Every ordering between handlers made explicit: Permissions decides in the handler that loaded the policy, Budgets enforces again on `BudgetLoaded`, Jobs holds a lost session itself, and host tests wait for the event that follows a record before they query it.
4. [No lock in production code](../design/core.md#concurrency): state owned by one reader, `SerialExecutor` in the SDK for channel consumers, immutable snapshots for queries, an architecture rule over `src/` and banned types for the compiler.

## Phase 8c: Connections and delegation

Status: connections and resources done with the simulator; delegation planned. Both parts are built and proven with the simulator before the user interface and before the real Claude Code adapter, which then only translates its protocol.

### Connections

Status: done with the simulator, see [Connections](../design/core.md#connections). Deferred: a command that creates a connection's login folder and runs the harness's own login in it; credential sources beyond the first two, such as the operating system's keychain, Bedrock, Vertex or a corporate gateway, and a `ConnectionEnvironment` field for a gateway's address when the first one needs it; validating a repository's default connection at submission, which needs the base commit before the workspace exists, like autonomy; the job's connection on an event of Jobs, for the views; caps across the jobs of a connection, such as a daily spend per API key, which need spending kept across restarts; replaying a recording with the recorded provider's identity and capabilities, which belong to a provider in the contract, so only the recorded account is lifted; an architecture rule against test classes whose static fields name a contract type; and checking the isolation of the real Claude Code adapter's connections, with phase 6.

A user may hold several subscriptions of the same harness, such as a work and a personal Claude Code account, or an API key next to a subscription. Agents used to start every session on the first registered provider, so it could not tell them apart.

- A provider is the adapter that speaks one harness's protocol, one per harness, inside its plugin. A connection is a configured instance of a provider: a name, a credential source and settings. A user has any number of connections per provider.
- A connection is composed, never inherited: provider, plus credential source, plus settings. The provider receives the resolved environment when a session opens and never learns where it came from.
- The credential source is an extension point. First implementations: a subscription login kept apart per connection through the harness's own configuration folder, and an API key held as a reference to an environment variable. Later ones, such as the operating system's keychain, Bedrock, Vertex or a corporate gateway, add an implementation without touching the core or an adapter.
- Connections are declared in the data folder, never in a repository: they belong to the machine. Secrets are never stored in the file, only references to them.

1. Done. The connection model, composed of a provider, a credential source and settings; `ICredentialSource` in `Agents.Contracts`, with the `login` and `apiKey` sources registered by Agents; the strict, documented `connections.json` in the data folder and its typed `ConnectionError`; `IConnections` to list and check connections without their secrets.
2. Done. Agents opens a session on the connection `AgentRequest.Connection` names, hands the provider the resolved `ConnectionEnvironment` in `SessionOptions.Connection`, and carries the connection on `OpenedSession` and `SessionOpened`. Without `connections.json`, every registered provider has one implicit connection and the first one is the default; a rejected file never falls back to them.
3. Done. `JobRequest.Connection` names a job's connection, checked at submission, or the job takes its repository's default from `.avala/jobs.json` at its base commit, or the machine's default. The job stores the connection its session opened on, in a new column, and recovery and continuation reuse it; an unknown or unusable connection is a typed rejection or fails the job as `ConnectionUnavailable`.
4. Done. `IUsage.ByConnection` with limits per connection, and the `avala.connection` tag on every metric; Budgets judges limit thresholds by the session's connection and gives a connection its own caps through the `connections` section of `.avala/budget.json`.
5. Done. The simulator reports the account of each connection's credential, binds its resume tokens to that account, and replays the recording a connection's `replay` setting names, with the recorded account. The recorded provider identity and capabilities stay the simulator's, see the deferred list.
6. Done. `CheckConnectionsAsync`: two connections of one provider share no session, account or resume token, and a token of one is refused on the other. The simulator passes it, and a scripted provider that shares its account and tokens is reported.
7. Done. Host simulation tests: two jobs on two connections of the simulator run side by side with their usage and accounts apart; a job recovered after a restart keeps its connection and resumes its conversation there; a job without a connection runs on its repository's default; and a job naming an unknown connection is rejected.

Done when: two connections of one provider run jobs side by side with their usage, limits and caps apart, proven with the simulator. Met.

### Delegation

An orchestrating agent delegates work to sub-agents that may run on any connection: another Claude Code subscription, another harness. Unlike a harness's native sub-agents, each one is a full Avala job, governed like any other.

- Avala injects a delegation tool into the orchestrator, like the canvas tool. A call creates a child job with its own worktree, started from the parent's current state, on a connection Avala chooses by policy, never by the orchestrator naming a harness.
- When the child ends, its summary, diff and verification evidence return to the parent as the tool's result: the parent receives evidence, not a sub-agent's word.
- A child inherits the parent's autonomy level and may only tighten it; its cap is carved out of the parent's budget, so delegating cannot multiply spending; supervision and verification apply to it as to any job.
- Depth and the number of children running at once are capped, so delegation cannot recurse without end.
- The audit keeps the whole tree: who delegated what, to which connection, at what cost, and what the policies decided.

1. Harness tools the harness executes: a call to such a tool reaches Avala and its result returns to the agent, in the provider contract with the simulator implementing it and a conformance check. Claude Code receives them later through the MCP server of phase 6.
2. Parent and child jobs in the Jobs domain, with the tree in its contracts and its persistence.
3. The delegation tool and the routing policy that picks a child's connection, starting with the repository's declared rules.
4. Inherited autonomy, budgets carved from the parent's cap, and the depth and fan-out caps.
5. Bringing a child's work into the parent's worktree once the child is verified, and a typed outcome when it conflicts.
6. A simulator scenario in which an orchestrator delegates to children on two connections, and host simulation tests: children run governed and in parallel, their evidence returns to the parent, a child cannot loosen autonomy or exceed its carved budget, and the depth cap stops a recursion.

Done when: a simulated orchestrator delegates to simulated children on two connections and receives their verified results, with the tree audited.

### Resources

Status: done with the simulator, after connections and before delegation, since delegation multiplies the processes, ports and worktrees an unattended run leaves behind. See [Process trees](../design/core.md#process-trees) and [Resources](../design/core.md#resources). Linux and Windows contain processes and run in CI; macOS compiles and is best effort, untested by CI. Deferred: a delegated cgroup on Linux where systemd user delegation is available, for memory and CPU measured and limited by the kernel; starting a Windows process inside its job rather than assigning it right after it starts, which needs `CreateProcess` with a job list; persisting samples, orphan reports, leases and the retention due of ended jobs across restarts; an approval command in Jobs, so an approved job's worktree retention applies through the application; admitting continuations and recoveries against the limit of running jobs; a memory cap across the jobs of a machine; CPU caps; checking that the real Claude Code adapter starts its process through the session's launcher and passes the port variables on, with phase 6; and the views of resources, with phases 9 and 10.

Agents and the commands they run leave resources behind: processes that outlive their session, such as test hosts and build servers, ports two worktrees fight over, and worktrees that pile up on disk. Avala accounts for every resource its jobs use and reclaims what they leave.

- Every process an agent starts belongs to its session's process tree, contained by the operating system where it can: a job object on Windows, a process group or a delegated cgroup on Linux, a process group on macOS. The platform specifics live behind one infrastructure port.
- Memory and CPU are sampled per tree and attributed to the job, the session, the connection and the provider; disk is measured per worktree, plus Avala's own data folder.
- When a session or a job ends, its tree is reaped. A process still alive afterwards is an orphan: reported with the job it came from, and killed by policy or by a human.
- Ports are leased per worktree from a range and handed to the agent's environment, and the listening sockets of each tree are observed, so collisions show up before they break a run.
- Worktrees of finished or discarded jobs are reclaimed by a retention policy, and worktrees on disk that no workspace knows, or workspaces whose folder is gone, are reported.
- Budgets may also cap resources: memory per job and the number of jobs running at once, so parallel builds cannot exhaust the machine.

1. Done. Process trees per session behind `IProcessTrees` in the SDK and the runtime's `IContainment` port: `setsid`, the tree's environment marker and descent on Linux, a job object on Windows, the marker and descent through `ps` on macOS. The harness hands every session a launcher, `SessionOptions.Processes`, that starts processes inside its tree, announced by `SessionOpened.ProcessTree`; `IProcessRunner` starts processes run in a tree's folder, the checks and the checkpoint commands, inside that tree. The simulator starts its processes through the launcher, and `CheckProcessesAsync` reports a provider that starts none through it.
2. Done. The Resources module samples the memory, CPU time and load, and listening ports of every tree, and the disk of every worktree and of the data folder, on a `TimeProvider` timer, as one `ResourcesSampled` per interval, queryable through `IResources` globally and by job, session, connection and provider.
3. Done. A session that ends with processes alive in its tree reports them as `OrphansFound` with its job; the default policy kills them, `report` leaves them to `IOrphans.ReapAsync`; every report is audited. Jobs stops the session of a job that ends, and discards a job through `IJobs.DiscardAsync`.
4. Done. Port leases per worktree from the range in `resources.json`, handed to every process of the worktree's trees as `AVALA_PORT` and `AVALA_PORTS`, released when the job ends; a leased port held outside its worktree is published as `PortConflictObserved`.
5. Done. Worktrees of ended jobs are reclaimed by the retention in `resources.json`, and `IWorkspaces` reconciles the worktree root with the store, reported at startup and cleaned by policy or command.
6. Done. `memoryPerJobMegabytes` in `.avala/budget.json` holds a job as `MemoryExceeded`; `budgets.json` in the data folder limits the jobs running at once through `IJobAdmission`, and queued jobs start, in order, when a slot frees.
7. Done. The simulator's `processes` scenario starts real processes of the `Avala.Simulator.Workload` program, leaves a server running past the session listening on the leased port. Host simulation tests: the orphan is reported with its job and reaped, and is gone; the lease reaches the server's environment; usage is attributed to its job, its connection and provider, and counted globally; a discarded job's worktree is reclaimed; and the limit of running jobs queues a job until a slot frees.

Done when: a simulated job that leaks a process and holds a port is reaped and reclaimed, with every resource attributed to its job and visible globally. Met.

## Phase 9: View models of the usable core

The whole application works through view models, with no user interface.

1. Job list, new job and job detail.
2. Timeline projection and the activity view: messages, reasoning, tools and canvases, the canvas view model reading `ICanvases` and following `CanvasUpdated`.
3. Diff review: approve, send back or discard.
4. Usage dashboards over `IUsage`: by provider, by job and by session.
5. Answering agents: one generic form view model that renders any `AgentForm` and answers it through `IAgents.AnswerAsync`, permission prompts answered through `IPermissionAnswers` with a message and "don't ask again", and the automatic decisions and assumptions of a job for its review, all designed from the data in the [catalog](../design/core.md#data-the-harness-produces).

Done when: the full job flow runs end to end through view models in tests.

## Phase 10: Views

1. Avalonia views for every view model, semi-transparent with themes, Inter for the interface and JetBrains Mono for code.
2. The canvas surface for each media type, with renderer plugins registered by media type.

Done when: the harness replaces a terminal for daily work.
