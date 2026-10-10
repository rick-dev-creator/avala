# Core action plan

Goal: a minimal core that is usable every day within a couple of days, built on the [core design](../design/core.md). Every step ends with green architecture and unit tests.

Order: everything is built and proven against the simulated provider first, the user interface included: phase 8c, then phases 9 and 10 running on the simulator, following the approved [design brief](../design/ui-brief.md). Real harnesses come last: the Claude Code adapter of phase 6, items 2 to 5, is added only once the whole application works on the simulator, and then only translates its protocol into the contract the simulator already honors.

## Phase 0: Spikes

Throwaway experiments that answer questions the design depends on. Results are written down here, and the code is deleted.

| Question | Why it matters | Done when |
| --- | --- | --- |
| How well does each agent implement ACP? | Sizes the Agents module and the first providers | Claude Code, Codex and Gemini tried through ACP, gaps listed |
| Which agents stream partial output? | Decides how the canvas streams | Partial output observed or ruled out per agent |
| Does Avalonia's WebView work on Linux, and under which license? | Decides how the canvas renders | Renders on Hyprland, license confirmed |
| Does Stateless export Mermaid diagrams? | Decides whether diagrams are generated | Answered: yes, through `Stateless.Graph.MermaidGraph` |

## Phase 1: Foundations

Status: done. The rule on provider names arrived with the first provider, in phase 6.

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

