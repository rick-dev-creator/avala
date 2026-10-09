# Avala — design brief

Design the desktop application of **Avala**, an open-source, cross-platform agent harness (Avalonia/.NET, macOS, Windows, Linux). Avala runs coding agents (Claude Code first, any harness later), each in its own git worktree, and lets them work unattended while every decision is governed, verified and audited. Its thesis: **agents you can trust without watching.**

The interface exists to make the user genuinely productive: deciding, reviewing evidence and steering, not watching a chat scroll. It must feel like a premium native macOS app: depth and relief, translucent materials (vibrancy), soft layered shadows, fluid motion with spring physics, crisp typography (Inter for the interface, JetBrains Mono for code), light and dark themes. Nothing generic: no stock dashboard cards, no default component library look. Every animation must carry meaning: it shows state changing, work streaming, attention moving.

## Ground rules

- **Design only from the data below.** Every screen, field, badge and number must map to a real event, query or command of the core. Do not invent features. When something is not in the data yet but is genuinely valuable, put it in a clearly marked "Proposed" layer and say which real problem it solves.
- **The data is provider-agnostic.** Never design around Claude-specific concepts. A session is a session of any harness; a form is a form whatever harness asked it.
- **Problems this design must solve**, from research on 2026 harnesses (Cursor, Claude Code desktop, Codex, T3 Code, Conductor, Superset, Copilot agents, Warp, Devin, Factory, Zed, Antigravity):
  1. Review is the bottleneck: more parallel agents produce more reading, not more output.
  2. Attention signals are unreliable, and approvals usually cannot be answered without opening the session.
  3. Usage and cost are opaque and limits arrive as surprises.
  4. Safety is binary (approve everything or bypass everything) and nobody shows why something was allowed.
  5. Stalled or crashed agents go unnoticed for hours.
  6. Leftover processes, port collisions and worktrees piling up on disk.

## What the core produces

### Jobs: the unit of work

- A job has a repository, an instruction, an attempts budget per round, an optional autonomy level and one or more sessions over its life (recovery and continuation open new ones).
- Status (`JobProgressed`): Draft, Preparing, Running, Checking, NeedsHelp, AwaitingReview, Approved, Discarded, Failed.
- Held jobs (`JobHeld`) carry a reason: Stalled, SessionLost, BudgetExceeded, LimitNearlyReached, InvalidBudget; and how the session was halted: Interrupted, Idle, Stopped, AlreadyClosed.
- Commands: submit (repository, instruction, attempts, autonomy); continue a held job with a message, which reports whether it continued in the same session, resumed the conversation, or started a new one.
- Each job has a workspace: a worktree path, a branch, the base commit it started from, and numbered checkpoints (a commit per checkpoint, with a label).

### Agent activity: the stream of a session

- A session opens with its provider (id, name), its working directory and an optional account (id, label). It may end on its own as Closed or Crashed.
- Capabilities a provider declares: streams partial output, exposes reasoning, can interrupt, can resume, accepts injected tools, reports usage, cost and limits, asks questions.
- A turn starts, contains items, and completes as Finished, Interrupted or Failed.
- Items have a kind: Message, Reasoning, FileEdit, Command, Search, Web, Mcp, Subagent, Other; a title; streamed text that arrives in order (`ItemProgressed`); and an outcome: Succeeded, Failed, Cancelled, Abandoned, Expired.
- A plan (`PlanUpdated`): ordered steps, each Pending, InProgress or Done.
- Telemetry per turn: tokens (input, output, cache read, cache write, reasoning), an optional cost (amount, currency), and usage limits (window name such as "5h", fraction used, optional reset time).
- Commands: send a message, interrupt the turn, stop the session.

### Canvas: what the agent draws

- A canvas is an item with a title and a media type: `image/svg+xml`, `text/vnd.mermaid`, `text/html`, `text/markdown`. It streams; snapshots (`CanvasUpdated`) carry the full content so far, throttled, and a status: Streaming, Completed, Failed, Cancelled, Abandoned, Expired.
- All canvases of a session can be queried. HTML is untrusted and must render isolated.

### Human input: the decisions only a person makes

