---
title: Configuration
ms.topic: reference
---

# Configuration

Every setting Avala reads, where and with which default. Avala reads three kinds of configuration:

- **Environment variables** in Avala's own environment.
- **Machine settings**: small JSON files in the data folder, for this computer. Most are written by Settings; you can also edit them.
- **Repository rules**: JSON files in a repository's `.avala` folder, committed with the code. A job reads them from the commit it started from, so an agent's edit to them never applies to its own job.

A missing file means its defaults. A file that is malformed, too large or holds an unknown or duplicate key is rejected, and its defaults apply instead, unless its row says otherwise. Settings, Repositories shows the rules of every repository a job ran in, one tab per kind of rule, as read from each job's base commit.

## Environment variables

| Variable | Read by | Effect | Default |
| --- | --- | --- | --- |
| `AVALA_DATA_PATH` | Host | The data folder. | `Avala` in the local application data folder: `~/.local/share/Avala` on Linux, `%LOCALAPPDATA%\Avala` on Windows. With `--smoke`, a new temporary folder. |
| `AVALA_PLUGINS_PATH` | Host | The folder plugins are loaded from. | `plugins` beside the executable, or else the nearest `artifacts/plugins` above it, as in a build from source. |
| `AVALA_DEVELOPER` | Simulator, ForgeSimulator | `1` or `true` loads the simulated harness and the simulated forge. Only a build from source holds them. | Off. |
| `AVALA_X11` | Host | `1` draws through X11 on a Linux Wayland session. | Wayland when `WAYLAND_DISPLAY` is set. |
| `WEBKIT_DISABLE_DMABUF_RENDERER` | Preview | Left as you set it; on Linux, Avala sets it to `1` for the Preview pane's web view when it is unset. | `1` on Linux. |
| `DO_NOT_TRACK` | Runtime | Any value but `0`, `false`, `off` or `no` turns usage statistics off, whatever the setting says. | Unset. |
| `AVALA_TELEMETRY` | Runtime | `0`, `false`, `off` or `no` turns usage statistics off. | Unset. |
| `FLATPAK_ID` | Host | The application id the window reports to the desktop. | `avala`. |
| `CLAUDE_CONFIG_DIR` | ClaudeCode | Another Claude Code sign-in folder, found as a connection. | Unset. |
| `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN`, `CLAUDE_CODE_OAUTH_TOKEN`, `CLAUDE_CODE_USE_BEDROCK`, `CLAUDE_CODE_USE_VERTEX` | ClaudeCode | Found as Claude Code credentials. | Unset. |
| `GH_TOKEN`, `GITHUB_TOKEN`, `GITEA_TOKEN` | GitHub, Gitea | Found as forge accounts. Avala checks only whether they are set. | Unset. |
| Variables a file names | Agents, Forges, Notifications, Triggers | The credential, token or secret named by `reference`, `tokenEnv` or `secretEnv`. Avala stores the name, never the value. | None. |

Avala also sets variables for the processes it starts:

| Variable | Set for | Value |
| --- | --- | --- |
| `AVALA_PORT`, `AVALA_PORTS` | Every process of a job | The first of the job's leased ports, and the whole lease as `first-last`. |
| `PORT`, `HOST`, `BROWSER` | A script started by Run in the Preview pane | The leased port, `127.0.0.1` and `none`. |
| `TMPDIR`, `TEMP`, `TMP` | Every process of a job | A scratch folder of its worktree under the data folder's `temp` folder, removed when the job ends. |
| `AVALA_PROCESS_TREE` | Every process Avala starts | The tree it belongs to, so it can be ended with its job. |
| `CLAUDE_CODE_AUTO_COMPACT_WINDOW` | Claude Code | The connection's `compactAt`, 250000 by default. |
| `MCP_TOOL_TIMEOUT` | Claude Code | No timeout for Avala's own tools. |
| `CLAUDE_CODE_ENABLE_TODO_TOOLS` | Claude Code | `1` unless the connection turns `planTools` off or you already set it. |

## Command-line options

| Option | Effect |
| --- | --- |
| `--smoke` | Composes the application, shows its window and exits with 0 when at least one page was loaded, or 2 otherwise. Uses a temporary data folder unless `AVALA_DATA_PATH` is set. |

## Machine settings

Files in the data folder.

### connections.json

Your own harness connections, beside the ones found on their own: `~/.claude`, every `~/.claude-*` folder (`~/.claude-work` becomes `claude-work`) and the variables above.

```json
{
  "default": "auto",
  "connections": [
    { "name": "claude-team", "provider": "claude-code", "credential": { "source": "apiKey", "reference": "TEAM_ANTHROPIC_KEY" }, "settings": { "compactAt": "400000" } }
  ]
}
```

