# Agent harness interfaces in 2026

Research done on 2026-10-09 before designing Avala's user interface. It covers dedicated harnesses (Claude Code desktop and agent view, Codex app, T3 Code, Conductor, Superset, Vibe Kanban, Nimbalyst, Sculptor, Emdash) and agent experiences inside editors and platforms (Cursor, Windsurf and Devin Desktop, Zed, GitHub Copilot's agents, Devin, Factory, Warp, JetBrains Air, Google Antigravity and Jules, Amp). Many details come from secondary sources; the claims below are the ones several sources agree on.

## What the products converged on

- One task is one worktree, one branch and one pull request.
- A sidebar of sessions, increasingly grouped by state rather than recency: working, needs you, ready for review, done.
- Three panes: the conversation or terminal, the diff, and a preview or browser where the agent checks its own interface work.
- Tool calls collapsed by default, with a density control. Diff stats as `+N −M` badges.
- Line comments on a diff that go back to the agent; one-click pull requests; CI status inside the app; sessions archived when their pull request merges.
- A plan mode approved or commented before any file changes.
- A graded autonomy ladder instead of a yes-or-no switch, with allow and deny lists, administrator caps, and more and more a classifier instead of a human for routine approvals.
- Steering a running agent between tool calls.
- Scheduled automations whose results land in a triage queue.

## What keeps failing

1. Review is the bottleneck. Parallel agents turn typing time into reading time; users settle at two to six agents, and report feeling busy without moving faster.
2. Attention signals are unreliable. Permission notifications do not fire, fire falsely, or cannot be answered without opening the session.
3. Usage is opaque. Limits are hidden in menus or contradict the command line, third-party tray apps fill the gap, parallel work drains quotas without warning, and spend caps are rare.
4. Safety is binary: prompt for everything or bypass everything. Almost no product documents an audit log.
5. Stalls go unnoticed: sessions stuck for hours with no signal.
6. Isolation stops at the files: ports, databases, processes and machine load collide between worktrees, and leftover processes and worktrees pile up.
7. Polish at scale: sidebars that lag past ten sessions, premature archiving, lost layouts, approval forms that hide the change they approve, flickering streamed diagrams.

## Where Avala can be clearly better

Avala already has the pieces each of these needs.

- **Evidence-first review.** Every job ends with a proof card: the gates that ran and their output, the policy decisions, the canvases and the diff summary. A reviewer trusts the result without reading every line.
- **One decision inbox** fed by the generic forms. Permissions, questions and plan approvals answered in place, with their context and the diff, the wait time, and "don't ask again" writing an audited rule.
- **Autonomy visible per job:** what was approved automatically and by which rule.
- **States nobody shows:** stalled, lost, over budget, leaking resources, each with one-click actions.
- **Spending as a live meter** per job, account and connection, with a forecast of when the cap is reached.
- **Resources accounted for:** processes, ports, memory and disk per job and globally, with orphans reaped and worktrees reclaimed.
- **Canvas as a work surface** for plans and diagrams, rendered without flicker: the previous drawing stays until the new one is complete.
- **Replay as the digest of what happened while away.**
- **A home screen that is a queue of decisions and evidence**, not a list of chats, with throughput in place of activity: verified, merged and blocked today.
