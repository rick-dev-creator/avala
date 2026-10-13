# Pull requests and CI

Open a pull request from an approved job, and let Avala watch its checks and reviews and wake the agent when they fail.

## What it does

- **Open pull request** pushes the job's branch and opens a pull request on GitHub, Gitea or Forgejo, titled after the first line of the instruction.
- **Avala watches it**: its checks ("2 passed · 1 failed · 0 pending"), its reviews and its conflicts with the base, until it is merged, closed or its checks pass.
- **The agent is woken** when the repository allows it: on failing checks and conflicts, and optionally on requested changes, a limited number of times. Each fix is pushed once its turn passes its own checks.

## How to use it

1. **Connect the forge account** in Settings, Accounts, Source control; see [Repositories](repositories.md).
2. **Declare the pull request** in the `pullRequest` section of `.avala/jobs.json` and commit it.
3. **Open the review** of a job and choose **Open pull request**.
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

- Open pull request only shows when the repository declares `pullRequest`.
- The forge is polled, so nothing needs to reach your computer from outside.
- Avala pushes with your own git credentials and keeps only a reference to a forge token, never the token.

## See also

- [Checks and review](checks-and-review.md)
- [Autopilot and triggers](autopilot-and-triggers.md)
- [Repositories](repositories.md)