| Key | Meaning | Default |
| --- | --- | --- |
| `default` | The connection for jobs that name none, or `"auto"` for the one with the most capacity left. | None. |
| `connections[].credential.source` | `login`, with a sign-in folder as `reference`, or `apiKey`, with an environment variable's name. | None. |
| `settings.compactAt` | The automatic compaction window of a Claude Code session, in tokens. | `250000`. |
| `settings.autoCompact` | Claude Code's automatic compaction. | `true`. |
| `settings.idleCompaction` | Claude Code's compaction of an idle conversation. | `true`. |
| `settings.userConfiguration` | Whether the session reads the login's user, project and local Claude Code settings. | `true`, except for an API key without a sign-in folder. |
| `settings.userHooks` | Whether the login's own Claude Code hooks run. | `false`. |
| `settings.planTools` | Whether Claude Code's task tools are on, which a documented job's plan comes from. | `true`. |
| `settings.transcripts` | A folder where the raw session protocol is written. | None. |

### Other machine settings

| File | Keys and defaults |
| --- | --- |
| `appearance.json` | `theme`: `system` (default), `light` or `dark`. `reduceMotion`: `system` (default), `on` or `off`. `readingWidth`: `comfortable` (default) or `wide`. `send`: `enter` (default) or `commandEnter`. `hidePersonalDetails`: `true` (default). |
| `layout.json` | `sidebar`: `shown` (default) or `collapsed`. `sidebarWidth`: 200 to 400, 260 by default. `previewShare`: 0.25 to 0.7, 0.45 by default. `collapsedGroups`: the folded sidebar groups. |
| `onboarding.json` | `onboarded`: `true` once the welcome tour is finished or skipped. |
| `workspaces.json` | `projectsFolder`: where clones and new repositories go. Until chosen, the first that exists of `~/Projects`, `~/projects`, `~/code`, `~/src`, `~/dev` and `~/source/repos`. |
| `readable.json` | `folders`: absolute or `~/` paths every job may read outside its worktree. Without the file, the projects folder; `[]` makes nothing readable; a malformed file makes nothing readable. |
| `forges.json` | `pollSeconds`: 15 to 3600, 60 by default. `forges[]`: `name`, `forge` (`github`, `gitea` or `forgejo`), `url` and `credential`, whose `source` is `cli` or `env` with a variable's name as `reference`. |
| `budgets.json` | `runningJobs`: how many jobs run at once, unlimited by default. `spendPerDay`: an amount per currency. `connections`: the same per connection. |
| `limits.json` | The `limits` section below, for every repository that declares none; Settings, Connections, Usage limits edits it. |
| `autopilot.json` | The loop limits of the `autopilot` section below, for every repository; a repository can only tighten them. |
| `supervision.json` | `silenceSeconds`: how long a silent job runs before it is held, above 0 and at most a day; 900 by default. |
| `verification.json` | `discoverChecks`: `false` stops Avala from finding the checks of a repository that declares none; on by default. |
| `resources.json` | `sampleSeconds`: 5 by default. `diskSeconds`: 60 by default. `orphans`: `kill` (default) or `report`, for processes a session leaves behind. `ports`: `first` 24000, `last` 24999 and `perWorktree` 10 by default. `worktrees`, in hours, `0` at once and `null` to keep or never delete: `freeArchivedHours` 0, `freeDeliveredHours` 0, `freePullRequestHours` 0, `freeUndeliveredHours` 24, `freeDiscardedHours` 0, `freeFailedHours` 0, `deleteDiscardedHours` 168, `deleteFailedHours` 720 and `freeIdleHours` 72 by default, `spaceBudgetGigabytes` 20 and `diskFloorPercent` 10 by default, `null` for no limit, and `reconcile`: `clean` (default) or `report`. Settings, Worktrees writes these; the earlier `keepDiscardedHours`, `keepFailedHours` and `keepApprovedHours` still read as `deleteDiscardedHours`, `deleteFailedHours` and `freeUndeliveredHours`. See [Worktrees and disk space](../guide/worktrees-and-disk-space.md). |
| `notifications.json` | `enabled`. `channel`: `ntfy` (default) or `webhook`. `ntfy`: `server` (`https://ntfy.sh` by default), `topic` (a private random topic by default) and `tokenEnv`. `webhook`: `url` and `secretEnv`. `events`: `decision`, `held`, `budget`, `failed`, `loopEnded` on and `review` off by default. `detail`: `minimal` (default) or `detailed`. `quietHours`: `from`, `to` and `days`, every day by default. |
| `triggers.json` | `repositories` and `triggers`; see [Autopilot and triggers](../guide/autopilot-and-triggers.md#settings-and-files). A trigger's `autonomy` is `supervised`, `attempts` 3 (1 to 10) and `concurrency` 1 (1 to 16) by default. |
| `claude-code.json` | `startJobsOnPhone`: `false` by default. |
| `fleet.json` | Written by Settings, Fleet: `server`, the fleet server's address; `instance` and `instanceName`, this instance on it; `enabled`, `false` while sending is paused; `keyRefused`, `true` once the server refused the key, until a new key or a successful test. Never the key, which is in the system keychain. Without the file, no fleet. |
| `telemetry.json` | `enabled` and the random install ID. Only a packaged release can send. |
| `updates.json` | `checkOnStartup`: `true` by default. A file that cannot be read, or holds anything else, turns the startup check off. |
| `recording.json` | Development only. `enabled`: `false` by default. `redact`: strings removed from recorded sessions. |

The data folder also holds Avala's state, which is not configuration: each module's SQLite database, `drafts.json`, `process-trees.json`, the `logs`, `attachments`, `scratch` and `temp` folders. Logs are `avala-YYYYMMDD-NNN.log`, each up to 4 MB, the newest ten kept.

## Repository rules

Files in a repository's `.avala` folder.

### jobs.json

One JSON object; each module reads its own section, and Jobs rejects a field the format does not define.

| Key | Meaning | Default |
| --- | --- | --- |
| `connection` | A connection's name, or `"auto"`. | The machine's `default`. |
| `model`, `effort` | The model and effort jobs run with. | The harness's own. |
| `approval` | `keep` leaves the job's branch, `merge` squashes it into the base branch with an `Avala-Job:` trailer, `pull-request` opens one. | `keep`. |
| `autopilot.approve` | `never` or `cleanEvidence`. | `never`. |
| `autopilot.followUps` | `refuse` or `accept`. | `refuse`. |
| `autopilot.iterations`, `failuresInARow`, `sameFailure`, `nothingChanged`, `pauseAtLimit` | The loop's limits. | 50, 3, 2, 2 and 0.9. |
| `delegation.connections` | Connections children may run on, up to 16. | None. |
| `delegation.routing` | `capacity` (also `leastUsed`) or `roundRobin`. | `capacity`. |
| `delegation.maxDepth` | 1 to 8. | 1. |
| `delegation.maxChildren` | 1 to 16. | 2. |
| `delegation.role`, `delegation.roles` | The default role, and per role (`worker`, `reviewer`, `research`) its `model`, `effort` and `compactAt` (20000 to 1000000). | None. |
| `delegation.escalation`, `delegation.parentWindowSeconds` | Retired: a child's question always goes to its parent and its permission to you. An older section that still sets them stays valid, and they are ignored. | None. |
| `limits.onLimit` | `hold`, `handoff-same-harness` or `handoff-any-harness`. | `hold`. |
| `limits.threshold` | The share of a usage window at which a job prepares to hold or move. | 0.9. |
| `limits.connections` | The connections a job may move to. | None. |
| `limits.resumeAtReset` | `ask` or `auto`. | `ask`. |
| `limits.hardCap` | The share of a usage window a job about to relay is stopped at, from `threshold` to 1. | 0.98, or `threshold` when higher. |
| `limits.strategy` | `most-headroom`, `in-order`, `soonest-reset` or `round-robin`. | `most-headroom`. |
| `limits.windDown` | Asks the agent to wind down before a handoff. | `true`. |
| `limits.compactOnResume` | Compacts the conversation before a job continues on the connection it waited for. | `true`. |
| `pullRequest.forge` | A forge connection in `forges.json`; required. | None. |
| `pullRequest.remote` | The remote pushed to. | `origin`. |
| `pullRequest.onPullRequest` | `watch-only`, `wake-on-ci` or `wake-on-ci-and-reviews`. | `watch-only`. |
| `pullRequest.maxWakeUps` | 0 to 20. | 3. |
| `pullRequest.redeliver` | `automatic` or `review`. | `automatic`. |
| `bestOf.candidates` | How many candidates a job starts with. | 1. |
| `bestOf.max` | The most a contest may have, 1 to 8. | 4. |
| `docs.enabled` | Feature documentation. | Off when the section is missing. |
| `docs.folder` | Where features are documented, relative to the repository. | `docs/features`. |

### Other repository rules

| File | Keys and defaults |
| --- | --- |
| `permissions.json` | At most 64 KB. `autonomy`: `supervised` (default) or `autonomous`. `formAnswers`: `recommended` (default) or `bestJudgment`. `rules[]`: `answer` (`allow`, `deny` or `ask`), and optionally `kind`, `target`, `within` (`anywhere` by default, or `workspace`) and `name`. A rejected file leaves the built-in rules, at Supervised. |
| `checks.json` | `checks[]`: `command` (required), `arguments`, `name` and `timeoutSeconds`, up to 86400 and 600 by default. Without it, Avala finds the checks while the repository's first job runs and ships them with that job; without any, verification says so. |
| `budget.json` | `costPerJob`, `tokensPerJob`, `memoryPerJobMegabytes`, `holdAtLimit` (above 0, at most 1), `carvePerChild` (0 to 1, 0.5 by default) and `connections`. No cap by default. |
| `backlog.json` | `tasks[]` and `recurring[]`, each with an `id` and an `instruction`, a recurring task with `everyMinutes`. |
| `triggers.json` | `triggers` only; no webhook or forge triggers. |

## See also

- The guide's page for each feature, under its **Settings and files**: [Getting started](../guide/getting-started.md#settings-and-files), [Permissions and autonomy](../guide/permissions-and-autonomy.md#settings-and-files), [Budgets and usage limits](../guide/budgets-and-usage-limits.md#settings-and-files) and the others in the [guide](../guide/README.md).
- [Where files live](../README.md#where-files-live).
