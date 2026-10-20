---
title: Architecture
ms.topic: concept-article
---

# Architecture

The C4 context and containers of Avala, as built. Avala is one desktop process made of plugins: the host knows none of its modules, which talk through integration events on an in-process bus, contracts and extension points. The [detailed architecture](../architecture.md) has the module map, the rules every change must pass, persistence and testing.

## Context

A person runs Avala on their own computer. Avala runs coding agents through their harness, works in the person's git repositories, and talks to a few outside services, each optional except the harness.

```mermaid
C4Context
    title System context of Avala

    Person(developer, "Developer", "Starts jobs, answers what the policy leaves to them, reviews and approves the work")
    Person_Ext(phone, "Developer's phone", "Receives notices and continues a job remotely")

    System(avala, "Avala", "Desktop harness that runs coding agents unattended, each in its own git worktree, under the repository's rules, with evidence and a cost for every job")

    System_Ext(claude, "Claude Code", "The claude CLI and the Anthropic accounts it signs in with; the supported harness")
    System_Ext(git, "git", "Repositories, worktrees, clones and pushes, with the person's own credentials")
    System_Ext(forges, "Forges", "GitHub, Gitea and Forgejo: repositories, pull requests, checks, issues and comments")
    System_Ext(ntfy, "ntfy or a webhook", "Delivers notices to the phone")
    System_Ext(releases, "GitHub Releases", "Published releases the update check reads and Velopack downloads")
    System_Ext(aptabase, "Aptabase", "Anonymous install and daily active counts, packaged releases only")
    System_Ext(senders, "Webhook senders", "Sign requests that start triggers, through a tunnel of the person's own")

    Rel(developer, avala, "Starts, steers and reviews jobs")
    Rel(avala, claude, "Runs sessions", "streaming JSON over stdio, MCP")
    Rel(avala, git, "Prepares worktrees, commits, merges, clones, publishes", "process")
    Rel(avala, forges, "Opens pull requests, polls events and checks", "HTTPS API, gh, tea")
    Rel(avala, ntfy, "Sends notices", "HTTPS")
    Rel(ntfy, phone, "Pushes notices")
    Rel(phone, claude, "Continues a session", "Remote Control")
    Rel(avala, releases, "Checks for updates", "HTTPS")
    Rel(avala, aptabase, "Sends installed and active events", "HTTPS")
    Rel(senders, avala, "Fires triggers", "signed HTTP on loopback")
```

- **Claude Code** is the only harness Avala officially supports. It runs as the `claude` CLI in its streaming JSON mode; Avala serves its permission prompt and its own harness tools, such as the canvas, delegation and follow-ups, from an MCP server inside the session.
- **git** is always run through Avala's process runner, with terminal prompts off. Avala never holds a token for git.
- **Forges** are reached through forge plugins with the person's command-line logins or tokens named by environment variable. Forge events are polled, so a laptop needs no public endpoint.
- **Avala never opens a public port.** The webhook listener and every preview bind to loopback.

## Containers

Avala ships as one process. Its containers are the host, the plugins it loads, the data it keeps and the processes it starts for each job.

