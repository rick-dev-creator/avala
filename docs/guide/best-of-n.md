# Best of N

Run one request as several candidate jobs, and keep the best by its evidence.

## What it does

- **Each candidate is an ordinary job**, in its own worktree with its own session, retries and checks, and an even share of a job's budget.
- **The verdict comes from evidence, never from an agent's word.** Once every candidate has settled, a candidate must pass its checks and must not have edited a rule file. The rest are ranked by fewest exceptions, fewest attempts, smallest diff, lowest cost and earliest finish.
- **Approving any candidate keeps it** and discards the others, whose worktrees are freed like any discarded job's.
- **An answer given "in this contest"** to one candidate's permission request goes to every candidate.

## How to use it

1. **Allow more than one candidate** in the `bestOf` section of the repository's `.avala/jobs.json`.
2. **Start a job** from **New job**: set **Candidates** with − and +, and tick **Spread across connections** to run each candidate on the next usable connection in turn instead of all on one. Start.
3. **Wait for the verdict** in the inspector's **Contest** section: "Waiting for every candidate to settle", then "Judging the candidates", then "Decided: the winner waits for your review".
4. **Review and approve** the winner, or any other candidate; see [Checks and review](checks-and-review.md). When no candidate passed, continue one or discard them.

## Settings and files

**`.avala/jobs.json`**, `bestOf` section:

```json
{ "bestOf": { "candidates": 3, "max": 4 } }
```

- `candidates`: how many candidates a job starts with, 1 by default.
- `max`: the most a contest may have, 1 to 8; 4 by default.

## Good to know

- A contest needs at least two candidates; Candidates only shows when `max` is above 1.
- When the job names a connection, every candidate runs on it, even with Spread across connections.
- Each candidate may spend its share of a job's budget, such as a third for three candidates.

## See also

- [Checks and review](checks-and-review.md)
- [Budgets and usage limits](budgets-and-usage-limits.md)
