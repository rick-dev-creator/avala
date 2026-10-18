---
title: Quickstart
ms.topic: quickstart
---

# Quickstart

Run Avala for the first time, from a release package or from source, and start your first job.

## Prerequisites

- **Claude Code**, installed and signed in, so `claude` is on your `PATH`. Avala finds each login on its own: `~/.claude`, every `~/.claude-*` folder, the folder in `CLAUDE_CONFIG_DIR` and the `ANTHROPIC_API_KEY` family of variables.
- **git** 2.40 or later.
- **To build from source**: the .NET 10 SDK (`global.json` pins `10.0.100`, rolling forward to the latest feature band).

## Install a release

Avala is a prerelease. Download the package for your platform from the [releases page](https://github.com/rick-dev-creator/avala/releases) and read its notes first.

| Platform | Package |
| --- | --- |
| Windows x64 | `avala-<version>-win-x64-setup.exe`, or the portable zip unzipped anywhere. The builds are not signed yet: on SmartScreen's warning choose More info, Run anyway. |
| Linux x64 | `avala-<version>-linux-x64.AppImage`, after `chmod +x`, or the `linux-x64.tar.gz` unpacked. |
| Flatpak | `io.github.rick_dev_creator.Avala`, built from `packaging/flatpak`. |
| macOS | No package yet; build from source. |

The setup, the portable zip and the AppImage update themselves in place; the other packages open the release page when a newer version is out.

## Build from source

From the repository's root:

```
dotnet build Avala.slnx
dotnet test --solution Avala.slnx
dotnet run --project src/Avala.Host
```

The build treats warnings as errors, so a clean build has none. A build from source sends no usage statistics.

To run Avala end to end without an agent account or tokens, start it in developer mode, which loads the simulated harness and the simulated forge, and keep its data apart from your own:

```
AVALA_DEVELOPER=1 AVALA_DATA_PATH=/tmp/avala-dev dotnet run --project src/Avala.Host
```

On a simulator connection, the first message of a job chooses a scenario with a tag such as `[simulate: edit]`; without one, the agent simply replies.

To check that a build composes and shows its window, then exits, run it with `--smoke`:

```
dotnet run --project src/Avala.Host -- --smoke
```

## Run your first job

1. **Open Avala.** A four-step welcome tour shows the agents it found, your projects folder and source-control sign-ins, and your privacy choices.
2. **Start a chat or pick a repository.** Type a task on the welcome screen to run it in a scratch repository, or open a folder, clone or create a repository, and start a job from **New job**.
3. **Follow it.** The job runs in its own worktree. Answer what the policy leaves to you, and when the job is ready, review its checks and diff, then approve or discard it.

## Next steps

- [Getting started](../guide/getting-started.md) in the guide, then one page per feature.
- [Configuration](../reference/configuration.md): what the data folder and a repository's `.avala` folder hold.
- [Troubleshooting](../guide/troubleshooting.md), with where the logs are.
