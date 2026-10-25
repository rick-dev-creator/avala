# Budgets and usage limits

Caps on cost, tokens and memory hold a job before it overspends, and a job that nears an account's usage limit waits for the reset or moves to another account.

## What it does

- **Caps per job**, set by the repository: cost, tokens, memory, and the share of a provider's usage window at which a job holds.
- **Caps per machine**, set by you: how many jobs run at once and a daily spending ceiling, for the machine and per connection.
- **A hold, not a crash.** A job that reaches a cap is held, "held: over budget" or "held: out of memory", and keeps its worktree and conversation. Raise budget and continue lets it go on.
- **Usage limits.** A job that nears or reaches an account's usage window is held, or is handed off to another connection with room left, with a brief of the work so far built from Avala's own records.
- **Elastic Harness Chaining.** Elastic Harness Chaining (EHC) is a relay race for AI agents: when an agent is about to run out of memory or tokens, it packages its progress and hands it off to a fresh execution environment, a harness, so a long, complex task keeps going instead of collapsing. With a handoff chosen, a job that passes the point where it prepares, 90% of a window by default, is first asked to wind down: it finishes the step it is on, leaves its work in a valid state, stops its sub-agents and ends its turn with a note of what is done and what is left. The baton then passes to the next connection, chosen by the strategy you pick, and the job goes on there from its brief. A job still working when it reaches the hard cap, 98% by default, is stopped there and relayed all the same. Across three or four subscriptions a job can loop from one to the next as each window fills and resets. A held job waits for the window to reset, then waits for you: its row says "window reset · waiting for you", and your next message resumes it in its conversation. It does not resume by itself unless you choose `"resumeAtReset": "auto"`. A job held at the limit while the harness reported no usage, or none above zero, is treated as having spent its window rather than continued at once; when the reset time is unknown, a job that resumes by itself goes on once the window's length, such as 5 hours for `5h`, has passed.
- **Costs at API prices.** On a subscription login, costs are API-equivalent amounts, labelled "at API prices".
- **Supervision.** A job whose turn falls silent for too long is held as "stalled"; a session that dies is held as "session lost". Waiting for your answer never counts as silence, nor does a conversation left idle between turns, however long: the next message starts a turn as usual.

## How to use it

1. **Set a repository's caps** in `.avala/budget.json` and commit it; Settings, Repository rules shows them.
2. **Set the machine's limits** in `budgets.json` in the data folder.
3. **Choose what happens at a limit** in the `limits` section of `.avala/jobs.json`, or for every repository in `limits.json` in the data folder.
4. **Follow spending** on the **Usage** page: Connections, Jobs · cost against their cap, Interventions across jobs and Usage by day, for Today, 7 days or 30 days.
5. **Choose it in Settings.** Settings, Machine, **Usage limits** sets what a job does near a connection's limit: **Near a connection's limit** (Hold the job, Hand off on the same harness, Hand off to any harness), **Prepare at**, **Continue by itself at the reset**, **Compact before continuing**, and, for a handoff, **Wind down first**, **Hard cap**, **Next connection** and **Connections in the relay**, in order. A sentence under it says what will happen; **Save** writes `limits.json` and applies it at once.
6. **Watch the relay.** A job that has ever relayed, waited for a reset or wound down shows the relays glyph in its header; it opens the **Relays** pane beside the conversation. It draws one lane per connection, with what is left of its window, and the baton's route across them as it happens: each handoff, each wait for a reset, each cap. Under it, the journey lists every leg, where it ran, for how long and what ended it.
7. **See what is left while you work.** A job's header shows what is left of the most used usage window on its connection, such as "28% usage left", and turns amber near the limit. Hover it for every window with its use and reset; click it to open Usage with that connection marked. A connection that reports no usage window, such as one with an API key, shows nothing there.
8. **Continue a held job** with **Raise budget and continue** in its inspector, which adds one more original allowance above what it already spent, or move it with "Continue on" another connection from its harness chip.
9. **Change the silence** after which a job is held in Settings, Machine, Supervision: "Hold a job after silence of … seconds".

## Settings and files

**`.avala/budget.json`**, in the repository:

```json
{ "costPerJob": { "USD": 5 }, "tokensPerJob": 2000000, "holdAtLimit": 0.9, "memoryPerJobMegabytes": 4096, "carvePerChild": 0.5 }
```

- `costPerJob`: an amount per currency. `tokensPerJob`, `memoryPerJobMegabytes`: whole numbers.
- `holdAtLimit`: the share of a usage window, above 0 and at most 1, at which a job holds.
- `carvePerChild`: the share of its parent's remaining budget a sub-agent gets, between 0 and 1; 0.5 by default.
- `connections`: the same caps for a named connection, such as `{ "team-api": { "costPerJob": { "USD": 10 } } }`.

**`budgets.json`**, in the data folder:

```json
{ "runningJobs": 2, "spendPerDay": { "USD": 40 }, "connections": { "team-api": { "spendPerDay": { "USD": 10 } } } }
```

Without `runningJobs`, any number of jobs run at once.

**Limits**, as the `limits` section of `.avala/jobs.json` or as `limits.json` in the data folder:

```json
{ "onLimit": "handoff-any-harness", "threshold": 0.8, "connections": ["claude-work", "claude-personal"] }
```

- `onLimit`: `"hold"` (the default), `"handoff-same-harness"` or `"handoff-any-harness"`.
- `threshold`: the share of the window at which a job moves, 0.9 by default.
- `connections`: the connections a job may move to.
- `resumeAtReset`: `"ask"` (the default), so a held job waits for your message once its window resets, or `"auto"`, so it resumes by itself the moment the window resets.
- `hardCap`: the share of a window a job winding down is stopped at, from the threshold up to 1; 0.98 by default, or the threshold when it is higher.
- `strategy`: which connection takes the baton: `"most-headroom"` (the default), `"in-order"`, the first in `connections`, `"soonest-reset"` or `"round-robin"`, the next in `connections` after the one the job is on.
- `windDown`: `true` (the default) asks the agent to wind down before a handoff; `false` relays at the end of the turn without asking.
- `compactOnResume`: `true` (the default) compacts the conversation before a job continues on the connection it waited for, on a harness that compacts on request.

**`supervision.json`**, in the data folder: `{ "silenceSeconds": 900 }`, 15 minutes by default, at most a day.

## Good to know

- A file that cannot be read holds the job as "invalid budget" rather than running it without caps; a rejected `budgets.json` lets one job run at a time.
- A job with no reset time reported waits for you.
- With `"hold"`, a job past the threshold holds when its turn ends rather than starting another; it is not stopped mid-turn, and the hard cap and winding down apply only to a handoff.
- Without `connections`, only a job that started on Auto relays, among the connections Auto compared; a job started on a named connection relays only when that connection is listed.
- With no connection left below the threshold, a relaying job waits for the first window to reset, then continues, or asks you, as `resumeAtReset` says.
- A repository's `limits` section replaces `limits.json` as a whole, not key by key.
- Why resuming waits for you by default: a job held for hours has lost its prompt cache, so its first message reads its whole conversation again. On a long conversation that alone can take a fifth of a 5-hour window, which `"auto"` would spend while nobody is there.
- A handoff starts a new conversation on the other connection; its brief holds the instruction, the plan, the files changed, the checks, the decisions taken, the open questions, what you sent during a turn that did not finish, Avala's own notes from that turn listed apart, and what remains.
- The daily ceiling also pauses an Autopilot loop until the next day.

## See also

- [Delegation and orchestration](delegation-and-orchestration.md)
- [Best of N](best-of-n.md)
- [Notifications](notifications.md)
