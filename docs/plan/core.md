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

Status: item 1 is done.

1. A simulated Claude Code provider that uses only the public agent contracts: declarative scenarios chosen by a tag in the first message, real file edits, permission requests answered through `IAgents.RespondAsync`, and failure scenarios that the conformance kit must report.
2. The Claude Code provider plugin.
3. It passes the conformance kit with recorded sessions.
4. The architecture rule that keeps provider names inside their own plugin.
5. The canvas tool the harness injects through MCP, translated into the canvas events the Canvas module already consumes.

Done when: a real job runs end to end with Claude Code.

## Phase 7: Observability

Status: done with the fake provider and the simulator. Two items wait for the real Claude Code provider of phase 6: aggregating by account, since no event carries the account yet, and checking that its turns show up in the aggregates. The view models of the usage dashboards moved to phase 9.

1. `SessionOpened` from Agents and `JobSessionStarted` from Jobs, so a session's activity can be tied to its provider and its job.
2. The Observability module: aggregates tokens, cost per currency, unpriced reports, limits and turns by outcome with their durations, by provider, session and job, behind `IUsage` in its contracts.
3. Metrics through `System.Diagnostics.Metrics`: the `Avala.Observability` meter with tokens, cost, turns, turn durations and limits.
4. A simulation test of the real application: a simulated job's tokens, cost, turn and limit show up in the aggregates of its job and its provider.

Done when: every turn of the fake and real providers shows up in the aggregates, with unit tests.

## Phase 8: Canvas

Status: done. The MCP canvas tool moved to phase 6, with the real provider; the canvas view model to phase 9 and the renderers to phase 10, with the rest of the user interface.

1. The Canvas module, a plugin of its own: the `CanvasDocument` aggregate with `CanvasLifecycle` and its generated diagram, accumulating each canvas from `CanvasStarted`, `ItemProgressed` and `ItemCompleted` and rejecting foreign, repeated and late content with typed errors.
2. Snapshots throttled per canvas with `TimeProvider`, published as `CanvasUpdated` with the full content so far and flushed at once on completion.
3. `Canvas.Contracts`: `CanvasId`, `CanvasSnapshot`, `CanvasUpdated` and the `ICanvases` query of a session's current canvases.
4. A host simulation test: the simulator's `canvas` scenario delivers its SVG and Mermaid canvases as snapshots that grow in order and end complete.

Done when: a canvas streamed by the simulator reaches the bus as snapshots in order, with unit tests. Met.

## Phase 8b: Trust

Agents you can trust without watching: an unattended job must prove its work, act within an explicit policy, never hang forever or die silently, and never spend without limit. Each concern is a plugin of its own that joins the job flow through a contract of Jobs, and every intervention leaves evidence queryable per job.

Status: done with the simulator. What waits is listed per module.

### Verification

Status: done. Deferred: reading the declaration from the job's base commit so the agent cannot change it, live progress of a running check, and persisting the reports.

1. The [Verification](../design/core.md#verification) module: a completion gate that runs the checks a repository declares in `.avala/checks.json` inside the worktree, each with its timeout, sends the failures back to the agent through the retry path, and publishes the evidence of every attempt as `AttemptVerified`, queryable through `IVerifications`.
2. A host simulation test: a job whose declared check fails reaches review only after the agent fixes it.

### Permissions

Status: done with the simulator. Reading the policy from the committed base rather than the worktree, and opening sessions in `AskEveryTime` so that edits also reach the policy, wait for the real Claude Code provider of phase 6.

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

### Supervision

Status: done with the simulator. Deferred: stopping a session whose agent ignores the interruption, after a grace period; resuming a job held as `SessionLost` in a new session when a human hints it; and persisting the interventions.

1. The [Supervision](../design/core.md#supervision) module: a running job silent for the window, outside the time a permission waits for a human, is held as `Stalled`. A job whose session ends on its own is held as `SessionLost` by Jobs itself, since [concurrent delivery](#concurrency) would make a hold from Supervision race the evaluation of the failed turn.
2. The silence window from `supervision.json` in the data folder, 15 minutes by default, parsed strictly; a rejected file keeps the default and says why.
3. Alarms measured with `TimeProvider` and confirmed in the watchdog's mailbox, after every event published before they rang, so an active agent never looks silent.
4. Every intervention published as `SupervisorIntervened` with the silence measured, and queryable per job through `ISupervision`.
5. Host simulation tests: the `hang` scenario is interrupted and held as `Stalled`, `crash` is held as `SessionLost`, and `left-open` reaches review with no intervention.

### Budgets

Status: done with the simulator. Deferred: reading the budget from the job's base commit, keeping spending across restarts, an override that lets a human raise the cap of a held job, and caps across jobs or per account.

1. The [Budgets](../design/core.md#budgets) module: per-job caps on cost per currency and on tokens, and a threshold on the provider's usage limits, from `.avala/budget.json`; no caps without the file.
2. The file parsed strictly; an invalid file is reported with a typed `BudgetError` and holds the job as `InvalidBudget` as soon as it runs.
3. Spending read from `IUsage` on every `UsageRecorded`, and again whenever a job starts running.
4. Every intervention published as `BudgetIntervened` with what was measured against the cap, and queryable per job through `IBudgets`.
5. Host simulation tests: a cost cap below the `permission` scenario's cost holds the job as `BudgetExceeded`, and a limit threshold below the simulator's reported limit holds it as `LimitNearlyReached`. Both scenarios report their spending and then wait for a human to answer a permission, so the hold never races the end of the turn.

Done when: a simulated job proves its work, asks permission within a policy, and is held with its reason recorded when it hangs, loses its session or reaches its budget. Met.

### Concurrency

Status: done.

1. [One mailbox per handler](../design/core.md#event-bus): each handler sees its events in publishing order, a slow handler delays only itself, and stopping waits for every handler to end.
2. [One queue per job](../design/core.md#job-flow-coordinator): `CheckTurn` hands the evaluation of a turn to the job's queue and returns, so the checks of one job never delay the bus or another job; holds and launches go through the same queue.
3. Every ordering between handlers made explicit: Permissions decides in the handler that loaded the policy, Budgets enforces again on `BudgetLoaded`, Jobs holds a lost session itself, and host tests wait for the event that follows a record before they query it.
4. [No lock in production code](../design/core.md#concurrency): state owned by one reader, `SerialExecutor` in the SDK for channel consumers, immutable snapshots for queries, an architecture rule over `src/` and banned types for the compiler.

## Phase 9: View models of the usable core

The whole application works through view models, with no user interface.

1. Job list, new job and job detail.
2. Timeline projection and the activity view: messages, reasoning, tools and canvases, the canvas view model reading `ICanvases` and following `CanvasUpdated`.
3. Diff review: approve, send back or discard.
4. Usage dashboards over `IUsage`: by provider, by job and by session.

Done when: the full job flow runs end to end through view models in tests.

## Phase 10: Views

1. Avalonia views for every view model, semi-transparent with themes, Inter for the interface and JetBrains Mono for code.
2. The canvas surface for each media type, with renderer plugins registered by media type.

Done when: the harness replaces a terminal for daily work.