```mermaid
C4Container
    title Containers of Avala

    Person(developer, "Developer", "Uses the desktop window")

    System_Ext(claude, "Claude Code", "The claude CLI")
    System_Ext(git, "git", "The git executable")
    System_Ext(forges, "Forges", "GitHub, Gitea, Forgejo")
    System_Ext(outside, "Outside services", "ntfy or a webhook, GitHub Releases, Aptabase")

    System_Boundary(avala, "Avala") {
        Container(host, "Host", "C#, .NET 10, Avalonia", "Avala.Host: the application, composition root and plugin loader; it references no module")
        Container(shell, "Shell and Workbench", "C#, Avalonia, view models", "The main window, its regions and every screen, drawn from other modules' contracts")
        Container(runtime, "Runtime", "C#, System.Threading.Channels", "The event bus, the process runner, logs, appearance, the update check and usage statistics")
        Container(trust, "Trust layer plugins", "C# modules", "Verification, Permissions, Supervision and Budgets, around Resources and Observability")
        Container(work, "Work plugins", "C# modules", "Agents, Jobs, Workspaces, Projects, Transcripts, Canvas, Preview, Documentation, Delegation, Autopilot, Triggers, Contests, Handoffs, Notifications")
        Container(providers, "Provider and forge plugins", "C# modules", "ClaudeCode, GitHub and Gitea; in developer mode the Simulator, ForgeSimulator and Recording")
        ContainerDb(databases, "Module databases", "SQLite, EF Core", "One database per module in the data folder; no module reads another's")
        ContainerDb(settings, "Machine settings", "JSON files", "Small JSON files in the data folder, such as connections.json and budgets.json")
        ContainerDb(rules, "Repository rules", "JSON in .avala", "permissions.json, checks.json, budget.json, jobs.json, backlog.json, triggers.json, read from a job's base commit")
        Container(worktrees, "Job worktrees", "git worktrees", "One per job, with its own processes, leased ports and temporary folder")
        Container(preview, "Preview servers", "Processes the agent starts", "A job's page or app, served on 127.0.0.1 at AVALA_PORT")
        Container(listener, "Webhook listener", "HTTP on loopback", "Receives signed trigger webhooks")
    }

    Rel(developer, shell, "Uses")
    Rel(host, runtime, "Starts")
    Rel(host, shell, "Composes")
    Rel(host, work, "Loads as plugins")
    Rel(host, trust, "Loads as plugins")
    Rel(host, providers, "Loads as plugins")
    Rel(shell, work, "Reads contracts, sends commands")
    Rel(work, runtime, "Publishes and handles events")
    Rel(trust, runtime, "Publishes and handles events")
    Rel(work, trust, "Asks through extension points", "ICompletionGate, IApprovalStrategy, IJobAdmission")
    Rel(work, providers, "Asks through extension points", "IAgentProvider, forge contracts")
    Rel(providers, claude, "Runs sessions", "stdio, MCP")
    Rel(providers, forges, "Calls APIs and CLIs", "HTTPS")
    Rel(work, git, "Runs", "process runner")
    Rel(git, worktrees, "Prepares")
    Rel(claude, worktrees, "Edits and runs commands in")
    Rel(worktrees, preview, "Starts")
    Rel(shell, preview, "Shows beside the conversation")
    Rel(work, databases, "Reads and writes")
    Rel(trust, databases, "Reads and writes")
    Rel(work, settings, "Reads")
    Rel(trust, rules, "Reads from the base commit")
    Rel(work, rules, "Reads from the base commit")
    Rel(runtime, outside, "Checks updates, sends statistics")
    Rel(work, outside, "Sends notices")
    Rel(listener, work, "Fires triggers")
```

| Container | What it is |
| --- | --- |
| **Host** | `src/Avala.Host`: an Avalonia application that scans its `plugins` folder, instantiates every `IPlugin` it finds and lets each register its services, handlers and views. All plugins share one load context, so a contract has one type identity for every module. |
| **Shell and Workbench** | `src/Avala.Shell` and the Workbench module: the main window's regions and every screen, view model first, built only on other modules' contracts. |
| **Runtime** | `src/Avala.Runtime`: the in-process event bus, where each handler has its own mailbox and sees its events in order; the process runner, which every process goes through and which ends every process tree when Avala stops; logs, appearance, the update check and usage statistics. |
| **Trust layer plugins** | Verification runs the repository's checks after every finished turn; Permissions answers every request through the policy and audits it; Supervision holds a silent, lost or could-not-start job; Budgets caps cost, tokens, memory and running jobs. Resources and Observability measure what they decide on. |
| **Work plugins** | Each module under `src/Modules`, with its core, an optional `.UI` project and optional public `Contracts`. A module depends only on the SDK, its own projects and other modules' contracts. |
| **Provider and forge plugins** | ClaudeCode implements `IAgentProvider`; GitHub and Gitea implement the forge contracts. The Simulator, ForgeSimulator and Recording plugins load only in developer mode and are left out of release packages. |
| **Module databases** | Each module's own SQLite database in the data folder, through EF Core with migrations. Every fact a screen presents as a job's record survives a restart. |
| **Machine settings** | The JSON files in the data folder; see [Configuration](../reference/configuration.md#machine-settings). |
| **Repository rules** | The `.avala` folder a repository commits. A job reads it from the commit it started from, so an agent's edit to it never applies to its own job. |
| **Job worktrees** | One git worktree per job, its processes tracked, its ports leased from a range, and `TMPDIR`, `TEMP` and `TMP` pointed at a scratch folder removed when the job ends. |
| **Preview servers** | What an agent serves for a job on its leased port, shown in the Preview pane. |
| **Webhook listener** | The Triggers module's loopback listener for signed webhooks. |

## See also

- [Architecture, in detail](../architecture.md): modules, cross-cutting rules, persistence, the Workbench, performance and testing.
- [Lifecycle diagrams](../diagrams/job-lifecycle.md), generated from the code.
- [Configuration](../reference/configuration.md).
