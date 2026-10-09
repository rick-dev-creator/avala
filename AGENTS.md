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
- No sleeps, no delays, no waits on time: `Thread.Sleep`, `Task.Delay`, spins and timers do not compile in tests either. Await the event or signal that states the thing happened, such as an integration event, a component's refreshed signal, a `TaskCompletionSource` or a channel, and drive behavior that is genuinely about time with `FakeTimeProvider`.
- Prove that a new rule or guard can fail: break it on purpose, watch the test fail, then restore it.

## Architecture

The rules are enforced by `tests/Avala.ArchitectureTests` and by the compiler. Do not work around them. If a rule blocks a legitimate change, raise it instead of bypassing it.

- **Modular monolith.** The host knows no module. Every module is internal and loaded as a plugin. A module depends only on the SDK, its own projects and other modules' `Contracts`.
- **Screaming architecture.** Name folders and namespaces after what the code does, such as `Submission`, `TurnChecks` or `Storage`, never after layers. Declare each new folder's layer in the layer map, `tests/Avala.ArchitectureTests/Scopes/LayerMap.cs`; an undeclared namespace fails the architecture tests.
- **Layers.** The layer map assigns each namespace to `Domain`, `Application`, `Infrastructure` or `ViewModels`. Dependencies point inward: view models use `Application`, never `Domain` or `Infrastructure`.
- **MVVM, view model first.** CommunityToolkit.Mvvm, with `[INotifyPropertyChanged]` instead of a base class. Every `XViewModel` has an `XView`. No fat view models: split them into child view models.
- **View model first, everywhere.** A view model never asks for its view: the view registry picks the view that shows it. Parents receive their child view models through dependency injection, as interfaces or factories, so every child can be replaced in a test.
- **Components and regions.** A component is a view model plus its XAML view. A page is drawn as named regions, an idea taken from Prism, and each region is filled with the view models of composable components that plugins register, so a page never knows the components a module contributes.
- **View models belong to their module and know no UI framework.** A module's view models and their interfaces live in the module's own core assembly, which references no UI framework at all. The views live in the module's `.UI` assembly. Supporting another technology, such as Blazor, MAUI, Uno or a terminal interface, means replacing the `.UI` assemblies and nothing else.
- **Shared components.** Components reused across modules are valid: their view models live in a shared assembly free of any UI framework, and their views in its own `.UI` assembly. Any module may depend on them as it depends on the SDK.
- **Folders by view.** Inside a `.UI` assembly, each view has a folder named after it that holds the view and the smaller components only it uses.
- **Every view model is mockable at design time.** Each view model has an interface that its view binds to, and a design-time implementation with realistic data shaped like the contracts. Each view declares its design-time `DataContext`, so the view renders without errors in the designer of Visual Studio or Rider, without the application running.
- **Code-behind only for presentation.** A view's code-behind may hold pure presentation concerns: animations, focus, scrolling, visual states, measuring. It never references services, module contracts or other view models, takes no dependencies, runs no business logic and changes its view model's state only through its commands.
- **Use XAML to the full.** Compiled bindings everywhere, with `x:DataType` on every view, so a broken binding fails the build. Styles, templates, converters and animations in XAML before code. Views take colors, brushes, font sizes, families and weights, radii and shadows from the theme's resources, never hard-coded; user actions are command bindings, never event handlers; an icon-only button declares `AutomationProperties.Name`; a region is named by its typed `{x:Static}`, never a string.
- **Size of a component.** A XAML file has at most 800 lines, its code-behind at most 400, and a view model at most 400. Beyond that, decompose the component into smaller ones.
- **Views stay dumb.** A view only binds: every user action is a command on its view model, with its `CanExecute`, and asynchronous commands carry cancellation and expose their running state. Which view shows a view model is decided by data templates through the view registry, never by a view creating another view or a view model.
- **Parent view models compose children.** A parent creates, owns, activates and deactivates its child view models through `IActivatable`, and passes data down through properties. A child never references its parent or a sibling: it reports upward through its own commands, events or a message.
- **Master and detail.** The master owns the list of item view models and the selection. The parent turns a selection into the detail view model it shows; the detail knows nothing of the master and works for any item handed to it.
- **The event aggregator.** Components that live in different regions or modules talk through UI messages over an injected messenger (CommunityToolkit's `IMessenger`), never by holding references to each other. A UI message is a small immutable record of what happened in the interface, such as a job selected. It never replaces a command to the core: work goes through the application services, and facts from the core arrive as integration events adapted for the interface.
- **One item, one view model.** Each kind of row, entry or card is its own small view model with its own view, so lists stay templates over view models.
- **Collections change on the UI thread**, through `IUiDispatcher`, updated in place where possible so lists do not flicker or lose their selection and scroll position.
- **Every finished component and page has scripted acceptance tests.** Its acceptance criteria are written first, as for any test, and each one becomes a script that plays the user's actions, such as selecting, typing, pressing a key or invoking a command, and asserts what the user would see. Unit tests are preferred: the view model driven with fakes or its design-time data. The view itself is driven headless, rendered without a screen, to prove its bindings, templates and keyboard handling work. A page that composes regions also has scripted tests of the composition: the regions are filled, the region context reaches every component, and a change in one region shows in the others. These tests prove the interface works on every build, without anyone, person or model, having to look at it. Scripts live in a class named after the component with the suffix `Scripts`, written with `ViewModelScript` and `ViewScript`, and wait deterministically, only on events: a core event or the component's own presented or refreshed signal, never polling or time. A timeout exists only as a guard against a hang, and when it fires it reports the last state the script saw.
- **View models are a thin application layer.** They expose state to the view and delegate the work of every command to services injected through their constructor. They never know infrastructure: it reaches them only through the ports of the application layer.
- **Interfaces only where they earn their place:** for infrastructure and for what must be replaceable, such as the ports a test replaces with a double. Everything else stays concrete and domain-centric; prefer extension members and plain domain code over an interface with a single implementation.
- **No fat constructors.** More than four dependencies is a smell that calls for a refactoring, and the architecture tests reject it. Records holding data are exempt.
- **Composition over inheritance.** Every class is `sealed`. Only framework types may be inherited: Avalonia types and EF Core's `DbContext`.
- **Concurrency only through the event bus or channels.** No `lock`, `Lock`, semaphore, monitor, mutex, wait handle, barrier or concurrent collection in `src/`. State belongs to one reader: a handler's mailbox, or a channel consumer such as the SDK's `SerialExecutor`. Queries read an immutable snapshot the owner replaces whole, published with `Volatile` or `ImmutableInterlocked`. A `TaskCompletionSource` as a one-shot signal is fine; tests may use what they need.

## Domain

- DDD without base classes: aggregates implement `IAggregateRoot<TId>`, events implement `IDomainEvent`.
- Aggregates have private constructors and factory methods, no public setters, no exposed mutable collections, and reference other aggregates by identifier only.
- Every public operation returns `Result<TEvent, TError>`. Results never throw: read them through `Match` or `TryGetValue`.
- Absence is an `Option<T>` handled with `Match`, never `null` in a signature. Turn nullable values from frameworks into an `Option` at the edge with `ToOption()`.
- A module that can reject an operation has exactly one error enum; a pure projection such as Observability has none. No magic strings: error codes are enum values, and the presentation layer turns them into text.
- The domain performs no I/O, has no async methods and never throws.
- State machines use Stateless, inside the domain only, through `TryFire`. Direct `Fire` calls do not compile.
- Diagrams in `docs/diagrams` are generated from the code. Refresh them with `AVALA_UPDATE_DIAGRAMS=1 dotnet test --solution Avala.slnx`.

## Code

- Modern C# 14 on .NET 10. Lean and declarative: the smallest code that states the behavior clearly.
- No comments, anywhere: not in C#, not in XAML, not even XML documentation. Explanations belong in `docs/`.
- No type longer than 600 lines.
- Nothing blocks a thread: no `Thread.Sleep`, `Wait`, `Result`, `GetResult` or synchronous file I/O. Everything is asynchronous.
- Nothing waits on time instead of an event: no `Thread.Sleep`, `Task.Delay`, `SpinWait`, `System.Threading.Timer`, `System.Timers.Timer` or `PeriodicTimer`. Await the event that states the thing happened (an integration event, a component's signal, a `TaskCompletionSource`, a channel, `UntilCancelledAsync` on a token); behavior genuinely about time uses `TimeProvider.CreateTimer`.
- Warnings are errors. An analyzer exception is allowed only with a reason recorded in [docs/architecture.md](docs/architecture.md#analyzer-exceptions).
- Every script is C#: .NET file-based apps in `scripts/`, run with `dotnet run`. No Python, shell, PowerShell, JavaScript or any other language, and CI steps only invoke `dotnet`. The architecture tests enforce both.

## What to avoid

Avala exists partly as an answer to harnesses whose code grew out of control. Do not reproduce their failure modes:

- No god class: an orchestrator, a view or a service that keeps growing with every feature. Split responsibilities into modules and small types.
- No patch on top of a patch. Fix the cause, add the test that would have caught it, and keep the design coherent.
- No provider-specific branches outside a provider's own plugin. The core decides by declared capabilities.
- Avala offers, harnesses adapt. The core is agnostic: it offers its tools, such as the canvas, delegation or follow-ups, together with what it supports, such as the media types its renderers draw, and every harness adapts to that offer through its plugin. Never bend the core to what one harness happens to produce.
- No big-bang rewrites. Change in small, verified steps.

## Documentation

- Repository content is written in English.
- At the end of every session, record it in `docs/sessions` with its token usage and cost, following [docs/sessions/README.md](docs/sessions/README.md).
- Update the design and the plan in the same change when a decision changes.
- Record every analyzer exception and its reason in `docs/architecture.md`.
