# Autopilot and triggers

Start work without being there: triggers start jobs on a schedule, from a signed webhook or from a forge event, and a loop works through a backlog unattended.

## What it does

- **Triggers** start ordinary jobs, or a loop, when they fire:
  - on a **schedule**, every so many minutes or at a time on chosen days, in this computer's time zone;
  - on a **webhook**, a signed request received on a loopback port only;
  - on a **forge event** from GitHub, Gitea or Forgejo: an issue opened, an issue labeled, a command in a comment, failing checks on a branch or a review requested, by an author on your list.
- **Forge events are polled**, so a laptop needs no public endpoint. The first poll only records where the forge stands, and each event fires at most once.
- **A loop** (Autopilot) works through the repository's backlog one job at a time, within limits on iterations and failures. It approves a job on its own only when the repository allows it and the evidence is clean: checks passed, nothing denied or declined, no assumption, no hold and no edit under `.avala`.
- **Triggered work never exceeds its repository's autonomy**: a trigger asking for Autonomous in a supervised repository runs Supervised.

## How to use it

1. **Declare triggers** in `triggers.json` in the data folder, listing your repositories, or in a listed repository's `.avala/triggers.json`.
2. **Open the Triggers page** and choose **Reload**. Each trigger has a switch to enable it, **Run now**, or **Check now** for a forge trigger, which polls at once.
3. **For a loop**, give the trigger `"target": "loop"` and describe the work in `.avala/backlog.json`. Running loops show under **Loops** with **Pause**, **Resume** and **Stop**.
4. **Let an agent propose more work**: with `followUps` set to `accept`, the agent's `propose_follow_up` tool adds a task to the loop.

## Settings and files

**`triggers.json`**, in the data folder:

```json
{
  "repositories": ["/home/me/app"],
  "triggers": [
    { "id": "nightly", "repository": "/home/me/app", "schedule": { "at": "02:00", "days": ["mon", "tue", "wed", "thu", "fri"] }, "instruction": "Work through the backlog", "target": "loop" },
    { "id": "fix-label", "repository": "/home/me/app", "forge": { "connection": "github", "event": "issue-labeled", "label": "avala", "authors": ["me"] }, "instruction": "Fix {{payload.title}}" }
  ]
}
```

- Each trigger has an `id`, an `instruction` and exactly one of `schedule`, `webhook` or `forge`. Optional: `enabled`, `target` (`"job"` or `"loop"`), `connection`, `autonomy` (`"supervised"` by default), `attempts` (1 to 10, 3 by default), `concurrency` (1 to 16, 1 by default) and, for a schedule, `catchUp` (`"none"` or `"once"`).
- `schedule`: `{ "everyMinutes": 60 }`, or `{ "at": "HH:mm", "days": [...] }`.
- `webhook`: `{ "secretEnv": "HOOK_SECRET", "ratePerHour": 60 }`. Avala listens on `http://localhost:<port>/hooks/<id>` for a POST signed with HMAC-SHA256 in `X-Avala-Signature: sha256=<hex>` over `<timestamp>.<nonce>.<body>`, with `X-Avala-Timestamp` and `X-Avala-Nonce`.
- `forge`: `connection` (a name in `forges.json`), `event` (`issue-opened`, `issue-labeled` with `label`, `comment-command` with `command`, `checks-failed` with `branch`, or `review-requested`), `authors`, and `acknowledge` to post a comment when a job starts.
- An instruction may use `{{trigger.id}}`, `{{trigger.repository}}`, `{{run.at}}`, `{{payload}}` and a field such as `{{payload.title}}`.
- A repository's `.avala/triggers.json` holds `triggers` only, and may not declare webhook or forge triggers.

**`.avala/backlog.json`**:

```json
{ "tasks": [ { "id": "fix-lint", "instruction": "Fix the lint warnings" } ], "recurring": [ { "id": "deps", "instruction": "Update the dependencies", "everyMinutes": 1440 } ] }
```

**`.avala/jobs.json`**, `autopilot` section: `approve` (`"never"` by default, or `"cleanEvidence"`), `followUps` (`"refuse"` by default, or `"accept"`), and the loop's limits: `iterations` (50), `failuresInARow` (3), `sameFailure` (2), `nothingChanged` (2) and `pauseAtLimit` (0.9 of a usage window).

**`autopilot.json`**, in the data folder, holds the same limits for the machine; a repository can only tighten them.

## Good to know

- There is no cron syntax: a schedule is an interval or a time of day.
- A loop starts only from a trigger; the app has no button that starts one.
- Avala never opens a public port. To receive webhooks from the internet, run a tunnel of your own to the port, such as Tailscale Funnel.
- A forge trigger's instruction is filled from its event, so it cannot be run by hand: Check now polls instead.
- Pull requests from forks never fire a trigger.

## See also

- [Pull requests and CI](pull-requests-and-ci.md)
- [Permissions and autonomy](permissions-and-autonomy.md)
- [Notifications](notifications.md)