Status: done. Real jobs ran end to end with Claude Code: an edit with a canvas, a question answered by the autonomous policy and a command denied by a repository rule, plus direct sessions that called a harness tool, resumed and ran on a second login. The provider contract covered what the adapter needed beyond a turn, so the adapter only translates its protocol, see [the Claude Code provider](../design/claude-code.md). Left for later: a resumed session recorded as the continuation of the one before. The canvas streamed in chunks and the `Task*` tools as a plan were done in [phase D](#phase-d-claude-code-gaps).

1. A simulated Claude Code provider that uses only the public agent contracts: declarative scenarios chosen by a tag in the first message, real file edits, permission requests answered through `IAgents.RespondAsync`, and failure scenarios that the conformance kit must report.
2. Done. The Claude Code provider plugin, `Avala.ClaudeCode`: the CLI in its streaming JSON mode, started through the session's launcher with only the connection's configuration folder or key; its stream translated into items, plan, usage with cost, limits, resume tokens and turn ends; every acting tool sent to Avala's policy through a `PreToolUse` hook and the permission prompt tool of Avala's own MCP server, which runs in the session over the CLI's control channel; `AskUserQuestion` and plan approval as forms; the harness tools served by the same server; and a discovery of the machine's logins, one connection per configuration folder. Later: the user's configuration of that folder alone, `CLAUDE.md`, skills, plugins and MCP servers, loaded under Avala's permissions with the user's hooks off, switched per connection by `userConfiguration` and `userHooks`, see [the user's configuration](../design/claude-code.md#the-users-configuration).
3. Done. It passes the conformance kit with recorded sessions: the agnostic recordings `claude-code-edit`, `claude-code-question` and `claude-code-denial` in `tests/recordings`, replayed by the host tests and the simulator, and the protocol transcripts in `tests/transcripts/claude-code`, replayed against the adapter itself by a fake `claude`, for the canvas, process, form, denial, harness tool, resume and two-login checks. Real sessions run only with `AVALA_REAL_CLAUDE=1`.
4. Done. `ProviderNamesStayInsideTheirPluginAsync`: the Claude Code provider's identifier, its configuration variables and its protocol's names appear nowhere in `src` outside its plugin, except in design-time data.
5. Done. The canvas tool the harness injects through MCP, translated into the canvas events the Canvas module already consumes: [harness tools](../design/core.md#harness-tools) shaped like MCP tools, the `AcceptsTools` capability, the `Canvas` surface, the canvas tool the Canvas module offers, and Avala's MCP server in the Claude Code plugin that lists and answers them.
6. The provider contract for the real adapters: [resume tokens](../design/core.md#resuming-a-conversation) under `CanResume`, stored by Jobs and used by recovery and by `IJobs.ContinueAsync`; [harness tools](../design/core.md#harness-tools) under `AcceptsTools`; and the session's [account](../design/core.md#accounts). Host simulation tests: a job recovered after a restart resumes its simulated conversation, a job held as `SessionLost` resumes when a human continues it, the canvas scenario reaches `CanvasUpdated` through the injected tool, and usage adds up by the simulator's account.
7. The contract for the harnesses' modals: [human-input forms](../design/core.md#human-input-forms), one closed provider-agnostic format each provider translates its questions, permission prompts and plan approvals into, asked with `FormRequested` and answered with `FormAnswered` inside an item the turn waits on in `AwaitingAnswer`, answered through `IAgentSession.AnswerAsync` and `IAgents.AnswerAsync`, under the `AsksQuestions` capability; and a `Message` on `PermissionDecision`, the "no, do this instead" answer. The simulator's `question` and `plan-approval` scenarios ask forms and its denials repeat their message; the kit checks forms only by capability and well formed, answers refused for forms that are not open, and denials honored. What waits for item 2: the Claude Code adapter's translation of `AskUserQuestion`, plan approval and its permission prompts into forms and permission requests.

8. [Session recording and replay](../design/core.md#session-recording-and-replay): `IAgentProviderDecorator` in the Agents contract, applied by `SessionStarter` to every provider; the Recording plugin, off unless `recording.json` in the data folder enables it, which records each session at the agnostic level, its options, capabilities, account, events, the harness's inputs and the content of its edits, with relative times and literal redaction, in the versioned `avala-recording` format under `recordings/`, written by the single reader of a channel; the simulator's `[replay: …]` and `[replay as recorded: …]` tags, which convert a recording into a scenario, honor its inputs and report every divergence by failing the turn and closing the session; and regression fixtures in `tests/recordings`, recorded from the `edit`, `fix-after-feedback` and `question` scenarios through the real application, replayed by the host tests against their expected outcomes, round-tripped from a fresh recording, and checked by the conformance kit. Deferred: a recording that spans the sessions of one job, so a resumed session replays as the continuation of the one before; replaying with the recorded provider's identity, capabilities and account instead of the simulator's; capturing deleted and binary files; and a view of recordings in the application.

Done when: a real job runs end to end with Claude Code.

## Phase 6b: Capabilities as components

Status: done, with Claude Code and the simulator, before any second real harness. See [capability components](../design/core.md#capability-components).

A provider declared its capabilities as a closed record of booleans: every new capability changed the contract and every provider, and nothing said how a capability works or let it differ per connection. Capabilities became components, in the spirit of an entity component system:

- A component is an immutable record in the contracts that states a capability and its data, such as `Resumable(Scope)`, `ReportsLimits(Windows)`, `AcceptsTools(Surfaces)` or `StreamsPartialOutput(Granularity)`.
- Providers attach the components they support; a connection may add or refine components, such as cost reporting for an API key or the limit windows of a subscription.
- The core's systems query components as `Option<T>` and act on what is present; a plugin may define a new component without touching the base contract, and systems that don't know it ignore it.
- The conformance kit verifies each declared component against the provider's behaviour.
- Components are listed in a documented catalog, like the data catalog, and are never a bag of strings.

The components are designed from what the Claude Code adapter and the selection by capacity actually need.

What was built:

1. Done. `Avala.Agents.Contracts.Capabilities`: `ICapability`, the typed `CapabilitySet` keyed by component type, with `Get<T>()` as an `Option<T>`, `Has<T>()`, `With` to attach or refine and `Without<T>()`, and `ValueSet<T>`, a set with value equality for a component's data. `AgentCapabilities` is removed.
2. Done. The catalog: `StreamsPartialOutput`, `ExposesReasoning`, `Interruptible`, `Resumable`, `AcceptsTools(Surfaces)`, `AsksForms`, `ReportsUsage`, `ReportsCost(Currency)` and `ReportsLimits(Windows)`, the nine booleans with the data the core and the kit read. Planning, a resume scope and a streaming granularity were left out: no system reads them.
3. Done. Per connection: `IAgentProvider.CapabilitiesOn(ConnectionEnvironment)`. Claude Code and the simulator declare `ReportsLimits` with the subscription's windows on a login and none on an API key, and the simulator then leaves out the limits it would report.
4. Done. Agents reads the set once per session, on its connection: `SessionStarter` gives a provider only the harness tools of the surfaces it accepts and a resume token only when it is `Resumable`, and `AgentSessions` decides interruption, forms, tool results and `SessionResumable` from the set kept with the live session.
5. Done. The recorder writes the components of the session's connection, each named after its type with its data, a plugin's own component included; the committed recordings carry the new shape.
6. Done. The conformance kit checks, on every turn, usage, cost and its currency, limits and their windows, reasoning and partial output against the declared components; `CheckReportsAsync` and `CheckInterruptAsync` check that what is declared happens. The simulator, on a login and on an API key, and Claude Code, through its recorded transcripts, pass them.
7. Done. An architecture rule: every component is a sealed immutable record in a `Contracts` namespace.
8. Done. End to end through the simulator: its connection settings `withoutCapabilities` and `toolSurfaces` play a harness that lacks a component, its sessions adapt to what they declare, and `CapabilityTests` in the host tests prove every component, and the API key refinement of `ReportsLimits`, inside the composed application; the conformance kit checks each of those connections.

Deferred: whether Claude Code emits `rate_limit_event` on an API key, to be observed with a real key; a view of a connection's capabilities in the settings page, when a person needs it; and a reader of the recorded capabilities, which nothing interprets yet.

## Phase D: Claude Code gaps

Status: done, proven through the simulator and through crafted Claude Code transcripts replayed by the real adapter in the composed application; one real run confirmed the plan-mode shape. See [the Claude Code provider](../design/claude-code.md).

An audit found Claude Code behaviour tested only with synthetic JSON, or not handled. Each gap got acceptance criteria, a simulator scenario where the behaviour is agnostic, a transcript in `tests/transcripts/claude-code` crafted from the CLI's shapes, the agnostic recording that transcript produces in `tests/recordings`, and a conformance check.

1. Done. **Plans.** AC1: given `TaskCreate` calls, each subject is a pending step of `PlanUpdated` and no item opens. AC2: given the result `Task #<id> created successfully`, `TaskUpdate` of that id moves its step to in progress or done, and `deleted` removes it. AC3: a failed `TaskCreate` drops its step. AC4: a subagent's `TodoWrite` never changes the job's plan. `CLAUDE_CODE_ENABLE_TASKS=0` is no longer forced. Recording `claude-code-tasks`.
2. Done. **Tool rows.** AC5: every tool's `ItemStarted` carries its input: an edit's replaced and new lines, a `MultiEdit`'s edits, a `Write`'s content, a command line with its heredoc, a search's pattern and path, a fetch's URL and prompt, a web search's query, a subagent's prompt, a `ToolSearch` query titled `Load <tools>`; the conversation's tool rows show it. AC6: a subagent's forwarded text grows its own item, never a message of the parent, and its final result is not repeated; its tool calls are items of their own. AC7: a file written through a command is captured by the recorder and recreated by the replay. AC8: plan approval as observed: the plan Claude writes to its configuration folder's `plans` folder is not asked as an edit, and is the context of the approval form when `ExitPlanMode`'s input is empty. AC9: a turn stopped mid-reply by supervision ends `Interrupted`, its open message cancelled, the job held as stalled, also when the recording is replayed by the simulator. Simulator scenario `tools`; recordings `tools`, `claude-code-tools`, `claude-code-plan-approval`, `claude-code-interrupt`; real recording `claude-code-real-plan-approval`.
3. Done. **Streamed canvas.** AC10: the canvas opens once its title and media type are complete in the partial input and grows with every `input_json_delta` that lengthens its content, an escape split across chunks held back; the final input adds only what is missing. Recording `claude-code-canvas`.
4. Done. **Resume.** AC11: a Claude Code job cut short by a restart is recovered with its token, resumed with `--resume` and the same session id, and reports only the cost the resumed turn added. Recording `claude-code-resume`.
5. Done. **Long harness calls.** AC12: the CLI is given the longest MCP tool timeout it accepts, through the server's `timeout` and `MCP_TOOL_TIMEOUT`, with auto-backgrounding off. AC13: a delegated call whose child waits two hours of the test clock on a person is answered with the child's report when it finishes, and the parent is never held as stalled, through the adapter and through the simulator's replay, which now replays recorded tool calls. Simulator scenario `delegate-waiting`; recording `claude-code-delegate`.

Deferred: a run on a model that enables the task tools, a real subagent with forwarded text, and whether the CLI writes the plan file without a prompt once the hook leaves it alone; nesting a subagent's tool items under its own item, which the contract cannot express.

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
4. A host simulation test: the simulator's `canvas` scenario delivers its SVG and Markdown canvases, drawn through the injected canvas tool, as snapshots that grow in order and end complete.

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

Status: connections, resources, review and approval, autopilot and delegation done with the simulator. Both parts are built and proven with the simulator before the user interface and before the real Claude Code adapter, which then only translates its protocol.

### Connections

Status: done with the simulator, see [Connections](../design/core.md#connections). Deferred: a command that creates a connection's login folder and runs the harness's own login in it; credential sources beyond the first two, such as the operating system's keychain, Bedrock, Vertex or a corporate gateway, and a `ConnectionEnvironment` field for a gateway's address when the first one needs it; validating a repository's default connection at submission, which needs the base commit before the workspace exists, like autonomy; the job's connection on an event of Jobs, for the views; caps across the jobs of a connection, such as a daily spend per API key, which spending stored by time window, done with review and approval, now makes possible; replaying a recording with the recorded provider's identity and capabilities, which belong to a provider in the contract, so only the recorded account is lifted; an architecture rule against test classes whose static fields name a contract type; and checking the isolation of the real Claude Code adapter's connections, with phase 6.

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

### Several accounts of one harness

Status: done with the simulator, see [Discovery](../design/core.md#discovery) and [Choosing a connection by capacity](../design/core.md#choosing-a-connection-by-capacity). Deferred: the Claude Code plugin's discovery, which its plugin implements against the contract and the kit; finding new accounts while the application runs, since discovery runs once; a command of the Workbench that continues a held job on another connection, which `IJobs.ContinueOnAsync` already offers; showing `ConnectionChosen` in the Usage and Overview pages; and weighing a connection's cost, such as an API key billed per token, against a subscription's remaining window.

A machine may hold several subscriptions of the same harness. Avala finds them and always uses the one with capacity, unless something names a connection.

1. Done. `IConnectionDiscovery` in `Agents.Contracts`: a provider plugin reports the connections it finds as references, never secrets. `ConnectionRegistry` merges them: declared connections win, discovered ones fill in, they replace a provider's implicit connection without a file, and a rejected file still leaves nothing usable. The catalog says each connection's origin, and the settings page shows it.
2. Done. `CheckDiscoveryAsync` in the conformance kit: one provider, valid and stable names, no repeated name or credential, references only. The simulator discovers every folder under `simulated-logins` as a login and passes it.
3. Done. `IConnectionSelector` in `Jobs.Contracts`, implemented by Budgets: among the candidates, the one with the most remaining capacity from the latest readings per connection, skipping those at their hold threshold in a window that has not reset yet, a connection without readings available, ties to the first listed. A job follows the precedence named connection, repository default, then capacity, with `auto` in `.avala/jobs.json` asking for capacity, and `ConnectionChosen` carries the readings compared.
4. Done. Delegation routes by `capacity` by default among the listed connections, `leastUsed` kept as its earlier name, and a section without connections lets its children follow the precedence of any job. A loop that names no connection pauses only when every connection of the default provider is at its limit.
5. Done. `IJobs.ContinueOnAsync` moves a held job to another connection as a new conversation, with typed rejections; a job is never moved on its own.
6. Done. Host simulation tests: two simulated accounts discovered without `connections.json`; a job naming nothing runs on the first while neither has readings, and on the other once the first is near its limit; a connection the repository names is used near its limit and the budget holds the job; delegated children go to the listed account with capacity; and a job held at one account's limit continues on the other when asked.

Done when: two accounts of one harness are found without configuration and jobs, children and loops go to the one with capacity, proven with the simulator. Met.

### The default connection

Status: done with the simulator, see [the connections file](../design/core.md#connections-file), [Choosing a connection by capacity](../design/core.md#choosing-a-connection-by-capacity) and [the global pages](../design/core.md#the-global-pages). Deferred: keeping why a job ran where it did across restarts, since `ConnectionChosen` lives on the board in memory; the reason of a connection named on the job, by the repository or fixed by the machine, which the inspector does not tell apart yet; and previewing again on its own when a usage reading arrives while the New job page is open.

The person chooses the machine's default connection: `Auto`, recommended, which chooses by capacity, or one fixed connection. The precedence stays a connection named on the job, then the repository's, then the machine's default.

1. Done. `connections.json` `default` accepts the reserved `"auto"`, a missing `default` or file means `Auto`, and a file may hold only the default and leave the connections to discovery. A named default is checked against the merged connections, so one that disappeared rejects the file as `UnknownDefault`, never a silent fallback. `IConnections.ChangeDefaultAsync` rewrites only `default`, one write at a time, and the catalog's `DefaultMode` says `Auto` or `Fixed`.
2. Done. Jobs asks for capacity only under `Auto`, opens a job on a fixed default as it is, and `IConnectionPreview` answers where a job submitted now would run and why, at the repository's current commit, without announcing anything. Autopilot's loop watches the fixed default alone.
3. Done. Settings edits the default as a child component with its recommendation and explanation; New job preselects `Auto` or the fixed default with one line saying what it would pick now and why, keeps a connection chosen by hand, even at its limit, and follows a change of the default while open; the conversation's place line shows the connection the job actually runs on, and the inspector the reason of its `ConnectionChosen` with the readings compared.
4. Done. Scripted view model and headless view tests, among them no connections, every connection at its limit, a default whose discovered connection disappeared, a chosen connection that disappeared, a default switched while New job is open and a late preview of an earlier repository; and host simulation tests with two simulated accounts: New job says where `Auto` goes and the job runs there with its reason in the inspector, and a default fixed in Settings runs jobs that name nothing there without choosing.

Done when: the person chooses the default connection in Settings, New job says where a job would run and why, and a job shows where it ran and why, proven with the simulator. Met.

### Delegation

Status: done with the simulator, see [Delegation](../design/core.md#delegation). Persisting the records and answering a parent with the results of the children that settled while it was gone, after a restart or a hold, are done in [Phase B](#b-delegation-robust). Deferred: replaying a recorded tool call in the simulator, and the MCP transport of the harness tools with the real Claude Code adapter in phase 6, whose parallel calls `CheckParallelToolCallsAsync` will check; checking that the real adapter keeps a call pending for hours; routing by cost, by a connection's own caps or by the provider's declared strengths, and routing rules from the machine as well as the repository; a child's own attempts per round and a timeout for a child; letting the orchestrator cancel a child, or ask for its partial progress; admitting children against the machine's limit of running jobs, which today they bypass inside their parent's slot; a memory cap carved like cost and tokens; the status of ended children across restarts, which carves count as open until told otherwise; a child integrated into a parent that waits for review or is being checked, which is refused rather than queued; EF Core migrations for the new `Parent`, `Rules` and `Carves` schema; and the delegation tree as a view, with phases 9 and 10, from the queries and events already listed in the data catalog.

An orchestrating agent delegates work to sub-agents that may run on any connection: another Claude Code subscription, another harness. Unlike a harness's native sub-agents, each one is a full Avala job, governed like any other.

- Avala injects a delegation tool into the orchestrator, like the canvas tool. A call creates a child job with its own worktree, started from the parent's current state, on a connection Avala chooses by policy, never by the orchestrator naming a harness.
- When the child ends, its summary, diff and verification evidence return to the parent as the tool's result: the parent receives evidence, not a sub-agent's word.
- A child inherits the parent's autonomy level and may only tighten it; its cap is carved out of the parent's budget, so delegating cannot multiply spending; supervision and verification apply to it as to any job.
- Depth and the number of children running at once are capped, so delegation cannot recurse without end.
- The audit keeps the whole tree: who delegated what, to which connection, at what cost, and what the policies decided.

1. Done. Harness tools the harness executes, in the provider contract with the simulator implementing them: the `Executed` surface, `ToolCalled` and `ToolReturned`, `IAgents.ReturnAsync` and `IAgentSession.ReturnAsync`, first done with autopilot's follow-ups. For delegation: a call answered long after it was made and several calls pending at once, answered in any order, which the simulator's `CallTools` step plays and the new kit check `CheckParallelToolCallsAsync` requires; and Supervision pauses a job's silence while any call of its session is pending, like a form. Replaying a recorded call and the MCP transport are deferred.
2. Done. `JobRequest.Parent`, checked at submission (`UnknownParent`, `ParentNotRunning`), the job's `Parent` column, `JobSubmitted.Parent` and `JobSummary.Parent`, and `IJobCatalog.ChildrenAsync` and `TreeAsync`. A child asks no admission and starts from a checkpoint of its parent taken in the parent's queue; a workspace now reads its rule files from a rules commit, the parent's for a child, so an orchestrator cannot loosen its children's rules.
3. Done. The Delegation module and its `delegate` tool, registered like the canvas tool so only providers that accept tools get it; `.avala/jobs.json` accepts a `delegation` section, parsed strictly by Delegation from the rules commit, with `connections`, `routing` (`roundRobin` or `leastUsed`), `maxDepth` and `maxChildren`; without it a repository does not delegate. The orchestrator never names a connection.
4. Done. A child runs at the autonomy its call asks for or its parent's effective one, and asking for more is `AutonomyLoosened`; Budgets carves each child a share, `carvePerChild` in `.avala/budget.json`, 0.5 by default, of what its parent has left uncommitted, reserves it against the parent while the child runs, holds the child at its carve and the parent at its cap counting its children, and keeps carves in `budgets.db`; `DepthExceeded` and `TooManyChildren` are typed refusals returned as tool errors.
5. Done. Approving a child delivers its work into its parent's worktree: in the parent's queue, the parent is checkpointed, then the `merge` strategy squashes the child onto the parent's branch and fast-forwards the parent's clean worktree, never forcing. Delegation approves a child once it reached review verified, and returns its summary, files, verification evidence, spending and carve as the call's result, or `Conflict` with the conflicting files, the child left for review.
6. Done. Scenarios `delegate`, `delegate-conflict`, `delegate-loosen`, `delegate-expensive` and `recursive`, with the children `notes`, `notes-revised`, `todo` and `expensive`. Host simulation tests: an orchestrator delegates to two children at once on two connections, routed round robin, which run governed and both before either reports, and receives their verified work, merged into its worktree; a child asking for more autonomy is refused and a plain one inherits the supervised parent's level in an autonomous repository; a child is held at the budget carved for it and reported held; the depth cap refuses a recursive delegation at depth 2; and of two children writing the same file one is integrated and the other reported as a conflict and left for review.

Done when: a simulated orchestrator delegates to simulated children on two connections and receives their verified results, with the tree audited. Met.

### Resources

Status: done with the simulator, after connections and before delegation, since delegation multiplies the processes, ports and worktrees an unattended run leaves behind. See [Process trees](../design/core.md#process-trees) and [Resources](../design/core.md#resources). Linux and Windows contain processes and run in CI; macOS compiles and is best effort, untested by CI. Deferred: a delegated cgroup on Linux where systemd user delegation is available, for memory and CPU measured and limited by the kernel; starting a Windows process inside its job rather than assigning it right after it starts, which needs `CreateProcess` with a job list; persisting samples, orphan reports, leases and the retention due of ended jobs across restarts; admitting continuations and recoveries against the limit of running jobs; a memory cap across the jobs of a machine; CPU caps; checking that the real Claude Code adapter starts its process through the session's launcher and passes the port variables on, with phase 6; port leases that race across processes, since a lease checks which ports are listened on but the bind happens later, when the agent starts its service, so two Avala processes, or parallel test runs, with overlapping ranges can lease the same block and collide; and the views of resources, with phases 9 and 10.

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

### Review and approval

Status: done with the simulator, see [Review and approval](../design/core.md#review-and-approval), after resources and before delegation, which brings a child's verified work into its parent through the same mechanism. The design of the user interface showed what review needs from the core that its contracts do not expose yet. Deferred: storing the job's evidence, verification reports and the decisions, forms and assumptions of Permissions, together, so the review of a job survives a restart; the diff of the worktree's uncommitted edits while a job runs, and renames in the diff; signing the squashed commit and running the repository's commit hooks on it; a merge commit instead of a squash for repositories that prefer one; a strategy that opens a pull request, in a GitHub plugin; queries of interventions over time windows; compacting the stored usage facts; the hold reason stored with the job; and EF Core migrations, which should start with the first release now that the data folder holds history worth keeping.

- Approving a job delivers its work through an approval strategy, an extension point the core defines and plugins implement. A repository chooses its strategy in `.avala/jobs.json`, read from the base commit like every rule file.
- The core ships two strategies. `keep`, the default: the job's branch is left ready and nothing else is touched. `merge`: the job's checkpoints are squashed into one clean commit and merged into the base branch without ever forcing; a conflict, a moved base that no longer merges cleanly, or a checkout with uncommitted changes returns a typed outcome naming the problem, and the job stays awaiting review.
- Opening a pull request arrives later as a strategy of a GitHub plugin, without touching the core.

1. Done. `IJobCatalog` in `Jobs.Contracts`: the list of jobs with their repository, instruction, submission time, status, connection, autonomy and workspace, and each job's history of sessions and attempts, from untracked snapshots of the store. Each attempt now records its session, and each job its submission time.
2. Done. `IWorkspaceChanges` in `Workspaces.Contracts`: the files the job's branch changed against its base commit with their counts, the hunks of one file on demand, and the files that conflict with the base branch, through git and `IProcessRunner`, with typed failures. A workspace records the branch its base was checked out on.
3. Done. `IJobs.ApproveAsync` and `SendBackAsync`, with `DiscardAsync`, in `ReviewJob`: only a job awaiting review, a new round through the continuation machinery, typed rejections, transitions through `TryFire`, and `JobApproved` once an approved job is stored.
4. Done. `IApprovalStrategy` in `Jobs.Contracts`, chosen by `approval` in `.avala/jobs.json` at the base commit, parsed strictly; `keep`, the default, and `merge`, which squashes the checkpoints into one commit with a message derived from the job and moves the base branch only by compare and swap, updating its clean checkout or refusing with `MergeConflict`, `BaseCheckoutDirty`, `BaseMoved` or `NoBaseBranch`.
5. Done. Usage facts in `observability.db`, read over time windows through `IUsageHistory` and restored at startup, so `IUsage` answers across restarts; interventions of Supervision and Budgets in `supervision.db` and `budgets.db`, restored at startup. The decisions of Permissions are deferred with the verification reports, as the job's evidence. Recovery no longer recovers a job the application already submitted or started, which a slower startup had exposed, and the runtime publishes `StartupCompleted`.
6. Done. Host simulation tests: a verified job approved with `keep` leaves its branch and touches nothing else; with `merge` it lands on the base branch as one commit whose parent is the old tip and its checkout is updated; a base that moved into a conflict is reported and the job stays awaiting review; a base checkout with uncommitted changes is refused and left as it was; a job sent back starts a new round with the feedback; usage and interventions survive a restart and usage is read by time window. A discarded job's worktree is released by the existing test of Resources.

Done when: a simulated job is reviewed through the contracts alone, from its diff to its approval, with both strategies and the conflict proven. Met.

### Autopilot

Status: done with the simulator, see [Autopilot](../design/core.md#autopilot). Deferred: persisting loops, their digests and the approval decisions, so a loop survives a restart and resumes; storing the verification reports with the job's evidence, without which a job judged after a restart waits for a person; pausing a loop on a limit before the first job of a loop that names no connection, whose connection is only known once a job started; a limit threshold read from the repository or the machine instead of the start command, and breakers declared in a file; a pause that holds the running job instead of letting it settle; a cap on the follow-ups one job may propose and on the depth of follow-ups of follow-ups; replaying a recorded tool call in the simulator; GitHub and Linear issues as job sources, in their plugins; the digest as a view, with phases 9 and 10; and checking the real Claude Code adapter's tool calls, with phase 6.

Developers love to leave an agent looping on its own: take a task, finish it, take the next, all night. Done naively, such a loop burns quota, compounds its errors and lands work nobody would have approved. Autopilot runs the loop with the guarantees Avala already has, plus three pieces.

- **Approval without a person, only on clean evidence.** A repository declares in `.avala/jobs.json`, read from the base commit, that a job may be approved automatically when its verification passed and its run had no exception: no denial, no assumption, no edited rule file, no hold. A job with any exception waits for a person, and the loop moves on to the next.
- **Where the next task comes from.** Job sources are an extension point: a backlog file in the repository, recurring tasks, and follow-up tasks the agent proposes through an injected tool, accepted or refused by the policy. Linear or GitHub issues arrive later as plugins.
- **When to stop.** Circuit breakers: a cap on spending per loop and per time window, a number of failures in a row or the same failure repeated, attempts that change nothing, and a maximum number of iterations. When a connection reaches its usage limit, the loop pauses until the window resets and then continues, instead of stopping.
- **The digest on return.** What was approved on its own with its evidence, what waits and why, what it cost, and which breaker fired; replay holds the detail of any job.

1. Done. The automatic approval rule, `"autopilot": { "approve": "cleanEvidence" }` in `.avala/jobs.json` at the base commit, whose section Jobs accepts and Autopilot parses strictly; clean evidence defined from the audit data that exists, a passed last verification and no denial, assumption, edited rule file, hold or unreadable diff; every decision published as `AutoApprovalDecided` with its evidence summary and kept in the loop's digest. Only the jobs a loop took are judged.
2. Done. `IJobSource` in `Autopilot.Contracts`, asked in registration order; the backlog and the recurring tasks of `.avala/backlog.json`, read from the repository's current base through the new `IBaseFiles.ReadCurrentAsync`; follow-ups proposed through the executed harness tool `propose_follow_up`, the minimal version of delegation's first item, accepted only from an autonomous session whose repository says `"followUps": "accept"`; marks and proposals in `autopilot.db`, never in the repository.
3. Done. `IAutopilot` with start, pause, resume and stop, the loop's `LoopState` as data, one loop per repository, one job after another, every submission admitted by the machine's limit; breakers on iterations, failures in a row, the same failure signature, attempts that change nothing, and spending per loop and per trailing window of the stored history; a limit window at its threshold pauses the loop until it resets on a `TimeProvider` timer, and a job Budgets held near its limit is continued after the reset. Budgets no longer counts a limit reading whose window already reset.
4. Done. The events `LoopStarted`, `LoopTaskTaken`, `AutoApprovalDecided`, `LoopIterated`, `LoopWaiting`, `LoopPaused`, `LoopResumed`, `BreakerTripped`, `LoopEnded` and `FollowUpDecided`, and `IAutopilot.DigestOf`.
5. Done. Host simulation tests: a backlog of three tasks runs unattended, its two clean jobs merged as two commits on the base branch and the one that rewrote its checks left awaiting review; a check that fails the same way twice trips `SameFailure` and leaves the third task; the `near-limit` scenario pauses the loop until its window resets two seconds later and the loop then takes its next task; the `follow-up` scenario's proposal is accepted and runs next under an autonomous repository that accepts follow-ups, and refused for a supervised loop. Unit tests on `FakeTimeProvider` cover every breaker, the pauses, the waits for recurring tasks, the commands and the sources.

Done when: a simulated backlog runs to its end unattended, merging only clean jobs, stopping on a repeated failure and pausing across a limit reset. Met.

## Phase 9: View models of the usable core

The whole application works through view models, with no user interface.

Status: part 1 done with the simulator, see [Workbench](../design/core.md#workbench): the module that holds every screen, its job board and conversation projection, and the main window's view models; part 2 done for the job-centric screens: the review sheet, the decisions popover and the inspector, and Stop that holds a job instead of discarding it; part 3 done for the global pages: the overview, usage, settings, resources and a new job. Deferred: a ticking wait time in the decisions popover, which today updates on every board change and when it opens; choosing an option of a form field other than the first from the keyboard; the evidence of a job of an earlier run, whose verifications and decisions live in memory, so its review shows no verdict until they are stored; and the hold reason across restarts, so a job stopped before a restart shows as needing help. The screens of the approved [brief](../design/ui-brief.md) that remain are listed below, each built on these foundations.

1. Done. The job list is the sidebar: jobs grouped by what they need, one secondary fact per row from real data and the count of pending decisions; job detail is the conversation. A new job is its own page, submitted through `IJobs.SubmitAsync` with its repository, instruction, connection and an autonomy that can only tighten, a rejection shown by its reason.
2. Done. The `Transcript` projection of the job board: prompts from the job's attempts, messages and reasoning streamed in order, reasoning with its measured duration, tool rows, the turn's plan, canvases from the throttled `CanvasUpdated` snapshots, permission and form cards, and the end of every turn with its usage; a job of an earlier run shows its attempts and a restart mark. The board follows `CanvasUpdated` from startup, so `ICanvases` is not needed. Storing the event stream, so a conversation survives a restart, is done in [the restart findings](#restart-findings-from-dogfooding).
3. Done. The [review sheet](../design/core.md#review): the verdict, then only the exceptions from `IRunEvidence`, Autopilot's clean-evidence definition answered for any job, plus the attempts that failed; a quiet line of the decisions the rules allowed, the cost and the tokens; the diff with a file's hunks on demand; approve, send back with feedback and a two-step discard, with refusals shown in place, a merge conflict with its files.
4. Done. The usage page over `IUsage`, `IUsageHistory` and `IBudgets`: per connection with its limit windows, reset times and hold threshold, per job against its caps and carve, today and the last seven days with tokens by type, and the interventions across jobs, see [the global pages](../design/core.md#the-global-pages).
5. Partly done. One generic form card renders any `AgentForm`, recommended options preselected, and answers through `IAgents.AnswerAsync`; the permission card answers through `IPermissionAnswers` with a note and "don't ask again"; both are answered in place in the conversation. Done since: the automatic decisions and assumptions of a job in its review and its inspector, and the [decisions popover](../design/core.md#decisions), every waiting permission and form across jobs answered with the same cards, keyboard first, with the wait of each.
6. Done. The main window: the shell activates its page through `IActivatable`, the page shows the sidebar, the selected job's conversation and the inspector toggle, closed by default. The composer sends a message to a job that needs a person or awaits review, interrupts a running job by holding it as `Interrupted`, and stops a running job by holding it as `Stopped`, which stops its session and keeps its worktree, so a message continues it; discarding moved to the review sheet. The [inspector](../design/core.md#inspector) shows the selected job's evidence, decisions and assumptions, usage and caps with a child's carve, autonomy and connection, worktree with its port lease, and delegation, each section loaded on open and reloaded when the job's revision moves.
7. Done. Unit tests of the projection, the board and every view model with fakes, and host simulation tests driving the view models against the application composed on the simulator: a job streams into its conversation in order with its thinking and tool rows, a permission card answered from its view model unblocks the job, a form is answered with its recommended option, and the sidebar moves a job from running to needing a person when it is interrupted and to ready for review once continued. Part 2 adds: a verified job reviewed and approved with `keep` from the review sheet, its failed first attempt, its diff and a file's hunks shown; a job sent back from the sheet starts a new round with the feedback; an approval refused for a merge conflict shows the conflicting files; the decisions popover answers a permission and a form of two jobs; the inspector shows the evidence, decisions and usage of a finished job; and a stopped job keeps its worktree and is continued with a message.

8. Done. The [global pages](../design/core.md#the-global-pages), each a view model of the Workbench with its placeholder view, and its unit and host tests:
   - **Overview.** The connections with their provider, account, limits and running agents, and the delegation tree of a selected orchestrator from `IJobCatalog.TreeAsync`, `IDelegations` and `IBudgets.CarveOf`.
   - **Usage.** See item 4.
   - **Settings.** The repository's rules read-only from its current commit through the new `IRepositoryPolicies`, `IRepositoryBudgets` and `IRepositoryChecks`, with "Edit in repository" through the SDK's `IFileOpener`; the silence window edited through `ISupervision.ChangeSilenceAsync`; the connections listed and `connections.json` opened for editing; the resource settings shown.
   - **Resources.** The machine and each agent tree attributed to its job, connection and provider, orphans cleaned through `IOrphans.ReapAsync`, stale worktrees reconciled and cleaned, leases and conflicts, and `ResourceIndicatorViewModel` for the sidebar.
   - **A new job.** See item 1.
   - Each global page has unit tests with fakes and a host simulation test: the overview lists two connections with their agents, the delegation view shows a simulated orchestrator's children, the usage page shows a job's cost and its limit against the hold threshold, the settings show a repository's rules read from its current commit, the resources page shows an orphan and cleans it up, and a job submitted from its page starts on the chosen connection.

Remains for the next steps:

- Hosting the resources indicator in the sidebar's view.
- Editing connections in place, if a form for credential sources proves worth it.

Done when: the full job flow runs end to end through view models in tests.

## Phase 10: Views

0. The view rules, enforced before any view is designed, applied to the placeholder views and view models of phase 9:
   - regions, as [composing the interface across modules](../architecture.md#composing-the-interface-across-modules) describes: typed region names in `Avala.Sdk.Regions`, registration of view models into regions through the registrar with an order, region context received through `IRegionAware`, UI messages in each module's `Contracts` under `Presentation`, typed view model interfaces and factories there for the exceptional case, and design-time region content;
   - an interface for every view model and a design-time implementation with realistic data, declared as each view's design-time `DataContext`, so every view renders in the designer;
   - compiled bindings with `x:DataType` on every view;
   - code-behind limited to presentation concerns, replacing the rule that allowed only `InitializeComponent()`;
   - size limits: 800 lines per XAML file, 400 per code-behind, 400 per view model;
   - view model first everywhere: regions hold view models, parents receive their children through dependency injection as interfaces or factories;
   - view models and their interfaces in each module's core assembly, which references no UI framework, so the `.UI` assemblies alone can be replaced by another technology; shared components as a framework-free assembly of view models plus a `.UI` assembly of views, allowed as a dependency of every module; one folder per view inside each `.UI` assembly; architecture tests for the dependency rules;
   - scripted acceptance tests for every finished component and page: a scripting helper for view models and for views rendered headless, scripted tests of every page's region composition, and an architecture test that every view model and view has its scripted tests;
   - the MVVM structure: dumb views bound to commands, parents that own and activate their children, master and detail through the parent, UI messages over an injected `IMessenger` between regions and modules, one view model per kind of item, and no view model referencing its parent or a sibling, the last checked by an architecture test over the view model dependency graph;
   - architecture tests for each rule, checked against the production views and the compliant and violating fixtures;
   - scripted acceptance tests for every finished component and page, through `ViewModelScript` and the headless `ViewScript`, with waits on events and presentation signals only.

   Done: the regions in the SDK and the shell, with their context, activation and design-time content, and the shell's main window drawn as regions; Avala's [design system](../architecture.md#design-system) in `Avala.Components.UI`, with the fonts, materials, motion, control styles and item-kind icons; the first shared components, the status dot, status pill, meter and keycap hint, with their scripts; the scripting helpers and the `IPresentation` signal; every [view rule](../architecture.md#view-rules), including the XAML rules, on production and both fixtures; and the shell rendered headless from the published plugins.

   Done too, the Workbench retrofit: an interface and a design-time implementation with the brief's sample work for each of its view models, a design-time `DataContext` in each of its views, its placeholder views restyled with the theme's resources in a folder per view, `Scripts` classes for each view model and view, headless for the views, and its parts moved into the shell's regions with `AddToRegion`: the job list into `Sidebar`, the jobs page in `Content` and the inspector's sections into `Inspector`, the selected job set as the inspector's context through `IRegions`, a job chosen in the sidebar reaching the page as the `JobSelected` UI message and the page shown through `PageRequested`. The [scoped list](../architecture.md#no-rule-is-scoped) is empty.
1. Avalonia views for every view model, following the approved [design brief](../design/ui-brief.md), semi-transparent with themes, Inter for the interface and JetBrains Mono for code.
2. The canvas surface for each media type, with renderer plugins registered by media type.

   Done: the shared canvas surface, with versions, streaming without flicker and the focused view; `ICanvasRenderer`, registered through the view registry; the Rendering plugin, with Markdown and sanitized SVG. Done too, the [offer](../design/canvas-rendering.md#the-offer): renderer plugins declare the media types they draw as `CanvasFormat`, the canvas tool offers exactly those, a canvas in any other type is rejected as `NotOffered` and shown as source, highlighted for Mermaid and HTML, and the conformance kit reports a harness that draws outside the offer. [Decided](../design/canvas-rendering.md#mermaid-and-html-the-decision): Mermaid and HTML are not part of the core's offer; they are possible as optional renderer plugins, which appear in the offer once registered, and Mermaid has one since the C1 polish.

Done when: the harness replaces a terminal for daily work.

## Screen honesty

Nothing on a screen may mislead: every label says exactly what its figure covers, every control says what it will do, and nothing offers an action that cannot be completed there without saying so. An audit also found screens that present as durable what lives only in memory, so after a restart the interface lied: a verified job read as merely ready for review, an audit as empty, a cap as unknown, a reading of a reset window as current.

### A1: Persistence

Status: done.

Every module that owns data keeps it in its own SQLite database through EF Core, with generated migrations applied at startup, see [persistence](../design/core.md#persistence) and [migrations](../design/core.md#migrations).

0. Migrations: `Avala.Storage`, the shared library of `ModuleDatabase` and `StoredJson`; `scripts/migration.cs`, which scaffolds a module's migration with EF Core's design-time services and conforms the generated code to the architecture rules; an `Initial` migration for each existing database, generated from the model `EnsureCreated` built, so a database of an earlier build is adopted and upgraded in place; every store migrating on its first open and as a startup task; the architecture rules that every database has migrations and that every module's model matches its latest one.
1. Verification evidence: every `VerificationReport`, with each check's status, exit code, duration and bounded output tails, stored in `verification.db` and restored at startup. The inspector shows each check's duration.
2. The permission audit: policy reports, autonomy applied, permission and form decisions with their assumptions, and human answers stored in `permissions.db` and folded back into each session at startup.
3. Budget caps: each session's `SessionBudget` and connection stored in `budgets.db`.
4. Connection choices: the reason and the readings compared, stored in `jobs.db` and returned with the job's history.
5. Delegation records: every version of a record stored in `delegation.db`; the resumption of in-flight children is done in [B](#b-delegation-robust).
6. Sessions: Observability stores when each session opened and lists its sessions through `IUsageSessions`, so the Workbench finds the latest session of a connection or job of an earlier run, its account and its caps.
7. Limit readings: a reading whose window has reset is shown as reset on the Usage page and the Overview, live through a `TimeProvider` timer and after a restart; capacity treats it as fresh capacity, as before.
8. Pending decisions: by design they do not survive a restart, since the session that waits died; the count shows only what a live session waits for, and the audit marks the abandoned decision "unanswered, its session ended".

Acceptance criteria, each a host simulation test in `RestartTests` that captures what the screens show, restarts the application with `SimulatedRun.RestartAsync` over the same data folder and clock, and compares:

```
AC1  Given a job verified on attempt 2 of 2, when the application restarts, then the sidebar row, the inspector's evidence
     with each check's exit code and duration, and the review's verdict and failed attempt with its output tail are the same.
AC2  Given a governed job with a denial, an assumption and its autonomy, when the application restarts, then the inspector's
     audit, decisions, assumptions and autonomy and the review's exceptions are the same.
AC3  Given a job near its cost cap and held at a limit, when the application restarts, then the Usage page's cap, near-cap
     alert and hold threshold, the Overview card's account and near-limit ring, and the inspector's spending are the same.
AC4  Given a job placed by capacity, when the application restarts, then the inspector's connection, reason and compared
     readings are the same.
AC5  Given an orchestrator whose children were integrated, when the application restarts, then the delegation view's root
     with its harness and cap, its children with their outcome, harness and carve, and the inspector's children are the same.
AC6  Covered with AC3: the Overview card's account and the caps found through the latest session of an earlier run.
AC7  Given a reading whose window resets, when its reset time passes, then the Usage page shows it reset and the Overview
     card no longer near its limit; after a restart they still do; and a job naming no connection treats that connection
     as unused.
AC8  Given a permission left to a person when the application stops, when it restarts and the conversation resumes without
     asking again, then no decision is pending and the audit marks the decision unanswered because its session ended.
```

Unit tests cover each store's round trip and its exclusion of what the current run wrote, each book's restore, the adoption of a database created without migrations, the stored JSON of options and enums, the judgment of readings against the clock and the timer that refreshes a page at a reset. The simulator gained the `governed` and `waiting-permission` scenarios.

### A2: nothing misleading

Done. Each item has its acceptance criteria as view model scripts, headless view scripts where the view changed, and a host simulation test in `ScreenHonestyTests` that drives the composed application on the simulator.

1. **Usage over all time.** The usage page and each connection's meter show all recorded usage, earlier runs included; they say "All recorded usage, kept across restarts" and "… in total" instead of "since Avala started".
2. **Resources of Avala's agents.** The resources page and the sidebar indicator measure the process trees and worktrees of Avala's agents, and say so instead of "This computer".
3. **Don't ask again, for the session.** The session rule lasts as long as the agent session; the card, the decisions popover, which offers it too, and the inspector say "Don't ask again this session", with the exact scope in its hint.
4. **The autonomy a new job runs at.** The New job page reads the repository's declared autonomy at its current commit, offers "Repository's level: …" and "Supervised" only when it tightens it, and says what the choice means.
5. **The simulator outside developer mode.** A provider declares through `ProviderInfo.OffersImplicitConnection` whether it has an implicit connection; outside developer mode, `AVALA_DEVELOPER`, the simulator offers none and discovers none, and only a simulator connection declared in `connections.json` is listed.
6. **Auto across providers.** Auto compares the usable connections of every provider, and Autopilot watches the same connections; the New job line says when there is no reading to compare. The simulator registers a second harness, `simulator-second`, reached only through a declared connection, to prove it end to end.
7. **Edit a file that does not exist yet.** "Edit in repository" and "Open connections.json" create a missing file from a minimal valid template, then open it, and say what was created and when it applies; `IFileOpener` takes the template.
8. **Every form answerable from the popover.** A form of one choice field without free text keeps its numbered options; any other form is filled in a box under the list with the conversation's own field components, where typing never moves the selection.
9. **The wait of a decision.** The popover re-ages its decisions on a `TimeProvider` timer while open, and a decision without a timestamp shows no wait.
10. **Thinking the harness did not share.** A thought that streamed no text reads "Thought for Ns · content not shared by the harness" and does not open; the simulator's `unshared-thought` scenario plays it.
11. **The sidebar's decision badge.** Each row draws the count of decisions waiting on its job.
12. **The overview's limits.** Hovering a connection's hub lists every limit window of the connection.

### B: delegation robust

Done. A delegation tree survives a restart and runs across harnesses, proven end to end on the simulator, and with a Claude Code orchestrator replayed from its transcript; see [children across a restart](../design/core.md#children-across-a-restart) and [deferred recovery](../design/core.md#deferred-recovery).

1. **Children survive a restart.** A child that was running is recovered like any job; the desk rebuilds its pending calls from the stored records and settles at startup a child that settled unreported. Recovery defers a running parent that is owed a report, through Jobs' new `IRecoveryDeferral`, and resumes it through `IJobs.ResumeAsync` once its children reported: its conversation resumes, by its resume token, with the restart note and the owed reports, through Jobs' new `IJobBriefing`. A parent whose harness is not `Resumable` is held as `NotResumable` with the reports shown, and receives them when a person continues it.
2. **Answered exactly once.** Each record stores how its report reached the parent, as a tool result or in a message, and `ReportDelivered` announces it; a report owed to a held parent is briefed once when the parent continues, with or without a restart.
3. **Across harnesses.** A parent on `simulator` delegates by capacity to `simulator-second` and to a second `simulator` account, each child with its carve, both integrated into the parent's worktree; a Claude Code orchestrator, replayed from the `delegate` transcript by the fake CLI, delegates to a simulated child and receives its integrated report as the call's result.
4. **The simulator** gained the `Recall` step, which replies with what its turn was told, and the `delegate-paused`, `notes-paused` and `delegate-across` scenarios; `delegate-waiting` recalls what it is told when resumed.

Acceptance criteria, host simulation tests in `DelegationRestartTests` and `DelegationTests`:

```
AC1  Given a parent waiting on a running child, when the application restarts, then the child resumes and is integrated,
     the parent is not relaunched until then, its resumed conversation is told the report once after the restart note,
     and the Overview shows the child integrated and the inspector "Integrated · told to its parent in a message".
AC2  Given a child waiting for a person, when the application restarts twice, then the child asks again each time, and once
     answered its report reaches the parent once, with the same views.
AC3  Given a child that finished while its parent was held, when the application restarts, then the inspector says the report
     is not yet told to its parent; when a person continues the parent, the person's message is followed by the report, once.
AC4  Given each of AC1 to AC3, when the application restarts again, then nothing is delivered again, the parent has one child,
     and the parent's branch holds at most one integration of it.
AC5  Given a parent whose harness is not resumable, when the application restarts and its child reports, then the parent is
     held as NotResumable, shown as "held: its conversation cannot resume", with the report shown and not yet told.
AC6  Given a parent on simulator routing by capacity to simulator-second and a second simulator account, then the first child
     goes to the first listed, the second to the account with more left after the first reported, each with a carve of at
     most half the parent's cap, and both are integrated into the parent's worktree, the Overview naming each harness.
AC7  Given a Claude Code orchestrator replayed from its transcript, when it delegates to a simulated child, then the child is
     integrated and its report returns as the call's result, recorded by the recorder.
```

Unit tests cover the deferral, resumption, briefing and refusals in Jobs (`DeferredRecoveryTests`) and the rebuilt desk, the owed reports and the resumption of deferred parents in Delegation (`RestartedDeskTests`).

5. **Confirmed with real Claude Code.** One real run on haiku, `RealClaudeCodeTests.AConversationThatDiedWaitingOnAHarnessCallResumesAndTakesItsResultInAMessageAsync`, about 0.02 USD: a session that died while its `delegate` call was unanswered, resumed with `--resume`, accepts the new user message carrying the report and answers from it without calling the tool again; its redacted transcripts are in `tests/transcripts/claude-code/real-delegate-resumed`. No adaptation was needed.
6. **Reports mid-turn.** A report whose call can no longer receive it goes to a parent that runs a later turn in a live session through `IJobs.SteerAsync`, when the parent's harness declares `AcceptsMessagesMidTurn`; otherwise it waits for the parent's next round as before. Host test `DelegationTests.AReportForAParentRunningALaterTurnArrivesMidTurnOnceWhenItsHarnessAcceptsMessagesAsync`, simulator scenario `delegate-steered`.

Open: a provider that loses a tool result it received before dying in a parallel call.

## Gaps against the brief

### C2: steer while the agent works, usage windows, settings editing

Each item has its acceptance criteria as unit tests, view model scripts, headless view scripts where the view changed, and a host simulation test that drives the composed application on the simulator.

1. **Write while the agent works.** A capability component, `AcceptsMessagesMidTurn`, declared by Claude Code and the simulator, lets a message join the running turn; without it the composer queues the message honestly and delivers it when the job next stops for a person.
   - AC1 Given a session whose provider declares the component and a running turn, when `IAgents.SteerAsync` is called, then the provider receives a `UserTurn` marked `MidTurn`, the call returns the running turn, and the turn announces `MessageQueued` with the text.
   - AC2 Given a provider that does not declare it, `SteerAsync` returns `Unsupported` without asking the provider; a session that is not open returns `SessionClosed`.
   - AC3 The conformance kit: `CheckMidTurnAsync` passes for a provider that queues the message into the running turn and reports one that does not; `MessageQueued` from a provider that does not declare the component is a violation; the simulator passes both, with and without the component.
   - AC4 Given a running job, `IJobs.SteerAsync` reaches its session and leaves it running; a job that is not running returns `NotRunning`, a provider that refuses returns `NotSteerable`, an empty message `EmptyMessage`.
   - AC5 Given a running job whose session takes messages mid-turn, the composer says "Message the agent while it works…" and Send joins the turn; the conversation shows the message as "You, while it worked · joined the turn".
   - AC6 Given a job working without the component, or starting, or checking, Send queues the message, the composer shows it with Withdraw, and the hint says it waits for when the agent stops; a refusal mid-turn falls back to the queue.
   - AC7 Given queued messages, when the job next needs help they continue it, all at once; when it awaits review they are never sent on their own: the review sheet offers "Send back with this message" and sending back clears them; a withdrawn message is never delivered; a job that ends drops them.
   - AC8 The simulator's `steer` scenario waits for a message mid-turn and answers it in the same turn; Claude Code writes the message to the CLI and keeps the turn open until the result of the queued message.
   - AC9 End to end in `SteeringTests`: on the default simulator connection a message sent from the composer joins the running turn and is answered before the single turn end; on a connection without the component it is queued, and after an interruption it continues the job as "You"; a message queued while a permission waits stays queued when the job reaches review, the review sheet shows it, and "Send back with this message" starts a round with it as "Sent back".
   - AC10 Given a Claude Code turn interrupted while a message sent mid-turn is still queued, when the next message is sent, then the CLI's late answer to the queued message never opens, fills or ends the next turn, which shows only its own reply: a unit test, and `ClaudeCodeSteeringTests` replaying the real run committed in `tests/transcripts/claude-code/real-mid-turn` (one haiku run, 0.0044 USD) through the composed application.
2. **Choose a usage window and see it day by day.** The usage page offers Today, 7 days and 30 days.
   - AC1 Given a window, `UsageWindows` reads its whole from the local midnight of its first day to now and each local calendar day of it, 1, 7 or 30.
   - AC2 Given the page shows the last 7 days, when 30 days is chosen, then the page reads again and shows 30 days newest first, today marked, the total labelled "Last 30 days" with its tokens by type, and the caption names the first day; choosing today shows one day and the total "Today".
   - AC3 Each day shows its tokens, its cost, a bar against the busiest day of the window and, on hover, its tokens by type; choosing the window already shown reads nothing again; a range read for another window is not shown.
   - AC4 End to end in `UsageWindowTests`: after a simulated job reports usage, the week shows today's tokens on its first day and none on the day before, and choosing today shows the same tokens as its total.
3. **Edit settings in the app where it is safe and honest.** Machine files through their module's contract, repository rule files as their working copy.
   - AC1 Given `connections.json`, when a connection is declared, renamed, re-credentialed or removed through `IConnections`, then only that connection changes in the file, its settings and the other fields are kept, a renamed fixed default stays the default, and the catalog and new sessions read it at once.
   - AC2 A declaration the machine cannot use is refused before the file is touched: `InvalidName` (also `auto`), `UnknownProvider`, `UnknownSource`, `MissingReference`, `DuplicateName`, `UnknownConnection`; the fixed default cannot be removed (`RemovesTheDefault`); removing the last declared connection leaves a file the parser accepts.
   - AC3 Given the Settings page, "Add a connection" opens a form of name, harness, credential source and where it is; Save is offered only once the form is complete; an API key is declared by the name of its variable and the form says Avala never reads the key; a saved connection joins the list with a line that says what happened, and the first one names the implicit connections it replaces.
   - AC4 A declared connection offers Edit, prefilled from its declaration, and Remove, which asks first and does nothing when kept; discovered and implicit connections offer neither; a refusal is shown with its reason and the list stays as it was.
   - AC5 A rule file whose module registers a format offers "Edit here"; the editor starts from the working copy or the template and says the change applies to new jobs once committed; text the module would reject is never written and its error is shown; accepted text is written to the working tree and the page reads the repository again; a working tree that cannot be written keeps the editor open with the reason; `.avala/jobs.json` offers only "Edit in repository".
   - AC6 `IWorkingFiles` writes inside the repository only, refuses a path that leaves it, content over 64 KiB and a folder that is not a repository, and leaves the change uncommitted.
   - AC7 End to end in `SettingsEditingTests`: a connection added in Settings is written to the data folder and a job runs on it at once, then removing it makes it unknown; a budget file edited in Settings is refused with `InvalidThreshold`, then written, while the caps shown are still the committed ones; every format registered in the application accepts its template and rejects malformed text.
   - Not done: dedicated controls for autonomy, form strategy and caps, which are edited as the text of their file; the machine's running-jobs limit of Budgets, left to its module's owner.

## Polish

### C1: light theme, reduced motion, Markdown replies and Mermaid

Done. Each item has its acceptance criteria below, played by view model scripts, headless view scripts and host simulation tests that drive the composed application on the simulator: `ThemeVariantTests`, `ThemeVariantScripts`, `ThemeScreenScripts` and `ShellViewScripts` for the theme, `AppearanceFileTests`, `AppearanceViewModelScripts`, `AppearanceViewScripts` and the host's `AppearanceTests` for the setting, `MotionScripts`, `StreamingTextScripts` and `CanvasSurfaceViewScripts` for motion, `MarkdownBlocksTests`, `MessageViewModelScripts`, `MessageViewScripts` and the host's `MarkdownReplyTests` for replies, and `MermaidSvgTests`, `MermaidRendererScripts` and the host's `CanvasOfferTests` for Mermaid. The design is in [the design system](../architecture.md#design-system), [appearance](../architecture.md#appearance), [Markdown replies](../architecture.md#markdown-replies) and [the Mermaid plugin](../design/canvas-rendering.md#the-mermaid-plugin).

1. **Light theme.** Every theme resource has a light variant in the macOS-inspired direction of the brief; Avala follows the operating system's theme unless the machine's appearance setting forces light or dark.
2. **Reduced motion.** A machine setting, System, On or Off, turns off every animation that moves, pulses or fades; System follows the operating system's preference, which the host reads itself on Windows, GNOME and macOS since Avalonia 12.1 does not expose it.
3. **Markdown replies.** The agent's messages are drawn as Markdown, streamed without redrawing what is already settled, with copyable code blocks and links opened only when they are web links.
4. **Mermaid as an optional plugin.** A separate renderer plugin offers `text/vnd.mermaid` and draws it in pure .NET through the SVG renderer and its sanitizer; without the plugin, Mermaid is not offered and shows its source.

```
T1  Given the theme files, then every theme dictionary declares the same keys for Dark and Light, and no theme file holds a color or a shadow outside a theme dictionary.
T2  Given each variant, then primary text reaches 7:1 against the window, panel, float and canvas surfaces, secondary text 4.5:1, tertiary text and the attention and failure colors 3:1, and the accent 4.5:1 against the window.
T3  Given the key screens (the shell, a conversation, settings, the review and the decisions popover) rendered headless in Light and in Dark, then each draws the variant's window surface and every visible enabled text reaches 3:1 against what is drawn behind it.
T4  Given no appearance file, when Avala starts, then it follows the operating system's theme and reduced-motion preference, and settings show appearance.json as Absent; the main window is created only once the appearance is read, so it never shows the wrong theme first.
T5  Given settings, when Light, Dark or System is chosen, then appearance.json records it, AppearanceChanged is published, the application's theme becomes that variant, and it survives a restart.
T6  Given an appearance.json that cannot be read as an appearance, then settings say it was rejected and why, the defaults apply, and choosing again rewrites it.
M1  Given reduced motion off, then the pulse, the spinners, the thinking shimmer and dots, the caret and the entrances animate; given it on, then none of them runs: entrances show their final state at once, the shimmer and the pulse halo are hidden, spinners stand still, and streamed text and canvas versions appear without fading.
M2  Given settings, when Reduce motion is set to On, Off or System, then appearance.json records it, the shell's window follows it, System following the operating system's preference, and it survives a restart.
K1  Given a reply with Markdown (headings, emphasis, lists, inline code, a fenced code block, a table and a link), then it is drawn as Markdown, its code in the code font with a Copy button.
K2  Given a code block, when Copy is clicked, then the clipboard holds exactly its code.
K3  Given a streaming reply, when text arrives in its last block, then the blocks before it keep their drawing and only the last block is drawn again; an open code fence is drawn as code while it streams; when the reply ends, it is drawn whole once. A caret sits under the live block until the reply ends, still with reduced motion.
K4  Given a link to an http or https address, when it is clicked, then it is opened through the link opener; given any other link, such as a file, a script or a relative path, then nothing is opened and the reply says that only web links are opened.
K5  Given a reply with a remote image, then nothing is fetched and its alternative text is shown.
K6  Given the simulator's markdown scenario in the composed application, then its reply is drawn as Markdown with its code block and table, its web link opens through the link opener and its file link opens nothing.
R1  Given the Mermaid plugin installed, then the offer and the canvas tool list text/vnd.mermaid after SVG and Markdown, by each format's declared order, and a renderer draws it; without it, or without the SVG renderer it draws through, Mermaid is not offered.
R2  Given a Mermaid diagram, then it is translated to SVG in pure .NET with the theme's colors, every CSS variable and color-mix resolved, and drawn through the SVG renderer, so the SVG sanitizer guards it.
R3  Given Mermaid that cannot be parsed, then nothing is drawn while it streams and its source is shown once it is final.
R4  Given the simulator's mermaid-canvas scenario in the composed application, then its diagram is drawn as SVG in the conversation; given the unoffered-canvas scenario, now an HTML canvas, then its highlighted source is shown with the note.
```

## Beta prep (E)

Done. What a first public build needs before anyone outside the project runs it: an icon, one Avala per data folder, a startup that survives a migration killed halfway, an honest first run, a log to send when something breaks, and an About section. Each item has its acceptance criteria below, played by unit tests, view model and view scripts and host simulation tests that drive the composed application on the simulator, never a real harness: `ShellViewScripts` for the icon, `DataFolderClaimTests`, `DataFolderInUseViewModelScripts` and `DataFolderInUseViewScripts` for the claim, `ModuleDatabaseTests` for the lock, `SynchronousIoTests` for the one synchronous write, `FirstRunViewModelScripts`, `FirstRunViewScripts`, `SignInTests` and the host's `FirstRunTests` for the first run, `LogFileTests`, `ProcessCliTests` and the host's `DiagnosticsTests` for the log, and `AvalaBuildTests`, `AboutViewModelScripts`, `AboutViewScripts` and the host's `AboutTests` for About and the log folder. The design is in [beta prep](../design/core.md#beta-prep).

1. **Icon.** The brand's icon, `docs/assets/brand`, is the main window's icon on every system and the executable's icon.
2. **One Avala per data folder.** A second Avala started on a data folder another one runs on never opens its databases: it says the folder is in use and quits.
3. **A migration lock left by a killed process.** Since only one Avala owns a data folder, a `__EFMigrationsLock` row found at startup can only belong to a process that died while migrating, so it is cleared before migrating.
4. **First run.** With no connection to run a job on, the jobs page says so and guides: log in to Claude Code or add a connection in Settings.
5. **Diagnostics.** A rolling log in the data folder records startup failures, unhandled exceptions and what a provider's process writes to its standard error, never a secret; Settings shows its folder and opens it.
6. **About.** Settings shows Avala's version, the commit it was built from, and links to the repository and the license.

```
I1  Given the main window, then its Icon is set from the brand's avala.ico; the host's project declares the same file as its ApplicationIcon.
O1  Given a data folder claimed by a running Avala, when another claim of the same folder is attempted, then it is refused, and once the first claim is released the folder can be claimed again; a folder that does not exist yet is created and claimed; another folder can be claimed meanwhile.
O2  Given a second Avala on a claimed data folder, then it composes nothing, shows "Avala is already running" with the folder and why it stops, and its Quit command ends the application; the view says the same headless.
M1  Given a database whose migration was killed after taking EF Core's lock, so __EFMigrationsLock still holds its row, when the database is migrated, then the lock is cleared and every migration applies instead of waiting forever.
F1  Given developer mode off, no login of any harness and no connections.json, when the jobs page opens, then it says "No connections yet", explains that a job needs a harness to run on, and shows how to log in to Claude Code and how to add a connection in Settings.
F2  Given F1, when "Open Settings" is chosen, then the shell shows the Settings page.
F3  The guide appears only when no connection can run a job: any connection hides it, an implicit one included, such as the simulator's in developer mode; given a rejected connections.json, it says the file is rejected instead.
F5  Claude Code offers its implicit connection only when it can run on it: a login on the machine or one of the CLI's credential variables (ANTHROPIC_API_KEY, ANTHROPIC_AUTH_TOKEN, CLAUDE_CODE_OAUTH_TOKEN, CLAUDE_CODE_USE_BEDROCK, CLAUDE_CODE_USE_VERTEX); end to end, Claude Code without either shows the guide, with an API key it does not.
F4  End to end: given F1 in the composed application on the simulator, the jobs page shows the guide; once a connection is added in Settings, the guide is gone when the page is shown again.
D1  Given the composed application, then a log file named by the day exists under logs in the data folder and every ILogger message of every module reaches it, with its time, level, category and exception.
D2  Given a message that holds the value of a secret-looking environment variable (a key, token, secret or password), an Anthropic key or a bearer token, then the log holds [redacted] in its place.
D3  Given a log file over its size limit, a new file is started; only the newest files are kept.
D4  Given an unhandled exception of the application domain, an unobserved task or the UI dispatcher, or a startup task that fails, then it is logged as an error with its exception.
D5  Given a provider process that writes to its standard error, then each line reaches the log under the provider's category.
D6  Given Settings, then it shows the log folder, and "Open" opens it through IFileOpener; a folder the platform cannot open shows why.
A1  Given Settings, then About shows the version and the commit from the host's informational version, "unknown" when it has none, and Repository and License open their links through ILinkOpener.
```

## Release prep (G)

Done, and nothing published: everything a release needs is in place for the owner to cut the first one, see [releasing](../release.md). Each item has its acceptance criteria below, played by unit tests, view model and view scripts and host tests, never the network: `ReleaseVersionTests` and `UpdateCheckTests` with a fake HTTP handler for the check, `UpdateViewModelScripts`, `UpdateViewScripts`, `UpdateNoticeViewModelScripts`, `UpdateNoticeViewScripts`, `AboutViewScripts` and `WorkbenchPluginTests` for what the person sees, and `SmokeRunTests` for the smoke run, which start the built host as a process. The packages are proven by running `scripts/package.cs` with `--smoke`, locally for linux-x64 and on each platform's runner in the release workflow. The design is in [release prep](../design/core.md#release-prep).

1. **Packages.** `scripts/package.cs` builds a self-contained folder per platform, win-x64, linux-x64, osx-arm64 and osx-x64, with the plugins laid out as the host expects, the version from the tag, a zip for Windows, a tar.gz with an icon and a desktop entry for Linux, and an `Avala.app` with its `Info.plist` and an icns made from the brand's PNGs, zipped, for macOS, each with its SHA-256. An AppImage and a Windows installer come later.
2. **Smoke run.** `--smoke` composes the real application headless, shows its main window and exits; `--version` prints the version and the commit.
3. **Release workflow.** `release.yml` runs only on a pushed `v*` tag: it builds and tests, packages every platform on its own runner with the smoke run, and attaches the archives to a draft release. Signing is wired for Windows and macOS, notarization included, and stays off until the repository variable `AVALA_SIGNING` is `true` and its secrets exist.
4. **Update check.** At startup, and on demand from About, Avala asks GitHub whether a newer version is out, never blocking startup and never installing anything; a machine setting turns the startup check off.
5. **README.** Screenshots of the real application driven by the simulator, dark and light, the status as beta-ready and installs as coming soon.

```
P1  Given a run of scripts/package.cs for a platform, then its archive holds Avala, its plugins with only that platform's native files, and for macOS an Avala.app with Info.plist and avala.icns; with --smoke on that platform, the packaged --version names the version and the packaged --smoke exits 0.
S1  Given the built application and its plugins, when it is started with --smoke and no display, then it composes, shows its main window, says so and exits 0.
S2  Given no plugin, when it is started with --smoke, then it says no plugin was loaded and exits 2.
S3  Given --version, then it prints "Avala <version> (<commit>)" and exits 0.
U1  Given a release newer than the build, when Avala checks, then the state is Available with that version and its page, and UpdateFound is published.
U2  Given only releases equal to or older than the build, or no release at all, then the state is UpToDate and nothing is published.
U3  Given a stable build, drafts and prereleases are ignored; given a prerelease build, a newer prerelease is offered.
U4  Given a refused request, an error status, an unreadable reply or no network, then the state is Unreachable and nothing throws.
U5  Given updates.json turning the startup check off, or unreadable, then startup asks nothing and the state is Off, while a check from About still asks; without the file, or with it on, startup checks.
U6  Given a reply that has not arrived, startup completes; once it arrives, UpdateFound is published.
U7  Versions compare as Semantic Versioning's own precedence example orders them.
N1  Given the sidebar, the notice is hidden until an update is found, then reads "Version x is available"; opening it opens the release page through ILinkOpener, and an unopened page gives its address.
A2  Given About, it shows where the check stands; Check now asks and, when a newer version exists, Download opens its page.
```

## Restart findings from dogfooding

Done. A real Claude Code run restarted in the middle of verification found three faults; each is fixed and proven through the simulator in the composed application.

1. **A job checking when Avala stopped reruns its checks.** Recovery used to relaunch it like a running job: a paid turn telling the agent the harness restarted, which it could only answer with "the job was already finished", an attempt of the budget spent and the first attempt left without evidence. Now `job.Recheck` keeps the same attempt, `EvaluateTurn` runs the gates again without the agent, and only a retry opens a session that resumes the conversation, see [the job flow coordinator](../design/core.md#job-flow-coordinator). `RestartTests` restarts while a check waits on the test's signal, the workload's `verdict` mode.
2. **The conversation survives a restart.** The Transcripts module keeps what each conversation shows, bounded, in `transcripts.db`, and the board recalls it for a job of an earlier run, then the restart note, which now says what is above it, see [jobs that ran before the application started](../design/core.md#jobs-that-ran-before-the-application-started).
3. **The evidence counts what it lists.** The inspector lists every attempt of the job, an attempt without a report saying why, such as "interrupted by a restart, no checks completed", and counts the latest report's checks as checks.

```
R1  Given a job left Checking by an earlier run, when recovery runs, then its checks run again for the same attempt, no session opens, the agent is told nothing and a pass sends it to review.
R2  Given that rerun asks for a retry with retries left, then the retry starts in a new session that resumes the conversation and the agent is told the feedback; when it cannot resume, the instruction and the feedback.
R3  Given that rerun asks for a retry with no retry left, then the job needs help and no session opens.
R4  Given the composed application restarted while a check waits, then after the restart the check runs again, the job reaches review with one attempt and one session, and the evidence reads "Verified on attempt 1 of 1".
R5  Given a workspace whose latest checkpoint has a label, when a checkpoint with that label is asked while nothing changed since, then it is that checkpoint and no commit is made; after a change, a new checkpoint commits it; so the restarted job's branch holds one "Attempt 1".
T1  Given a job's sessions, then the agent events the conversation shows, canvas snapshots and policy decisions are kept in order with their time; limits, resume tokens and sessions of no job are not.
T2  Given a job that progresses, then each attempt is marked once, across restarts too.
T3  Given an item that streams more than the cap, then its text is kept up to the cap, then a line says the rest was not kept; tool inputs and results are cut the same way.
T4  Given kept facts of earlier runs, then the board shows them in order, a restart note between runs and at the end, open requests closed and open items abandoned; with nothing kept, prompts and a note saying the job ran before conversations were kept.
T5  Given the composed application, a finished job and a job waiting for permission read the same after a restart, entry by entry, followed by the restart note.
W1  Given ended jobs whose worktrees remain when the application starts, then each is retained from when its job ended: a due already past is reclaimed at once, a later one at the first sample after it, an approved job's is kept; in the composed application a discarded job's worktree and conversation go once its hour passes after a restart.
T6  Given a job whose worktree retention reclaims, then its kept facts but its attempt marks are deleted, in order with the writes before and after, other jobs untouched; after a restart it shows its prompt and the restart note.
E1  Given an attempt without a report, then the evidence lists it with why: working, checks running, interrupted by a restart, interrupted, or no checks ran; the verdict counts the same attempts the list shows.
```
