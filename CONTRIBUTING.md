# Contributing to Avala

Thank you for helping. This page is the practical side of a change: building, running, testing and sending it. Read the [architecture](docs/architecture.md) next for the big picture and the rules, and the [documentation index](docs/README.md) for everything else.

## Build and test

Requires the .NET 10 SDK and git 2.40 or later.

```
dotnet build Avala.slnx
dotnet test --solution Avala.slnx
```

Both must be green before and after every change. Warnings are errors, so a clean build has none. `Avala.UnitTests.slnf` holds the unit tests alone, the ones coverage is measured on.

## Run the simulator

A build from source holds a simulated harness, so you can run Avala end to end without an agent account or tokens. It exists only to develop and test Avala: the simulators and the session recorder are development-only plugins that the released packages leave out, as [Releasing](docs/release.md#the-production-plugin-set) describes. Build, then start the application in developer mode:

```
AVALA_DEVELOPER=1 dotnet run --project src/Avala.Host
```

Create a job on a simulator connection. The first message chooses a scenario with a tag such as `[simulate: edit]`; without one, the agent simply replies. To keep your own data apart, set `AVALA_DATA_PATH` to another folder.

## The rules

The rules are enforced by the architecture tests in `tests/Avala.ArchitectureTests`, by the analyzers and by CI. When one fails, its message names the file and says how to fix it. Fix the cause; if a rule blocks a legitimate change, raise it in an issue or the pull request instead of bypassing it.

- No comments in code. Explanations belong in `docs/`, and an analyzer exception is recorded in [Analyzer exceptions](docs/architecture.md#analyzer-exceptions).
- Every script is a C# file-based app in `scripts/`, run with `dotnet run scripts/<name>.cs`.
- Code, documentation and commit messages are written in English.

## Tests

- One test per behavior, named after what it proves, such as `ApprovingFinishesTheJob`.
- Search for an existing test before writing a new one; extend it rather than duplicate it.
- Unit tests by default. Behavior across modules is proven through the simulator, never a real agent.
- No waiting on time: await the event that states the thing happened.
- A view is tested headless, driven as a user would drive it.
- The lifecycle diagrams in `docs/diagrams` are checked against the code; run the tests with `AVALA_UPDATE_DIAGRAMS=1` to rewrite them after changing a lifecycle.

## Feature screens

The pictures in the README and the feature screens are taken from the real application, driven headless by the simulator, by the screen tests in `tests/Avala.Host.Tests` (such as `FeatureScreens`, `WelcomeScreens`, `PreviewScreens` and `ReadmeScreens`). They are skipped unless both variables are set:

```
AVALA_FEATURE_SCREENS=<folder> AVALA_HEADLESS_RENDER=1 dotnet test --project tests/Avala.Host.Tests
```

The screens are written to the folder as PNG files, most in both the dark and the light theme. `ReadmeScreens` also films the README's animations into `<folder>/reels`, one PNG per frame at 12 frames per second, in both themes: it advances the app's fake clock and the SVG animation clock frame by frame instead of waiting on time. Then turn them into the README's files, which needs `ffmpeg` built with libwebp:

```
dotnet run scripts/media.cs -- <folder>
```

The script writes into `docs/assets/readme`: an animated WebP for each reel in each theme, a GIF of the light reel for viewers without WebP, a PNG for each single-frame reel, and the PNGs it picks from the other feature screens. It fails when a WebP is over 2 MB or a GIF over 5 MB. Delete `<folder>` afterwards; only `docs/assets/readme` is committed.

## Documentation

- A change that a person would notice updates its page of the [guide](docs/guide/README.md), in the format the [documentation index](docs/README.md#how-a-guide-page-is-written) describes.
- A change to how Avala is built updates the [architecture](docs/architecture.md).
- Use the names the app shows, and never describe a feature that is not built yet.

## Issues

Work is tracked in GitHub issues, written in English. Open one with the Bug report or Feature request form; blank issues are turned off, and each form's required fields must be filled before it can be submitted. Open-ended ideas and questions start in [Discussions](https://github.com/rick-dev-creator/avala/discussions), and become an issue once the work is agreed.

## How work flows

Issues are planned on the [Avala project board](https://github.com/users/rick-dev-creator/projects/4), release by release:

| Status | Means |
| --- | --- |
| Backlog | Not planned for a release yet. |
| Ready | Triaged: it has acceptance criteria, a priority, a size and a release, the milestone it ships in. |
| In progress | Someone is working on it in its own branch, `fix/<issue>-<words>` or `feat/<issue>-<words>`. |
| In review | A pull request that fixes it is open. |
| Done | Merged into `main`. |

- **Priority.** P0 blocks using Avala, P1 hurts every use, P2 is an improvement, P3 an idea to explore. Ready work is taken by priority.
- **Size.** S takes hours, M about a day, L several days; an L issue is split into sub-issues so each pull request stays small.
- **Releases.** Each release is a milestone. When its last issue is done, the release is tagged, its notes are written from its closed issues, and the milestone is closed.

## Commits and pull requests

- Keep each change small and focused, one commit per step.
- A commit message is one English sentence in the imperative that says what changed and why, such as "Read the wrapped timeline entry by its index, since the oldest entry is built only by the navigation".
- A pull request must be green on CI before it is reviewed. CI runs on Linux; a change that touches Windows or macOS, such as processes, paths or containment, is also run there by hand with Run workflow on its branch.
- A pull request says `Fixes #<issue>`, lists the issue's acceptance criteria, and shows the evidence: the tests that prove them and, for a change a person sees, screenshots.
- `main` is protected: every change reaches it through a pull request with CI green, merged by squash or rebase so the history stays linear. Bugs and small changes are merged once green; a feature with a visible change waits for the maintainer's approval.
