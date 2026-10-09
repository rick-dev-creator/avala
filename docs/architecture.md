# Architecture

Avala is a modular monolith. The host knows nothing about the features it runs: every module is a plugin discovered at runtime.

## Layout

| Path | Role |
| --- | --- |
| `src/Avala.Sdk` | Public contracts for plugins. No Avalonia, no other Avala dependency. |
| `src/Avala.Sdk.UI` | Public contracts for plugin views. Depends only on the SDK. |
| `src/Avala.Shell` | Shell view models. Depends only on the SDK. |
| `src/Avala.Runtime` | Runtime services shared by every module, such as the event bus. Depends only on the SDK. |
| `src/Avala.Host` | Avalonia application, composition root and plugin loader. References only the SDK, the runtime and the shell. |
| `src/Modules/<Module>/Avala.<Module>` | Module core: domain, use cases, infrastructure and view models, in feature folders. Everything is `internal`. |
| `src/Modules/<Module>/Avala.<Module>.UI` | Module views and its single public type: the plugin entry. |
| `src/Modules/<Module>/Avala.<Module>.Contracts` | Optional public contracts other modules may depend on. |
| `src/Modules/Simulator/Avala.Simulator` | A provider plugin that plays scripted Claude Code sessions through the public agent contracts only, for demos and tests without tokens. Its plugin entry lives in the core, since it has no views. |
| `tests/Avala.ArchitectureTests` | The rules below, enforced on every build. |
| `tests/Avala.ArchitectureTests.Fixtures` | A compliant sample module and a module that breaks every rule on purpose. |
| `tests/Avala.Testing` | Helpers shared by the test projects: result assertions, a recording bus, a scripted agent provider, temporary folders and git repositories, committed rule files behind `IBaseFiles`, event watches with a safety timeout, generated diagrams. |
| `tests/Avala.<Project>.Tests` | Unit tests. |
| `tests/Avala.Integration.Tests` | End-to-end tests that compose the real modules through their public plugin entries, over a real git repository and SQLite. Not part of `Avala.UnitTests.slnf`. |
| `tests/Avala.Host.Tests` | Simulation tests of the real application: the composition root built from the published plugin folder, driven by the simulator over a real git repository. Not part of `Avala.UnitTests.slnf`. |

## Screaming architecture

Inside a module, folders and namespaces are named after what the code does: its domain model, its use cases and the infrastructure they reach. Never after technical layers. Namespaces follow folders. The Jobs core reads like this:

| Folder | Holds | Layer |
| --- | --- | --- |
| `Jobs` | The `Job` aggregate, its attempts, lifecycle, value objects, error enum and domain events | Domain |
| `Submission` | Creating and submitting a job, and the `IJobs` entry other modules call | Application |
| `Launching` | Preparing the workspace, opening the agent session and sending the instruction | Application |
| `TurnChecks` | Checking a finished turn against the completion gates, and holding a job whose session was lost | Application |
| `Recovery` | Resuming active jobs at startup | Application |
| `Holding` | Holding a running job for a typed reason and halting its agent session | Application |
| `Ledger` | Storing a job and announcing its progress, with the `IJobStore` port, and `JobQueues`, which runs the work on each job in order | Application |
| `Storage` | The EF Core store behind `IJobStore` | Infrastructure |
| `JobList` | The jobs page view model | ViewModels |

The other cores follow the same idea: Agents has `Turns` and `Sessions`; Workspaces has `Workspaces`, `Provisioning`, `BaseFiles`, `Git` and `Storage`; Canvas has `Canvases`, `Gallery`, `Streaming` and `Throttling`; Observability has `Usage`, `Tracking` and `Metrics`; Verification has `Checks`, `Verifying` and `Evidence`; Permissions has `Policies`, `Governance`, `Answering` and `PolicyFiles`; Supervision has `Watching`, `Supervising` and `Settings`; Budgets has `Caps`, `Enforcement` and `BudgetFiles`; the simulator has `Scenarios`, `Playback` and `FileSystem`. Plugin entries stay at the root of their project, and `Contracts` projects keep their own names.

### Layer map

