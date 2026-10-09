<h1>
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/assets/brand/avala-lockup-dark.png">
    <img src="docs/assets/brand/avala-lockup-light.png" alt="Avala" width="200">
  </picture>
</h1>

**Agents you can trust without watching.**

[![Debt grade](https://raw.githubusercontent.com/rick-dev-creator/avala/refs/heads/badges/debt.svg)](docs/architecture.md#technical-debt-grade)
[![Coverage](https://raw.githubusercontent.com/rick-dev-creator/avala/refs/heads/badges/coverage.svg)](docs/architecture.md#quality-metrics)
[![Maintainability](https://raw.githubusercontent.com/rick-dev-creator/avala/refs/heads/badges/maintainability.svg)](docs/architecture.md#technical-debt-grade)
[![Highest complexity](https://raw.githubusercontent.com/rick-dev-creator/avala/refs/heads/badges/complexity.svg)](docs/architecture.md#technical-debt-grade)
[![C# lines](https://raw.githubusercontent.com/rick-dev-creator/avala/refs/heads/badges/lines.svg)](docs/architecture.md#quality-metrics)
[![Share of T3 Code's lines](https://raw.githubusercontent.com/rick-dev-creator/avala/refs/heads/badges/t3-share.svg)](#a-note-on-t3-code)

Avala is an open-source desktop harness for coding agents. It runs each agent in its own git worktree and lets it work unattended, while every decision is governed by your rules, every result is backed by evidence, and every cost is accounted for.

![Avala's main window, from the approved design](docs/assets/avala-main-window.png)

<sub>The approved design of the main window. The application is being built to it.</sub>

## What it does

- **Evidence, not the agent's word.** A job is done only when your repository's own checks pass in its worktree, and its review shows the verdict first and only what deserves a look.
- **Policies, not prompts.** Every permission and question goes through rules the agent cannot edit, at the autonomy level you choose, and every answer is audited.
- **Nothing runs away.** Stalled or lost sessions are caught, spending stops at your caps, and leftover processes, ports and worktrees are reclaimed.
- **Any agent, any account.** Harnesses plug in behind one contract, with several connections per harness, and sub-agents run as governed jobs of their own.

## Status

Early and built in public. The core, the trust layer and the view models run end to end against a simulated agent; the views and the first real harness, Claude Code, come next.

## Build

Requires the .NET 10 SDK and git 2.40 or later.

```
dotnet build Avala.slnx
dotnet test --solution Avala.slnx
```

## Learn more

- [Architecture](docs/architecture.md): the rules every change must pass.
- [Core design](docs/design/core.md) and [plan](docs/plan/core.md).
- [AGENTS.md](AGENTS.md): how to contribute, with or without an agent.

## A note on T3 Code

Avala exists because of [T3 Code](https://github.com/pingdotgg/t3code). Its work showed what a desktop harness for coding agents can be, and the idea for this project was born from it. We admire what its developers have built and shared with everyone.

The comparison in the badges is only a yardstick: T3 Code is a well-known open-source harness, so its size gives us a fixed reference to measure our own against. It is not a criticism of the project or of the people behind it.

## License

[MIT](LICENSE)
