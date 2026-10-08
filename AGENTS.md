# AGENTS.md

Guidance for every agent working on Avala. Avala aims to be a better harness than its competitors in no more than 15% of their code, and to show that good engineering practices still matter when agents write the code. Every rule here serves that goal.

## Before you change anything

1. Read [docs/architecture.md](docs/architecture.md) and the [core design](docs/design/core.md).
2. Check the [action plan](docs/plan/core.md) to see which phase the work belongs to.
3. Run the build and the tests. They must be green before and after your change.

```
dotnet build Avala.slnx
dotnet test --solution Avala.slnx
```

## Writing tests

Tests come before the code they verify, and every test follows this workflow.

### 1. Write the acceptance criteria first

Before writing any test, list the acceptance criteria of the behavior in the task, the pull request or the conversation. Each criterion:

- describes one observable behavior, in Given / When / Then form;
- names the expected outcome precisely: the returned value, the event, the error code or the new state;
- covers the rejected paths as well as the successful one.

```
AC1  Given a job awaiting review, when it is approved, then it returns JobApproved and its state is Approved.
AC2  Given a job in any other state, when it is approved, then it returns CannotApprove and its state is unchanged.
```

### 2. Check for duplicates

Before writing a test for a criterion, verify that no existing test already covers it:

- Search the test projects for the behavior: test names, the operation under test and the asserted outcome.
- Check the broad tests that cover many cases at once. For example, `JobTransitionTests` already checks every operation in every state of the job lifecycle, so a new test must not re-check a single transition.
- If a test already covers the criterion, reuse it or extend it. Never add a second test for the same behavior.
- If two existing tests overlap, merge them in the same change.

### 3. Write the tests

- One test per acceptance criterion. The test name states the behavior: `ApprovingFinishesTheJob`, not `Test1` or `ApproveWorks`.
- Unit tests by default. Integration tests only when a behavior cannot be verified otherwise, such as git or the file system.
- No sleeps, no waits on real time. Use `TimeProvider` and awaitable signals.
- Prove that a new rule or guard can fail: break it on purpose, watch the test fail, then restore it.

## Architecture

The rules are enforced by `tests/Avala.ArchitectureTests` and by the compiler. Do not work around them. If a rule blocks a legitimate change, raise it instead of bypassing it.

- **Modular monolith.** The host knows no module. Every module is internal and loaded as a plugin. A module depends only on the SDK, its own projects and other modules' `Contracts`.
- **Layers.** Module cores are organized in `Domain`, `Application`, `Infrastructure` and `ViewModels`. Dependencies point inward: view models use `Application`, never `Domain` or `Infrastructure`.
- **MVVM, view model first.** CommunityToolkit.Mvvm, with `[INotifyPropertyChanged]` instead of a base class. Every `XViewModel` has an `XView`. View code-behind contains only the constructor calling `InitializeComponent()`. No fat view models: split them into child view models.
- **View models are a thin application layer.** They expose state to the view and delegate the work of every command to services injected through their constructor. Every constructor parameter is an interface, so tests replace it with a double, and a view model never knows infrastructure.
- **Composition over inheritance.** Every class is `sealed`. Only Avalonia types may be inherited.

## Domain

- DDD without base classes: aggregates implement `IAggregateRoot<TId>`, events implement `IDomainEvent`.
- Aggregates have private constructors and factory methods, no public setters, no exposed mutable collections, and reference other aggregates by identifier only.
- Every public operation returns `Result<TEvent, TError>`. Results never throw: read them through `Match` or `TryGetValue`.
- Each module has exactly one error enum. No magic strings: error codes are enum values, and the presentation layer turns them into text.
- The domain performs no I/O, has no async methods and never throws.
- State machines use Stateless, inside the domain only, through `TryFire`. Direct `Fire` calls do not compile.
- Diagrams in `docs/diagrams` are generated from the code. Refresh them with `AVALA_UPDATE_DIAGRAMS=1 dotnet test --solution Avala.slnx`.

## Code

- Modern C# 14 on .NET 10. Lean and declarative: the smallest code that states the behavior clearly.
- No comments, anywhere: not in C#, not in XAML, not even XML documentation. Explanations belong in `docs/`.
- No type longer than 600 lines.
- Nothing blocks a thread: no `Thread.Sleep`, `Wait`, `Result`, `GetResult` or synchronous file I/O. Everything is asynchronous.
- Warnings are errors. An analyzer exception is allowed only with a reason recorded in [docs/architecture.md](docs/architecture.md#analyzer-exceptions).
- Every script is C#: .NET file-based apps in `scripts/`, run with `dotnet run`. No Python, shell, PowerShell, JavaScript or any other language, and CI steps only invoke `dotnet`. The architecture tests enforce both.

## What to avoid

Avala exists partly as an answer to harnesses whose code grew out of control. Do not reproduce their failure modes:

- No god class: an orchestrator, a view or a service that keeps growing with every feature. Split responsibilities into modules and small types.
- No patch on top of a patch. Fix the cause, add the test that would have caught it, and keep the design coherent.
- No provider-specific branches outside a provider's own plugin. The core decides by declared capabilities.
- No big-bang rewrites. Change in small, verified steps.

## Documentation

- Repository content is written in English.
- At the end of every session, record it in `docs/sessions` with its token usage and cost, following [docs/sessions/README.md](docs/sessions/README.md).
- Update the design and the plan in the same change when a decision changes.
- Record every analyzer exception and its reason in `docs/architecture.md`.