- **Permission requests**: a title, the item kind, and the target (a file path, a command line, a URL, a tool). Answers: Allow or Deny, with an optional message to the agent ("no, do this instead") and "don't ask again", which creates an audited session rule.
- **Forms** (one generic format for every harness's modals): a purpose (Permission, Question, PlanApproval, Other), a title, a context, and fields. Each field has a header, a prompt and a kind (SingleChoice, MultipleChoice, FreeText, Confirmation), options with a label, a description and a recommended flag, and whether free text is accepted. Answers: chosen labels, text, confirmation, or declining with a message.
- One form waits at a time per turn; a turn waiting on a person is in "awaiting permission" or "awaiting answer", which never counts as a stall.

### Policy and autonomy: why something was allowed

- Each job runs at an autonomy level: **Supervised** (anything not covered by a rule waits for a person) or **Autonomous** (actions inside the worktree are allowed, actions outside are denied instead of asked, forms are answered by the policy). A job may only be stricter than its repository; a request to loosen is refused and recorded.
- Every decision is recorded: the request, the answer (Allow, Deny, Ask), the rule that decided (origin BuiltIn, Repository or Session; name; kind; target pattern; scope Anywhere, Workspace or OutsideWorkspace) and how it was delivered (Answered, LeftToHuman, Undelivered).
- Automatic form answers record their **assumptions**: per field, the prompt, the basis (RecommendedOption, FirstOption, AgentJudgment, Confirmed) and what was chosen.
- Rule files (`.avala/permissions.json`, `.avala/checks.json`, `.avala/budget.json`) are read from the job's base commit, and each reports its origin: the commit, and whether the agent edited its copy in the worktree (ignored for the job, but shown).

### Verification: evidence instead of the agent's word

- After each finished turn, the repository's declared checks run in the worktree. Per attempt: an outcome (Passed, Failed, NoChecksDeclared, InvalidDeclaration); per check: name, command line, status (Passed, Failed, TimedOut, NotFound, Skipped), exit code, duration, and the tails of its output and error streams; and the verdict with the feedback sent back to the agent on a retry.

### Supervision and budgets: nothing runs away

- Supervision holds a job that went silent for a configurable window and records the measured silence against the window.
- Budgets: per-job caps on cost per currency, on tokens, and a threshold on a provider's usage limit. Each intervention records what was measured against the cap.

### Observability

- Usage summarized by provider, by account, by session and by job: tokens by type, costs per currency, unpriced reports, turns by outcome with their total duration, and the latest reading of each limit window.

## Proposed: valuable, not in the core yet

Design these, marked as proposed, because the research shows they solve real problems:

- **Jobs list and history queries, the workspace diff, and approve, send back and discard commands.** The domain has the states but the contracts do not expose them yet; the review screen needs them.
- **Connections** (in progress): several configured instances of one provider, such as "claude-work", "claude-personal" or an API key, each with its own account, usage, limits and caps.
- **Resources** (planned): memory and CPU per agent process tree, attributed to job, session, connection and provider; disk per worktree; ports leased per worktree and observed; orphan processes reaped; worktree retention. Shown globally and per job.
- **Delegation** (planned): an orchestrating agent delegates to sub-agents as governed child jobs on any connection, forming a tree with inherited autonomy and budgets carved from the parent.
- **Recording and replay**: sessions recorded at the agnostic level and replayable; a timeline scrubber that turns a long unattended run into a digest of what happened while away.

## Screens to design

1. **Home: a queue of decisions and evidence, not a list of chats.** Jobs grouped by what they need from the user: needs you (forms, permissions, holds), ready for review, running, done. Throughput instead of activity: verified, approved and blocked today. A live sense that agents are working, through subtle motion, without demanding attention.
2. **Orchestration view.** All running agents across providers and connections as a living, interactive graph or flow: jobs, their sessions, recovery and continuation, and later delegation trees. Smooth, fluid motion: state changes animate, streaming work pulses, a held job visibly stops. Hover and focus reveal detail; nothing requires opening a session to understand it.
3. **Decision inbox.** Every pending permission and form across all jobs, answerable in place with full context: the target, the diff or plan it concerns, how long it has waited, the autonomy level, and what "don't ask again" would create. Keyboard-first. Batch answers where safe.
4. **Job view.** The activity of a session as a calm, animated timeline: streamed messages that type in smoothly, reasoning shown as a living "thinking" animation that can be expanded, tool items collapsed into meaningful one-line summaries with their outcome, the plan as a progressing checklist, canvases inline or pinned. Density control (summary, normal, verbose). Steering: send a message, interrupt, stop.
5. **Proof card and review.** The end of a job as evidence: verification attempts with their checks and output tails, policy decisions and assumptions, rule-file origins (with a warning when the agent edited one), interventions, usage and cost, canvases, and the diff (proposed). Approve, send back with feedback, or discard.
6. **Canvas surface.** Large, focused rendering of SVG, Mermaid, HTML (isolated) and Markdown, streaming without flicker: the previous drawing stays until the new one is complete, with a smooth transition. Version stepping through snapshots.
7. **Usage and limits.** Live meters per provider, account and job (connection when it exists): tokens by type, cost by currency, limit windows with their reset time, budget caps as visible thresholds, and the interventions that hit them. Forecasting time-to-cap is proposed.
8. **Resources** (proposed): global memory, CPU, disk and ports, then drill down by job and agent; orphan processes and stale worktrees with one-click cleanup.
9. **Settings that explain themselves:** autonomy level and form strategy per repository, the effective rules in decision order, the supervision window, budget caps, and each file's status (Absent, Applied, Rejected with its error).

## Deliverables

- A visual language: materials, depth, color tokens for light and dark, typography scale, iconography, motion principles (durations, springs, what animates and why).
- The screens above at desktop size, in light and dark, using realistic data shaped exactly like the contracts: several jobs in different states, a job held as Stalled, one AwaitingReview with two verification attempts (failed, then passed), a pending question form with a recommended option, a pending command permission, an autonomous job with recorded assumptions, a streaming Mermaid canvas, usage near a 5h limit.
- Key interaction flows: answering a form from the inbox, reviewing a proof card and approving, continuing a held job, and following a running agent from the orchestration view into its timeline.
- Proposed elements visibly marked, each with the problem it solves.
