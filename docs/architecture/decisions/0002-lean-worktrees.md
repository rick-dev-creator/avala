# Lean worktrees

## Status

Accepted. All three steps of the delivery are built.

## Context

Every job runs in a git worktree of its own under the data folder's `worktrees` folder, on a branch `avala/<id>`. A worktree is a full checkout, and once an agent has built or installed in it, its build outputs, such as `node_modules`, `bin`, `obj` or `target`, usually weigh far more than its code.

Resources keeps a worktree after its job ends for as long as `resources.json` says: a discarded job's for 0 hours, a failed job's for 168 hours and an approved job's forever. Removing one is destructive: Workspaces deletes the folder, the branch and its record, so the job can never continue there. Reopening an approved job whose worktree is gone is refused as `WorkspaceUnavailable`, which is why approved worktrees are kept by default.

The result is a computer that fills up with dead worktrees:

- An approved job whose work is already on `main`, merged by Avala, through a pull request or by hand, keeps its whole checkout forever.
- Archiving a conversation, the clearest sign that a person is done with it, changes nothing on disk.
- A worktree no job knows about, left by a crash, is only reported at startup.
- Nothing watches how much space worktrees take in total, or how much the disk has left.
- The only way to change any of this is to edit `resources.json` by hand.

Avala must stay lean: the person running it should never have to think about worktrees, and Avala should never saturate the computer it runs on.

## Decision

A worktree's folder is disposable; its work is not. A worktree exists only while its job works or waits for its person. Every other worktree is **freed**: its folder is removed and its work kept, and it is rebuilt at the same path when its job continues.

### Freeing and rebuilding

Workspaces gains a third state for a workspace, beside live and removed: **shelved**.

- **Shelve.** If the folder has changes no commit holds, they are committed first, with Avala's identity, so nothing is lost. The branch's tip is then kept under a hidden ref, `refs/avala/shelved/<id>`, the branch `avala/<id>` is deleted, so it no longer crowds `git branch`, and the folder is removed with `git worktree remove --force`. The workspace record stays, marked shelved.
- **Rebuild.** `git worktree add -b avala/<id> <same path> refs/avala/shelved/<id>` recreates the folder at the path it had, and the hidden ref is dropped. Since the path is the same, a harness that keys its conversations on the folder, as Claude Code does, resumes the same conversation.
- **While shelved**, the diff, the conflict check and the merge read the hidden ref instead of the branch, so a shelved job's review, inspector and delivery work as before.
- **Remove** stays as it is for a job whose work is no longer wanted, and also deletes a shelved workspace's hidden ref.

Jobs rebuilds a shelved workspace before it opens a session for a job that continues, such as an approved job that receives a message, is reopened or is woken by its pull request. The board no longer refuses messages to an approved job whose worktree was freed.

Shelving runs inside `IEndedJobs.WhileEndedAsync`, as removal already does, so a job that continues at the same moment is never shelved under it.

### Knowing that work is on main

Work counts as **delivered** when any of these holds:

- Avala merged it: `refs/avala/merged/<id>` points at the job's current tip.
- Its pull request was merged, as the forge reports through `PullRequestWatchChanged` with `WatchEnd.Merged`. This covers squash merges.
- git finds the job's tip in its base branch, or in that branch's upstream: `git merge-base --is-ancestor`. This covers a job approved with `keep` and merged by hand.

Workspaces answers this, with shelving and rebuilding, through a contract of its own, `IWorkspaceShelf`. Resources asks when a job is approved, at startup and on a slow sweep, so a merge made outside Avala is noticed without the person doing anything.

### What happens at each moment

Resources owns the policy, as it owns retention today. A worktree is freed at the moment its job reaches one of these, after the delay its setting gives:

| Moment | Default |
| --- | --- |
| Archived | Free at once |
| Approved and delivered | Free at once |
| Approved, pull request open | Free at once; rebuilt if the pull request wakes the job |
| Approved, not delivered (`keep`) | Free after 1 day |
| Discarded | Free at once |
| Failed | Free at once |
| Awaiting review or held, untouched | Free after 3 days |

Freed work is deleted, its hidden ref with it, 7 days after a discard and 30 days after a failure. An approved job's work is never deleted automatically: a hidden ref costs only the commits it keeps, which a merge has usually brought into the base branch already.

Two guards keep the computer from filling regardless of the moments above:

- **A space budget**: worktrees together may use at most 20 GB by default.
- **A disk floor**: Avala keeps at least 10% of the disk that holds the data folder free.

When either is crossed, Avala frees the worktrees whose jobs have been idle the longest, never one whose job is running or checking, until both hold again. If that is not enough, the Worktrees page says once what still takes the space.

A worktree no job knows about is removed at startup by default: `reconcile` becomes `clean`.

### Settings, not files

A new Settings section, **Worktrees**, shows each moment as a choice: keep the worktree, free it at once or free it after a delay. Below them, the person chooses when a discarded and a failed job's freed work is deleted, or never; the space budget and the disk floor join them in the second step. The person never edits a file. The choices are kept in the `worktrees` section of `resources.json`, in hours, and saving rewrites only that section. The existing `keepDiscardedHours`, `keepFailedHours` and `keepApprovedHours` keep reading, as when a discarded or failed job's work is deleted and when undelivered approved work is freed.

### The Worktrees view

A page of its own, reached from the disk figure of Resources, tells the person what Avala does with their disk, without asking anything of them:

- **Now**: the space worktrees take and how many there are, by state; the largest, with their jobs and how much of each is build output; and what will be freed next, and when.
- **Trends**, for 7 or 30 days like Usage: space taken per day and its peak, worktrees created against freed, the space Avala recovered and why, and how long a worktree lives.
- **History**: each time Avala freed or deleted worktrees, how much and why, linked to the job, which can still be reopened.

Resources records each worktree freed, with its size and reason, and each one deleted, in a database of its own, `resources.db`, with a daily sample of the space taken, so the view has data from the day it ships.

### Delivery

1. **Delivered work**: shelving and rebuilding, delivery detection, the moments archived, approved, discarded and failed, and the Worktrees section of Settings.
2. **Dead work**: the idle moment, the space budget, the disk floor and cleaning unknown worktrees at startup.
3. **The Worktrees view** and its history.

## Alternatives considered

- **Keep the branch and remove only the folder.** Simpler, but every job ever run leaves an `avala/<id>` branch in the person's repository. The hidden ref keeps the same commits without crowding branches.
- **Remove everything once delivered.** The leanest, but the job could no longer be reopened, and its review would lose its diff. A hidden ref costs almost nothing in comparison.
- **Delete build outputs only, keeping the checkout.** Frees most of the space, but has to know every stack's output folders, and still leaves one checkout per job.
- **Time-based retention alone, as today.** It cannot react to a disk that fills faster than the hours it waits, and it does not know when work has reached `main`.

## Consequences

- Avala's footprint follows the jobs that are working, not every job ever run.
- Continuing a freed job costs a `git worktree add` and whatever the agent has to rebuild or install again, typically seconds to minutes, the first time it runs a build.
- A discarded job's work is now kept for 7 days instead of being deleted at once, so a discard made by mistake can be undone during that time.
- Resources depends on the contracts of Projects, to learn what is archived, and of Forges, to learn what was merged.
- The person can see, in one place, what Avala keeps on their disk and why.
