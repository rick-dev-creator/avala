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

Avala ships with a simulated harness, so you can run it end to end without an agent account or tokens. Build, then start the application in developer mode:

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

The screenshots in the README and the feature screens are taken from the real application, driven headless by the simulator, by the screen tests in `tests/Avala.Host.Tests` (such as `FeatureScreens`, `WelcomeScreens` and `PreviewScreens`). They are skipped unless both variables are set:

```
AVALA_FEATURE_SCREENS=<folder> AVALA_HEADLESS_RENDER=1 dotnet test --project tests/Avala.Host.Tests
```

The screens are written to the folder as PNG files, most in both the dark and the light theme.

## Documentation

- A change that a person would notice updates its page of the [guide](docs/guide/README.md), in the format the [documentation index](docs/README.md#how-a-guide-page-is-written) describes.
- A change to how Avala is built updates the [architecture](docs/architecture.md).
- Use the names the app shows, and never describe a feature that is not built yet.

## Commits and pull requests

- Keep each change small and focused, one commit per step.
- A commit message is one English sentence in the imperative that says what changed and why, such as "Read the wrapped timeline entry by its index, since the oldest entry is built only by the navigation".
- A pull request must be green on CI, on Linux, Windows and macOS, before it is reviewed.
