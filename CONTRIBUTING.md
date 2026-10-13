# Contributing to Avala

Thank you for helping. Start with the [architecture](docs/architecture.md) for the big picture.

## Build and test

Requires the .NET 10 SDK and git 2.40 or later.

```
dotnet build Avala.slnx
dotnet test --solution Avala.slnx
```

Warnings are errors, so a clean build has none.

## Run the simulator

Avala ships with a simulated harness, so you can run it end to end without an agent account or tokens. Build, then start the application in developer mode:

```
AVALA_DEVELOPER=1 dotnet run --project src/Avala.Host
```

Create a job on a simulator connection. The first message chooses a scenario with a tag such as `[simulate: edit]`; without one, the agent simply replies.

## The rules

The rules are enforced by the architecture tests in `tests/Avala.ArchitectureTests`, by the analyzers and by CI. When one fails, its message names the file and says how to fix it. Fix the cause; if a rule blocks a legitimate change, raise it in an issue or the pull request instead of bypassing it.

## Tests

- One test per behavior, named after what it proves, such as `ApprovingFinishesTheJob`.
- Search for an existing test before writing a new one; extend it rather than duplicate it.
- Unit tests by default. Behavior across modules is proven through the simulator, never a real agent.
- No waiting on time: await the event that states the thing happened.

## Pictures in the README

Every picture in the README is captured from the real application, driven headless by the simulator, and turned into files by a script. The feature-screen tests are skipped unless both variables are set; `ReadmeScreens` films its reels frame by frame at 12 frames per second, advancing the app's fake clock and the SVG animation clock instead of waiting on time, in both themes. The script needs `ffmpeg` built with libwebp.

```
AVALA_HEADLESS_RENDER=1 AVALA_FEATURE_SCREENS=<folder> dotnet test --project tests/Avala.Host.Tests -- --filter-class "*Screens"
dotnet run scripts/media.cs -- <folder>
```

The script writes into `docs/assets/readme`: an animated WebP for each reel in each theme, a GIF of the light reel for viewers without WebP, a PNG for each single-frame reel, and the PNGs it picks from the other feature screens. It fails when a WebP is over 2 MB or a GIF over 5 MB. Delete `<folder>` afterwards; only the files in `docs/assets/readme` are committed.

## Pull requests

- Keep each change small and focused.
- A pull request must be green on CI, on Linux, Windows and macOS, before it is reviewed.
- Write code, documentation and commit messages in English.
