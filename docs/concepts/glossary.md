---
title: Glossary
ms.topic: concept-article
---

# Glossary

The terms this documentation uses.

- **Elastic Harness Chaining (EHC)**: A relay race for AI agents. When an agent is about to run out of memory or tokens, it packages its progress and hands it off to a fresh execution environment, a harness, so a long, complex task keeps going instead of collapsing. In Avala, a job winds down near a connection's usage limit and its brief passes to the next connection; the **Relays** pane draws the race. See [Budgets and usage limits](../guide/budgets-and-usage-limits.md).
- **Relay**: One handoff of a job's baton from one connection to the next, recorded with why it happened and how much of the window was spent.
- **Hard cap**: The share of a usage window a job winding down is stopped at, whatever it is doing.
- **Freed worktree**: The worktree of a job that no longer works, whose folder Avala removed to save disk while keeping its work under a hidden ref. It is rebuilt at the same path when the job continues. See [Worktrees and disk space](../guide/worktrees-and-disk-space.md).
