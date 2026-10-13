# Budgets and usage limits

Caps on cost, tokens and memory hold a job before it overspends, and a job that nears an account's usage limit waits for the reset or moves to another account.

## What it does

- **Caps per job**, set by the repository: cost, tokens, memory, and the share of a provider's usage window at which a job holds.
- **Caps per machine**, set by you: how many jobs run at once and a daily spending ceiling, for the machine and per connection.
- **A hold, not a crash.** A job that reaches a cap is held, "held: over budget" or "held: out of memory", and keeps its worktree and conversation. Raise budget and continue lets it go on.
- **Usage limits.** A job that nears or reaches an account's usage window is held, waits for the reset and resumes in its conversation, or is handed off to another connection with room left, with a brief of the work so far built from Avala's own records.
- **Costs at API prices.** On a subscription login, costs are API-equivalent amounts, labelled "at API prices".
- **Supervision.** A job whose turn falls silent for too long is held as "stalled"; a session that dies is held as "session lost". Waiting for your answer never counts as silence, nor does a conversation left idle between turns, however long: the next message starts a turn as usual.

## How to use it

1. **Set a repository's caps** in `.avala/budget.json` and commit it; Settings, Repository rules shows them.
2. **Set the machine's limits** in `budgets.json` in the data folder.
3. **Choose what happens at a limit** in the `limits` section of `.avala/jobs.json`, or for every repository in `limits.json` in the data folder.
4. **Follow spending** on the **Usage** page: Connections, Jobs · cost against their cap, Interventions across jobs and Usage by day, for Today, 7 days or 30 days.
5. **See what is left while you work.** A job's header shows what is left of the most used usage window on its connection, such as "28% usage left", and turns amber near the limit. Hover it for every window with its use and reset; click it to open Usage with that connection marked. A connection that reports no usage window, such as one with an API key, shows nothing there.
6. **Continue a held job** with **Raise budget and continue** in its inspector, which adds one more original allowance above what it already spent, or move it with "Continue on" another connection from its harness chip.
7. **Change the silence** after which a job is held in Settings, Machine, Supervision: "Hold a job after silence of … seconds".

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

**`supervision.json`**, in the data folder: `{ "silenceSeconds": 900 }`, 15 minutes by default, at most a day.

## Good to know

- A file that cannot be read holds the job as "invalid budget" rather than running it without caps; a rejected `budgets.json` lets one job run at a time.
- A job with no reset time reported waits for you.
- A handoff starts a new conversation on the other connection; its brief holds the instruction, the plan, the files changed, the checks, the decisions taken, the open questions and what remains.
- The daily ceiling also pauses an Autopilot loop until the next day.

## See also

- [Delegation and orchestration](delegation-and-orchestration.md)
- [Best of N](best-of-n.md)
- [Notifications](notifications.md)
