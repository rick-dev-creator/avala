# AGENTS.md

Guidance for agents working on Avala.

If a `.method/` folder exists in this checkout, read `.method/AGENTS.md` first and follow it; it takes precedence.

## Build and test

Requires the .NET 10 SDK and git 2.40 or later.

```
dotnet build Avala.slnx
dotnet test --solution Avala.slnx
```

Both must be green, with no warnings, before and after every change.

## Rules

- The architecture tests in `tests/Avala.ArchitectureTests`, the analyzers and CI enforce the rules. A failing rule explains itself: read its message and fix the cause, never work around it.
- Everything is proven through the simulator: a behavior is tested end to end with the simulated harness, never with real tokens.
- One test per behavior, named after the behavior.
- No comments in code. Explanations belong in `docs/`, and an analyzer exception is recorded in [docs/architecture.md](docs/architecture.md#analyzer-exceptions).
- Repository content is written in English.

## Learn more

- [Architecture](docs/architecture.md): the big picture.
- [Contributing](CONTRIBUTING.md): how a change gets in.
