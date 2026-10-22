# Pull requests and CI

Open a pull request from an approved job, and let Avala watch its checks and reviews and wake the agent when they fail.

## What it does

- **Open pull request** pushes the job's branch and opens a pull request on GitHub, Gitea or Forgejo, titled after the first line of the instruction.
- **Avala watches it**: its checks ("2 passed · 1 failed · 0 pending"), its reviews and its conflicts with the base, until it is merged or closed. Once its checks pass the inspector reads "Checks passed · waiting for it to be merged".
- **A delivered job stays in sight.** Its row reads "delivered · #123 · checks 2 of 3" among the project's open jobs, and moves under done as "merged · #123" or "closed · #123".
- **The conversation says how CI ended.** While checks run, the job's header shows "Checks running · 1 of 3", with each check in its tooltip. Once every check of a commit has ended, a card "CI failed: build" names the failing checks, their summaries and a **Log** link to each; **Ask the agent to fix it** sends the agent those checks, when the job takes a message. When the agent was woken for that failure, the card says so instead. When every check passed, one quiet line says so. Each commit gets one card, also on a pull request the agent opened itself.
- **The agent is woken** when the repository allows it: on failing checks and conflicts, and optionally on requested changes, a limited number of times. Each fix is pushed once its turn passes its own checks.

## How to use it

1. **Connect the forge account** in Settings, Accounts, Source control, or from the card a repository's first job opens with; see [Repositories](repositories.md).
2. **Optionally declare the pull request** in the `pullRequest` section of `.avala/jobs.json` and commit it, to choose the connection, the remote or to wake the agent. Without it, the connection that hosts `origin` is used and the pull request is only watched.
3. **Open the review** of a job and choose **Open pull request on <connection>**.
4. **Follow it** in the inspector's **Pull request** section, with **Open in browser** and **Check now**.

## Settings and files

**`.avala/jobs.json`**, `pullRequest` section:

```json
{ "pullRequest": { "forge": "github", "remote": "origin", "onPullRequest": "wake-on-ci-and-reviews", "maxWakeUps": 3, "redeliver": "review" } }
```

- `forge`: required, the name of a forge connection in `forges.json`.
- `remote`: `"origin"` by default.
- `onPullRequest`: `"watch-only"` (the default) never wakes the agent; `"wake-on-ci"` wakes it on failing checks and conflicts; `"wake-on-ci-and-reviews"` also on requested changes.
- `maxWakeUps`: 0 to 20, 3 by default.
- `redeliver`: `"automatic"` (the default) pushes each fix, `"review"` brings each fix back to you first.

`forges.json` in the data folder holds the forge connections and `pollSeconds`, how often they are polled.

## Good to know

- Open pull request shows when the repository declares `pullRequest` or a usable connection hosts `origin`; otherwise the review says which forge to connect.
- A pull request the agent opens itself, for example with `gh pr create`, is found when its turn ends and watched the same way, whether it is on the job's branch or on a branch the agent switched to in its worktree, as long as its head commit was made in the job's worktree: a commit, merge, cherry-pick, revert or rebase its `HEAD` recorded, including one made by `git pull`, not a commit it only checked out, reset to, pulled or fast-forwarded to. Worktrees share their remote-tracking branches, so a push recorded there, which may come from the main checkout or another job, is never enough on its own. One on a branch the agent never checked out, for example made on a detached `HEAD` and given a branch with `git branch`, is found from the link the agent reported, in a command's output, in an MCP tool's result or in its reply, when its head commit was made in the worktree and pushed to that branch: its remote-tracking branch records the push, or the agent pushed the branch by URL to the repository `remote` names, for example `git push https://github.com/owner/repo.git HEAD:fix`, where the credentials may come from a variable such as `https://x-access-token:$GITHUB_TOKEN@github.com/owner/repo.git`. A push by URL counts only when its success is certain from the command line: alone, or joined to the rest by `&&`, never after `||`, before `;`, in a pipeline, in a quoted argument or a comment, as a dry run, a deletion or a push of submodules only, or with an option git would not read unambiguously. Links printed by a command or a tool that creates a pull request, such as `gh pr create` or an MCP `create_pull_request`, are tried first. What the agent reported is kept with the job in `forges.db` until the job is approved, discarded or fails, so a restart before review does not forget it. A pull request opened from another clone, the main checkout or another job, or one whose branch was only fetched or checked out here, for example with `gh pr checkout`, is not adopted, even when the agent names it.
- A pull request whose head branch is in a fork is not adopted, and is never taken for the job's own pull request by its branch name, even when its head commit is one this clone pushed. Avala pushes each fix to the branch of the repository `remote` names and reads the pull request's head from there, so it could neither update nor follow a head that lives in another repository. Open the pull request from a branch of the repository `remote` names.
- Without a connection, a pull request the agent reports is named once above its conversation: "Pull request #123 was opened by the agent. Connect github.com in Settings to follow its checks."
- The forge is polled, so nothing needs to reach your computer from outside.
- Avala pushes with your own git credentials and keeps only a reference to a forge token, never the token.

## See also

- [Checks and review](checks-and-review.md)
- [Autopilot and triggers](autopilot-and-triggers.md)
- [Repositories](repositories.md)
