# Worktrees and disk space

Avala frees the disk a job's worktree takes once the job no longer needs it, so finished work never fills your computer.

## What it does

- **A worktree lives while its job works or waits for you.** Every job runs in a worktree of its own under the data folder's `worktrees` folder. Once the job is archived, approved, discarded or failed, Avala frees it after the delay Settings gives that moment. A job waiting for your review or held, with no agent running, is freed too once you have left it untouched for three days, counted from when it started waiting, across restarts. Archiving a conversation frees its worktree whether its job ended or waits for you.
- **Freeing keeps the work.** The worktree's folder is removed, with everything a build left in it, but its changes stay in your repository under a hidden ref, `refs/avala/shelved/<id>`, so its `avala/<id>` branch no longer shows in `git branch`. Changes no commit held yet are committed first. Files git ignores that are not build output, such as a `.env` or a local configuration, are moved aside to the data folder's `shelved` folder and put back when the worktree is rebuilt.
- **A worktree whose checkout left its branch is left alone while that could lose work**: on a detached HEAD, for example during a rebase, or on another branch with changes no commit holds. One the agent switched to another branch with everything committed is freed as usual, and that branch stays in your repository. A worktree left alone is listed as kept on the Worktrees page, with why.
- **A freed job continues where it was.** Sending a message to a job, approving it, reopening it, or a pull request that wakes it rebuilds its worktree at the same path, and the agent resumes its conversation. The first build after that may take longer, since what the build left was removed.
- **Avala knows when work reached your base branch**: when it merged the work itself, when the forge reports the job's pull request merged, or when git finds the job's last commit on the base branch or on its upstream, so a merge you made by hand counts too. Avala checks again every ten minutes.
- **Deleting is separate.** The freed work of a discarded or failed job is deleted for good later, after a week and after 30 days by default. Approved work is never deleted by itself.

| Moment | By default |
| --- | --- |
| Archived | Freed at once |
| Approved and on its base branch | Freed at once |
| Approved, pull request open | Freed at once |
| Approved, not delivered yet | Freed after 1 day |
| Discarded | Freed at once, its work deleted after 7 days |
| Failed | Freed at once, its work deleted after 30 days |
| Waiting for you, untouched | Freed after 3 days |

When several moments apply, the earliest wins: an approved job you archive is freed at once even if its approval would wait a day.

- **Two limits keep the disk from filling whatever the moments say.** Worktrees together may take 20 GB, and the disk holding the data folder keeps 10% free. Past either, Avala frees the worktrees idle the longest first, never one whose job is working. When nothing more can be freed, the Worktrees page says so.
- **Worktrees no job knows about**, left by a crash, are removed when Avala starts, once nothing has changed in them for an hour, so a worktree being added at that moment is never touched. A job's worktree whose folder is missing is freed instead of forgotten, so its work stays reachable.
- **The Worktrees page** shows what worktrees take now, the largest and how much of each is build output, what is freed next and when, the space taken day by day over 7 or 30 days with the worktrees created and freed, the space recovered and why, how long a worktree lives, and the latest worktrees Avala freed or deleted, each with **Open** to go to its job. **Free space now** frees every worktree whose job is not working, keeping its work, and says how many it kept because their checkout left its branch.

## How to use it

1. Open **Settings** and choose **Worktrees**.
2. Under **Free a worktree**, choose for each moment **Keep it**, **Free it at once** or **Free it after** a delay.
3. Under **Delete freed work**, choose when a discarded or a failed job's work is deleted, or **Never**.
4. Under **Limits**, choose **Space for worktrees** and **Free disk to keep**, or **No limit** and **No floor**, and whether to **Remove unknown worktrees at start**.
5. Choose **Save**. The choices apply to worktrees already waiting too.
6. To see what Avala does with the disk, open **Resources** from the sidebar's footer and choose **in worktrees ›** under Disk.

The inspector's worktree section of a freed job reads "freed, rebuilt when the job continues".

## Settings and files

**`resources.json`**, in the data folder, keeps the choices under `worktrees`, in hours, `0` for at once and `null` to keep or never delete:

```json
{
  "worktrees": {
    "freeArchivedHours": 0,
    "freeDeliveredHours": 0,
    "freePullRequestHours": 0,
    "freeUndeliveredHours": 24,
    "freeDiscardedHours": 0,
    "freeFailedHours": 0,
    "deleteDiscardedHours": 168,
    "deleteFailedHours": 720,
    "freeIdleHours": 72,
    "spaceBudgetGigabytes": 20,
    "diskFloorPercent": 10,
    "reconcile": "clean"
  }
}
```

`spaceBudgetGigabytes` and `diskFloorPercent` take `null` for no limit. `reconcile` is `clean` by default; `report` only lists worktrees no job knows about, on the Resources page. Saving from Settings changes only the `worktrees` section and keeps every other setting in the file.

What Avala freed and deleted, and the space taken each day, are kept in `resources.db` in the data folder, for the Worktrees page. The earlier fields still read: `keepDiscardedHours` and `keepFailedHours` set when the work is deleted, and `keepApprovedHours` when undelivered approved work is freed.

## Good to know

- A freed folder that a build had filled, such as `node_modules`, `bin`, `obj` or `target`, is rebuilt only when the agent builds again.
- A job that works again when its worktree is due to be freed keeps it, and its delay starts over once it ends again.
- A squash merge made on the forge without a pull request Avala watches is not recognized, so that job is freed after the delay for undelivered work.
- The space a worktree takes is measured once a minute, so a limit is enforced within a minute of being crossed.
- Peeking a file cited in a freed job's conversation needs its worktree, so it is not shown until the job continues; the diff does not.
- A worktree Avala could not free, for example because a file in it is locked, is tried again ten minutes later.
- Deleted work is listed without a size, since its folder was freed before.
- See [ADR-0002](../architecture/decisions/0002-lean-worktrees.md) for the design.

## See also

- [Jobs and conversations](jobs-and-conversations.md)
- [Checks and review](checks-and-review.md)
- [Pull requests and CI](pull-requests-and-ci.md)
- [Troubleshooting](troubleshooting.md)
