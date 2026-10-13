<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/brand/avala-lockup-dark.png">
  <img src="docs/assets/brand/avala-lockup-light.png" alt="Avala" width="220">
</picture>

### Agents you can trust without watching.

An open-source desktop harness for coding agents: each agent works unattended in its own git worktree,<br>
under your rules, with evidence for every result and a cost for every job.

[![CI](https://github.com/rick-dev-creator/avala/actions/workflows/ci.yml/badge.svg)](https://github.com/rick-dev-creator/avala/actions/workflows/ci.yml)
[![Debt grade](https://raw.githubusercontent.com/rick-dev-creator/avala/refs/heads/badges/debt.svg)](docs/architecture.md#technical-debt-grade)
[![Coverage](https://raw.githubusercontent.com/rick-dev-creator/avala/refs/heads/badges/coverage.svg)](docs/architecture.md#quality-metrics)
[![Maintainability](https://raw.githubusercontent.com/rick-dev-creator/avala/refs/heads/badges/maintainability.svg)](docs/architecture.md#technical-debt-grade)
[![Highest complexity](https://raw.githubusercontent.com/rick-dev-creator/avala/refs/heads/badges/complexity.svg)](docs/architecture.md#technical-debt-grade)
[![C# lines](https://raw.githubusercontent.com/rick-dev-creator/avala/refs/heads/badges/lines.svg)](docs/architecture.md#quality-metrics)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

[Install](#install) · [Quick start](#quick-start) · [Features](#features) · [Privacy](#privacy) · [Build from source](#build-from-source) · [Docs](#docs)

<br>

<picture>
  <source media="(prefers-color-scheme: dark)" type="image/webp" srcset="docs/assets/readme/hero-dark.webp">
  <source media="(prefers-color-scheme: light)" type="image/webp" srcset="docs/assets/readme/hero-light.webp">
  <img src="docs/assets/readme/hero-light.gif" alt="A request typed on Avala's welcome screen starts a job; the agent's reply streams in and draws an animated bar chart of the jobs finished this week in the conversation" width="900">
</picture>

</div>

## Why Avala

- **Evidence, not the agent's word.** A job is done only when the checks your repository declares pass in its worktree. The review shows that verdict first, then the diff.
- **Policies, not prompts.** Every permission, question and plan approval is answered in place or by rules the agent cannot edit, at the autonomy you allow, and every decision is audited.
- **Nothing runs away.** Stalled sessions are caught, spending stops at your caps, and leftover processes, ports and worktrees are reclaimed.
- **Claude Code, every account.** Claude Code is the harness Avala supports, with several connections at once. Harnesses plug in behind one contract, so more can follow.

## Features

<table>
<tr>
<td width="50%" valign="top">

**Start from one sentence.** Describe a task on the welcome screen and pick the connection, model and effort from the chip in the composer, or open a folder, clone a repository or start a new one.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/readme/welcome-dark.png">
  <img src="docs/assets/readme/welcome-light.png" alt="The welcome screen with a task typed in the composer and the menu of connections open, Auto recommended">
</picture>

</td>
<td width="50%" valign="top">

**A conversation that feels native.** Replies render Markdown with code colored by its language, and canvases are drawn as Markdown, SVG (animations included) and Mermaid.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/readme/conversation-dark.png">
  <img src="docs/assets/readme/conversation-light.png" alt="An agent's reply with a heading, a list, a highlighted Go code block and a table">
</picture>

</td>
</tr>
<tr>
<td width="50%" valign="top">

**Permissions in place.** Allow a command once, for that program for the rest of the job, or for everything in the job, or let your repository's rules answer.

<picture>
  <source media="(prefers-color-scheme: dark)" type="image/webp" srcset="docs/assets/readme/permission-dark.webp">
  <source media="(prefers-color-scheme: light)" type="image/webp" srcset="docs/assets/readme/permission-light.webp">
  <img src="docs/assets/readme/permission-light.gif" alt="A permission card for dotnet ef database update: Always dotnet in this job is chosen, the command is allowed and the job finishes ready for review">
</picture>

</td>
<td width="50%" valign="top">

**Review from the verdict.** The review opens on the evidence, lists only what deserves a look, and shows the diff before you approve, send back or discard.

<picture>
  <source media="(prefers-color-scheme: dark)" type="image/webp" srcset="docs/assets/readme/review-dark.webp">
  <source media="(prefers-color-scheme: light)" type="image/webp" srcset="docs/assets/readme/review-light.webp">
  <img src="docs/assets/readme/review-light.gif" alt="The review sheet: verified on attempt 2 of 2, the failed first attempt worth a look, the one-line diff, then approved">
</picture>

</td>
</tr>
<tr>
<td width="50%" valign="top">

**Best of N.** Run one request as several candidates, each in its own worktree; the best is kept by its evidence and the others are discarded once you approve it.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/readme/contest-dark.png">
  <img src="docs/assets/readme/contest-light.png" alt="Three candidates of one request, the winner's review open and the inspector ranking them by evidence, diff and cost">
</picture>

</td>
<td width="50%" valign="top">

**A preview beside the conversation.** When a job starts a server, its page opens in a pane by itself, behind a loopback gateway that keeps other origins out.

<picture>
  <source media="(prefers-color-scheme: dark)" type="image/webp" srcset="docs/assets/readme/preview-dark.webp">
  <source media="(prefers-color-scheme: light)" type="image/webp" srcset="docs/assets/readme/preview-light.webp">
  <img src="docs/assets/readme/preview-light.gif" alt="The agent builds and runs a service, and the preview pane opens by itself with the running orders page">
</picture>

</td>
</tr>
<tr>
<td width="50%" valign="top">

**Triggers and forges.** Start jobs on a schedule, from a signed webhook, or from events on GitHub, Gitea or Forgejo, and open pull requests whose checks are watched.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/readme/triggers-dark.png">
  <img src="docs/assets/readme/triggers-light.png" alt="The Triggers page with a forge trigger, its next poll and its events with their verdicts">
</picture>

</td>
<td width="50%" valign="top">

**Notifications on your phone.** When a job needs you while you are away, Avala tells you through ntfy or a webhook of your own, and shows exactly what is sent.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/readme/notifications-dark.png">
  <img src="docs/assets/readme/notifications-light.png" alt="The notification settings: an ntfy topic, the events that send, quiet hours and the exact request sent for a decision">
</picture>

</td>
</tr>
</table>

Also: budgets per job and per connection with a daily ceiling for the machine, usage windows that hold or hand a job off to another account, sub-agents that ask their parent first, an autopilot that works through a backlog, and jobs that survive a restart. The [guide](docs/guide/README.md) has a page for each. Every picture here is captured from the real application by its screen tests, which drive it headless on the development simulator; [Contributing](CONTRIBUTING.md) explains how.

## Install

> **Avala 0.1.0-beta.1 is out.** It is a prerelease: [read the release notes](https://github.com/rick-dev-creator/avala/releases/tag/v0.1.0-beta.1) before you start. The builds are not signed yet, so Windows SmartScreen warns about an unknown publisher; choose More info, Run anyway.

| Platform | How |
| --- | --- |
| Windows x64 | Run [`avala-0.1.0-beta.1-win-x64-setup.exe`](https://github.com/rick-dev-creator/avala/releases/download/v0.1.0-beta.1/avala-0.1.0-beta.1-win-x64-setup.exe), or unzip [the portable zip](https://github.com/rick-dev-creator/avala/releases/download/v0.1.0-beta.1/avala-0.1.0-beta.1-win-x64-portable.zip) anywhere. Both update themselves in place. |
| Linux x64 | Run [`avala-0.1.0-beta.1-linux-x64.AppImage`](https://github.com/rick-dev-creator/avala/releases/download/v0.1.0-beta.1/avala-0.1.0-beta.1-linux-x64.AppImage) (`chmod +x` it first), which updates itself in place, or unpack [the tar.gz](https://github.com/rick-dev-creator/avala/releases/download/v0.1.0-beta.1/avala-0.1.0-beta.1-linux-x64.tar.gz). |
| Arch Linux | `avala-bin` on the AUR is coming soon; until then use the AppImage or the tar.gz. |
| Flatpak | `io.github.rick_dev_creator.Avala`, built from `packaging/flatpak` for now; Flathub comes later. |
| macOS | Later. Avala builds and runs from source on a Mac today. |

The installers and archives are attached to each [release](https://github.com/rick-dev-creator/avala/releases); details for each package are in [Releasing](docs/release.md).

## Quick start

1. **Install Claude Code and sign in**, so `claude` is on your `PATH`. Avala finds each login on its own.
2. **Open Avala.** A four-step welcome tour shows the agents it found, your projects folder and source-control sign-ins, and your privacy choices.
3. **Start a chat or pick a repository.** Type a task on the welcome screen to run it in a scratch repository, or open a folder, clone or create one, and the job runs in its own worktree.

**Supported harness:** Claude Code. It is the only harness Avala officially supports.

## Privacy

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/readme/privacy-dark.png">
  <img align="right" width="380" src="docs/assets/readme/privacy-light.png" alt="The privacy step of the welcome tour: hide personal details, and share anonymous usage statistics">
</picture>

A released build of Avala sends anonymous usage statistics to [Aptabase](https://aptabase.com) so we can count installs and daily active installs. Nothing else leaves your computer on Avala's behalf.

- **What is sent.** An `installed` event once ever, and an `active` event at most once per local day, each with Avala's version, your operating system's name, version and architecture, and a random install ID made on first run. The install ID is never derived from your hardware, user name or paths.
- **What is never sent.** Repositories, jobs, prompts, paths, harnesses, connections, accounts, costs, the features you use or your locale.
- **Turning it off.** Choose Turn off on the notice shown on first run, or clear Send usage statistics in Settings, About; or set `DO_NOT_TRACK=1` or `AVALA_TELEMETRY=0` in Avala's environment. Settings shows the exact event that is sent and lets you reset the install ID.
- **Builds from source** send nothing: only a packaged release holds the key that makes sending possible.

<br clear="right">

## Build from source

Requires the .NET 10 SDK and git 2.40 or later.

```
dotnet build Avala.slnx
dotnet test --solution Avala.slnx
dotnet run --project src/Avala.Host
```

A build from source also holds the development simulator, which the released packages leave out; [Contributing](CONTRIBUTING.md) shows how to run Avala on it.

## Docs

- [Guide](docs/guide/README.md): one page per feature, starting with [Getting started](docs/guide/getting-started.md).
- [Documentation index](docs/README.md): every document, by who it is for.
- [Architecture](docs/architecture.md): the big picture and the rules every change must pass.
- [Releasing](docs/release.md): packages, usage statistics, signing and the update check.
- [Contributing](CONTRIBUTING.md): how to build, test and send a change, and how the pictures above are made.
- [AGENTS.md](AGENTS.md): what an agent working on Avala reads first.

## License

[MIT](LICENSE)
