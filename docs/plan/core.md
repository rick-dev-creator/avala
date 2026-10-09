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

Status: items 1, 6 and 7 are done, and the contract part of item 5. The provider contract now covers what the Claude Code adapter needs beyond a turn, so the adapter only translates its protocol: each addition came with the simulator implementing it and a check of the conformance kit.

1. A simulated Claude Code provider that uses only the public agent contracts: declarative scenarios chosen by a tag in the first message, real file edits, permission requests answered through `IAgents.RespondAsync`, and failure scenarios that the conformance kit must report.
2. The Claude Code provider plugin.
3. It passes the conformance kit with recorded sessions, including its resume, canvas tool and account checks.
4. The architecture rule that keeps provider names inside their own plugin.
5. The canvas tool the harness injects through MCP, translated into the canvas events the Canvas module already consumes. Done in the contract: [harness tools](../design/core.md#harness-tools) shaped like MCP tools, the `AcceptsTools` capability, the `Canvas` surface that says how a call becomes canvas events, and the canvas tool the Canvas module offers, which the simulator's `canvas` scenario draws through. What waits for item 2: the MCP server that transports the tools to Claude Code, and the adapter's translation of its tool calls.
6. The provider contract for the real adapters: [resume tokens](../design/core.md#resuming-a-conversation) under `CanResume`, stored by Jobs and used by recovery and by `IJobs.ContinueAsync`; [harness tools](../design/core.md#harness-tools) under `AcceptsTools`; and the session's [account](../design/core.md#accounts). Host simulation tests: a job recovered after a restart resumes its simulated conversation, a job held as `SessionLost` resumes when a human continues it, the canvas scenario reaches `CanvasUpdated` through the injected tool, and usage adds up by the simulator's account.
7. The contract for the harnesses' modals: [human-input forms](../design/core.md#human-input-forms), one closed provider-agnostic format each provider translates its questions, permission prompts and plan approvals into, asked with `FormRequested` and answered with `FormAnswered` inside an item the turn waits on in `AwaitingAnswer`, answered through `IAgentSession.AnswerAsync` and `IAgents.AnswerAsync`, under the `AsksQuestions` capability; and a `Message` on `PermissionDecision`, the "no, do this instead" answer. The simulator's `question` and `plan-approval` scenarios ask forms and its denials repeat their message; the kit checks forms only by capability and well formed, answers refused for forms that are not open, and denials honored. What waits for item 2: the Claude Code adapter's translation of `AskUserQuestion`, plan approval and its permission prompts into forms and permission requests.

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
