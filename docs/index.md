---
title: avala
ms.topic: overview
---

# avala

Avala is an open-source desktop harness for coding agents. Each agent works unattended in its own git worktree, under the rules its repository commits, and every result comes with evidence and every job with a cost. It is written in C# on .NET 10 with Avalonia, runs on Windows and Linux, and builds from source on macOS. Claude Code is the harness it officially supports.

## What it does

- **Isolates every job.** A job runs in its own git worktree, with its own processes, ports and temporary folder, so several agents work on one repository at once without touching your checkout. A job started without a repository gets a scratch repository of its own.
- **Answers for you, by policy.** Every permission request and question an agent asks goes through the repository's `.avala/permissions.json`, built-in guards and the job's autonomy, Supervised or Autonomous, and every decision is audited with its reason.
- **Judges work by evidence.** A job is done only when the checks its repository declares in `.avala/checks.json` pass in its worktree; the review shows that verdict first, then the diff.
- **Accounts for every token.** Usage, cost and limits are kept per provider, account, connection and job, budgets stop a job before it overspends, and a job that reaches a usage limit can wait for the reset or be handed to another connection.
- **Works unattended.** Autopilot works through a backlog, Triggers start jobs on a schedule, a webhook or a forge event, Delegation lets an agent hand work to child jobs, and Best of N runs one request as several candidates and keeps the best by its evidence.
- **Stays in touch.** Notifications reach your phone through ntfy or a webhook when a job needs you, and a Claude Code job can be continued from the phone.

## Who it is for

| You are | Start with |
| --- | --- |
| Using Avala to run coding agents | The [quickstart](get-started/quickstart.md), then the [guide](guide/README.md), from Getting started on |
| Contributing to Avala | The [quickstart](get-started/quickstart.md#build-from-source), [Contributing](../CONTRIBUTING.md), then the [architecture](concepts/architecture.md) and its [detailed page](architecture.md) |
| Releasing Avala | [Releasing](release.md) and the [release notes](releases/) |
| An agent working on Avala | [AGENTS.md](../AGENTS.md), then the same as contributing |

## Where to start

- [Quickstart](get-started/quickstart.md): install or build Avala and run your first job.
- [Architecture](concepts/architecture.md): its context and containers, as built.
- [Configuration](reference/configuration.md): every setting Avala reads, where and with which default.
- [Architecture decisions](architecture/decisions/index.md): the decisions that shaped documented features.
- [What's new](whats-new.md): what each documented feature delivered.
