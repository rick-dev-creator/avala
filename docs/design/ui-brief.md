# Avala — design brief, version 2

This replaces the first brief. The first round packed every screen with every piece of data, used a different saturated color for each state, buried the conversation inside an event timeline, and its sample job (an agent drawing an architecture diagram) read like an "architecture" section of the app. Start again from the visual language, with the direction below.

## What Avala is

An open-source, cross-platform desktop agent harness (Avalonia/.NET: macOS, Windows, Linux). It runs coding agents (Claude Code first, any harness later), each in its own git worktree, and lets them work unattended while every decision is governed, verified and audited. Thesis: **agents you can trust without watching.**

A developer works by **talking to agents**. The conversation is the heart of the app. Everything else Avala knows (evidence, decisions, usage, holds) supports that conversation and appears when it matters, not all at once.

## Design direction

**Calm, focused, premium.** It should feel like the best native macOS apps (Things 3, Craft, Raycast, Linear's restraint, the Claude Code desktop app's conversation), not like a dashboard.

- **One idea per screen. Detail on demand.** Each screen answers one question. Everything secondary lives in an inspector panel, a popover or a hover, and appears only when asked.
- **Depth instead of color.** Hierarchy comes from translucent layered materials, soft shadows and spacing, never from colored fills.
- **Restraint in color.** One neutral surface family, one accent, and at most two state colors: attention (amber) and failure (red). Everything else (running, done, idle) is neutral, told apart by a small dot, a label and motion. No colored cards, no rainbow badges.
- **Motion that means something.** Spring physics. Streaming text that flows in smoothly. A living "thinking" animation while the agent reasons. A pulse while an agent works, and the pulse stopping when it is held: stillness is the alert. Nothing moves without a reason.
- **Typography:** Inter for the interface, JetBrains Mono for code, paths and commands. A clear, small type scale.
- **Dark first.** Light mode comes once the visual language is approved.

## The design system: Avala's own, inspired by macOS

Follow Apple's Human Interface Guidelines for macOS (materials, vibrancy, depth, layering, restraint, content first) without copying macOS chrome, because Avala also runs on Windows and Linux.

- **Three material levels:** window background, sidebar/panel, floating card or popover. Each one is translucent with blur where the platform allows it: vibrancy on macOS, Mica or Acrylic on Windows, and an elegant solid fallback on Linux. Design the solid fallback too.
- **Tokens:** surfaces, text (primary, secondary, tertiary), separators, one accent, attention, failure; radii; spacing scale; shadows per material level.
- **Motion tokens:** spring presets (snappy, gentle), durations, and the rule for what animates.
- **Components:** sidebar row with status dot, message bubble or turn block, collapsed tool line, permission card, form card, inspector section, status pill, meter, empty state.

## What the core produces (stay within it)

Use only this data. Anything else must be marked "Proposed" with the real problem it solves.

- **Jobs:** repository, instruction, status (Draft, Preparing, Running, Checking, NeedsHelp, AwaitingReview, Approved, Discarded, Failed), the reason a job is held (Stalled, SessionLost, BudgetExceeded, LimitNearlyReached, InvalidBudget), autonomy (Supervised or Autonomous), the connection it runs on (a named instance of a provider, such as "claude-work" or "claude-personal"), a worktree with its branch and base commit, the time it was submitted, and its history of sessions and attempts. Commands: submit, continue a held job with a message, and for a job awaiting review: approve (delivered by the repository's strategy, keep the branch or merge it into the base branch as one commit, or refused with a reason such as a conflict or a base checkout with uncommitted changes), send back with feedback, discard.
- **Diff:** the files a job changed against its base commit, added, modified or deleted, with lines added and removed; the hunks of one file on demand; the files that conflict with the base branch.
- **Conversation:** turns that finish, are interrupted or fail; inside a turn, items of kind Message, Reasoning, FileEdit, Command, Search, Web, Mcp, Subagent, with a title, streamed text and an outcome; a plan of steps (Pending, InProgress, Done). Commands: send a message, interrupt, stop.
- **Human input:** permission requests (title, kind, target such as a path or a command line), answered Allow or Deny, with an optional message to the agent and "don't ask again" for the rest of that agent session, worded with its scope. Forms with a purpose (Permission, Question, PlanApproval, Other) and fields (single choice, multiple choice, free text, confirmation) whose options may be marked recommended.
- **Evidence:** per attempt, verification checks with status (Passed, Failed, TimedOut, NotFound, Skipped), exit code, duration and output tail; the policy decisions and the rule that made each; the assumptions recorded when the policy answered for the user; whether the agent edited a rule file.
- **Canvas:** SVG or Markdown drawn by the agent, streaming, in the media types Avala offers; a canvas in any other type shows its source.
- **Usage:** tokens by type, cost by currency, usage limit windows with their reset time, by provider, account, connection and job, kept across restarts and readable over any time window or per day; budget caps and the interventions that hit them, kept across restarts.
- **Delegation:** an orchestrator's tree of child jobs, each with its status, the connection it was routed to, its autonomy, what it spent against the budget carved for it, and its outcome as reported back to the orchestrator: integrated into the orchestrator's work, a conflict, held, failed; refused delegations with their reason.
- **Proposed** (not in the core yet, valuable): replay of a session. Resources per agent (memory, CPU, disk, ports, leftover processes, stale worktrees) are in the core since the resources step, and delegation trees since the delegation step.

## Screens, in this order

1. **Visual language page first**, for approval: materials, tokens, type, motion, the core components above.
2. **The main window**, which is where the developer lives:
   - **Sidebar:** jobs grouped by what they need: Needs you, Running, Ready for review, Done. Each row is one line: the job's title, a status dot and at most one short secondary fact (for example "asks a question" or "held: stalled"). A small badge for pending decisions.
   - **Conversation:** the selected job's chat with its agent. User messages and agent replies; reasoning as a collapsible thinking block with its live animation; tool items (edits, commands, searches) as single collapsed lines with their outcome, expandable; the plan as a compact checklist; canvases inline. Permission requests and forms appear in the flow as cards answered in place. A composer at the bottom to send, interrupt or stop.
   - **Inspector** (closed by default, opened on demand): the job's evidence, decisions and assumptions, usage and caps, autonomy and connection, worktree. Each as a short section, never all expanded.
3. **Review:** the end of a job as a calm proof summary: verdict first, then only the exceptions (failed checks, denials, assumptions, holds, an edited rule file), then the diff. Approve, send back with feedback, or discard; an approval refused shows its reason in place.
4. **Decisions popover:** every pending permission and form across jobs, answerable in place, keyboard first.
5. **Overview** (optional view): all running agents across connections as a light, fluid graph with smooth motion, hover for a one-line summary, click to open the conversation.
6. **Usage:** simple meters per connection and job, limit windows with reset time, budget caps as thresholds.
7. **Settings:** autonomy and form strategy per repository, the effective rules, connections, supervision window, budget caps, with each file's status.

## Sample data

Ordinary development work, never architecture diagrams as the main example: fix a failing test, add an API endpoint, refactor a module, update a dependency. Include: one job running and streaming; one asking a question with a recommended option; one asking permission to run a command; one held as Stalled; one awaiting review after a failed then passed verification; one autonomous job with recorded assumptions; one small Mermaid canvas inside a conversation; two connections, "claude-work" and "claude-personal", one close to its 5-hour limit.

## Deliverables

The visual language page, then the main window in its three states (conversation streaming, a permission card answered in place, inspector open), then the review, decisions popover, overview, usage and settings, all dark. Keep every screen sparse; when in doubt, hide it behind the inspector.
