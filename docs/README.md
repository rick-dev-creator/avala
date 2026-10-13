# Documentation

Avala's documentation, by who it is for. Start with the first document of your row and read on in order.

| You are | Read, in order |
| --- | --- |
| **Using Avala** | The [README](../README.md), then the [guide](guide/README.md), from Getting started on |
| **Contributing to Avala** | [Contributing](../CONTRIBUTING.md), then the [architecture](architecture.md), then the [diagrams](#diagrams) as you need them |
| **Releasing Avala** | [Releasing](release.md), then the [release notes](releases/) |
| **An agent working on Avala** | [AGENTS.md](../AGENTS.md), then the same as contributing |

## The documents

| Document | For | What it holds |
| --- | --- | --- |
| [README](../README.md) | Everyone | What Avala is, its status, installing and privacy in short |
| [Guide](guide/README.md) | People using Avala | One page per feature: what it does, how to use it, its settings and limits |
| [Contributing](../CONTRIBUTING.md) | Contributors | Building, testing, the simulator, screenshots and how a change gets in |
| [Architecture](architecture.md) | Contributors | The modules, the rules every change must pass, persistence, the Workbench, performance and testing |
| [Releasing](release.md) | Maintainers | Versions, packages, signing, usage statistics and the update check |
| [Release notes](releases/) | Everyone | The notes of each release, one file per tag, which the release workflow uses for its draft |

### Diagrams

State diagrams generated from the code, which the tests keep up to date: the [job](diagrams/job-lifecycle.md), [turn](diagrams/turn-lifecycle.md), [workspace](diagrams/workspace-lifecycle.md), [canvas](diagrams/canvas-lifecycle.md) and [contest](diagrams/contest-lifecycle.md) lifecycles.

## How a guide page is written

Every page of the guide has the same sections, in this order, so a reader always knows where to look:

1. **Title**, the feature's name.
2. **A one-sentence summary** right under it.
3. **What it does**: the behavior, in a few short paragraphs or bullets.
4. **How to use it**: numbered steps, with the labels exactly as the app shows them.
5. **Settings and files**: the exact settings, file names, keys and environment variables, with a small example when a file is involved.
6. **Good to know**: limits, what is not done yet, and what to expect.
7. **See also**: links to related pages.

A section with nothing to say is left out rather than filled. Names are the ones in the app, files are in `code`, and a feature that is not built yet is said to be not built yet, never described.

## Where files live

Avala keeps its own data in one **data folder**: the folder in `AVALA_DATA_PATH` when it is set, and otherwise `Avala` in your local application data folder, such as `~/.local/share/Avala` on Linux or `%LOCALAPPDATA%\Avala` on Windows. Your settings are small JSON files there, such as `appearance.json`, and its `logs` folder holds the logs.

A repository's own rules live in its `.avala` folder, committed with the code: `permissions.json`, `checks.json`, `budget.json`, `jobs.json`, `backlog.json` and `triggers.json`. A job reads them from the commit it started from, so an agent's edit to them never applies to its own job.