The layers still exist, and the rules still enforce them, but no folder name carries them. `tests/Avala.ArchitectureTests/Scopes/LayerMap.cs` is the single place that assigns every namespace of every module assembly to a module and a layer: `Domain`, `Application`, `Infrastructure`, `ViewModels`, `Contracts`, or `None` for plugin entries and views. Every layer rule asks the map. The fixture modules keep their layer-named folders and are declared in the map as well.

A namespace missing from the map fails the architecture tests. A new folder therefore needs a conscious decision about its layer, recorded in the map in the same change.

## Rules

Enforced by `tests/Avala.ArchitectureTests`:

- The host references only the SDK, the runtime and the shell, and depends on no module.
- A module depends only on the SDK, its own projects and other modules' `Contracts`.
- A module's `Contracts` depend only on the SDK and other modules' `Contracts`: identifiers such as `JobId` or `SessionId` are a vocabulary the modules share, never their internals.
- A module exposes exactly one public type outside its `Contracts`: its `IPlugin` entry.
- `InternalsVisibleTo` targets only the project's own module and its test project. The shell and the runtime may also open to the host.
- View models live in assemblies that do not reference Avalonia.
- Every `XViewModel` has an `XView` and the other way around. Views are resolved view-model-first through the view registry.
- Code-behind holds nothing but a constructor calling `InitializeComponent()`.
- No class takes more than four constructor dependencies. Records holding data are exempt.
- Every class is `sealed`. Only framework types may be inherited: Avalonia types for views and the application, and EF Core's `DbContext` for each module's database.
- No non-private member exposes a nullable type in its signature: properties, fields, parameters and return types, generic arguments such as `Task<T?>` included. Absence is an `Option<T>`. Exempt: members that implement framework interfaces or override framework members, such as Avalonia's `IDataTemplate`; properties of view models, since an empty selection is `null` in Avalonia; the `Optional` bridge; unconstrained generic type parameters; and generated code.
- No comments, in C# or in XAML.
- No type spans more than 600 lines, counting every part of a partial type.
- Every script is C#, and CI steps only invoke `dotnet`.
- The SDK, the runtime, the shell and every module core have a unit test project.
- Every namespace of a module assembly is declared in the [layer map](#layer-map).
- The DDD and layer rules of the [core design](design/core.md#architecture-rules-to-add), with each type's layer taken from the layer map.
- Only infrastructure types of the modules depend on `Microsoft.EntityFrameworkCore`.
- Database work never runs on the UI thread. Every infrastructure type that declares a field whose type derives from `DbContext` must be declared in a source file that calls `Task.Run`. The rule checks that the call is present in that file, not that every database operation goes through it; the stores route every operation through a single `RunAsync` that awaits `Task.Run`, as the [core design](design/core.md#sqlite-and-blocking) requires.
- The architecture tests reference every source project, so no project escapes the rules.
- Concurrency between components goes only through the event bus or `System.Threading.Channels`. No source file under `src/` holds a `lock` statement or names a coordination primitive: `Lock`, `Monitor`, `SemaphoreSlim`, `Semaphore`, `Mutex`, `ReaderWriterLock`, `ReaderWriterLockSlim`, `SpinLock`, `SpinWait`, `Barrier`, `CountdownEvent`, the reset events, `EventWaitHandle`, `WaitHandle`, or a concurrent collection. State is owned by one reader, a handler's mailbox or a channel consumer such as `SerialExecutor` in the SDK, and queries read an immutable snapshot the owner replaces whole. Publishing that reference with `Volatile` or `ImmutableInterlocked` is not coordination, and a `TaskCompletionSource` as a one-shot signal is allowed. Tests may use what they need.

Enforced by the compiler through `BannedSymbols.txt` and the threading analyzers:

- Nothing blocks a thread: no `Thread.Sleep`, `Wait`, `Result`, `GetResult`, synchronous waits or synchronous file I/O.
- No `async void`.
- No direct `Fire` on a Stateless machine: transitions go through the guarded `TryFire`.
- No coordination primitive in `src/`: `src/BannedSymbols.Concurrency.txt` bans the types above for every source project, so a violation fails the build before the architecture tests run. A `lock` on a plain object names no banned type, so only the architecture rule catches it.

Every rule is checked against the production modules, a compliant fixture module and a violating fixture module. A rule must pass on the first two and find exactly the expected violations in the third, so it can never pass vacuously.

## Analyzer exceptions

Comments are banned, so every exception to an analyzer is recorded here.

| Rule | Scope | Reason |
| --- | --- | --- |
| `MVVMTK0032` | Everywhere | It recommends inheriting from `ObservableObject`. View models use `[INotifyPropertyChanged]` to favor composition. |
| `CA1000` | Everywhere | `Result<TValue, TError>.Success` and `Failure` are the canonical static factories of a generic result. |
| `VSTHRD003` | Everywhere | It guards against deadlocks under Visual Studio's `JoinableTaskFactory`, which Avala does not use. Awaiting a stored task, such as the event bus loop, is correct here. |
| `CA1716` | Everywhere | It reserves Visual Basic keywords such as `Option`. Avala is C# only, and `Option<T>` is the established name of the pattern. |
| `RS0030` | `GuardedTransitions.cs` | The guarded transition helper is the single place allowed to call `Fire`, right after `CanFire`. |
| `RS0030` | xUnit's generated entry point | Third-party generated code that blocks on the test platform's task. |
| All analyzers | `Avala.ArchitectureTests.Fixtures` | The fixtures break rules on purpose. |

## Plugins

A project becomes a plugin with `<AvalaPlugin>true</AvalaPlugin>` and `<EnableDynamicLoading>true</EnableDynamicLoading>`. Every module entry is one: Agents, Workspaces, the Jobs UI, Canvas, Observability, Verification, Permissions, Supervision, Budgets and the simulator. Its build output is copied to `artifacts/plugins/<AssemblyName>`. The host loads every folder there, or the folder named by `AVALA_PLUGINS_PATH`, in folder name order.

All plugins share the host's default load context, so every assembly is loaded once:

- An assembly the host already ships, such as the SDK, the runtime, dependency injection or Avalonia, comes from the host.
- Any other assembly, such as a module's `Contracts`, EF Core or a native library like SQLite, is resolved from the plugin folders through each plugin's `.deps.json`, the first time anyone asks for it.
- A contract therefore has one type identity for every module: the `IAgentProvider` the simulator implements is the one Agents asks for.

Isolating each plugin in its own load context would load a `Contracts` assembly once per plugin and break every cross-module contract. Isolation can come back, as one shared context for module contracts plus private contexts, if a third-party plugin ever needs a dependency that conflicts with another plugin.

The host knows no module: the loader only scans folders and instantiates the `IPlugin` types it finds. `tests/Avala.Host.Tests` composes the application from the published folder exactly as the host does at startup. Like the host, it does not ship the module contracts it compiles against, so they come from the plugin folder, which its assembly fixture loads before any test runs.

## Testing

Unit tests are the default. Integration tests are added only when a behavior cannot be verified otherwise.

```
dotnet build Avala.slnx
dotnet test --solution Avala.slnx
```

## Quality metrics

The CI builds and tests on Linux and Windows, then measures coverage and lines of code on every push to `main` and every pull request. Both appear in the run summary.

Coverage comes from the unit tests only, listed in `Avala.UnitTests.slnf`. The architecture tests run without instrumentation, because the coverage tooling rewrites the code they inspect. Generated code is excluded from the figures.

```
dotnet tool restore
dotnet test --solution Avala.UnitTests.slnf --coverage --coverage-output-format cobertura --results-directory TestResults
dotnet tool run reportgenerator -reports:"TestResults/*.cobertura.xml" -targetdir:TestResults/report -reporttypes:"TextSummary;Html" -assemblyfilters:"+Avala.*;-*.Tests" -filefilters:"-*.g.cs"
dotnet run scripts/metrics.cs
```

Lines of code are physical lines of C#, counted the same way as the reference figure for T3 Code: about 907,000 lines of non-test TypeScript at commit `a4c9494b0`, on 2026-10-08. The goal is a better product in no more than 15% of that.
