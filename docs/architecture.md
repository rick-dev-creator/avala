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
| `src/Modules/<Module>/Avala.<Module>` | Module core: domain, application and view models. Everything is `internal`. |
| `src/Modules/<Module>/Avala.<Module>.UI` | Module views and its single public type: the plugin entry. |
| `src/Modules/<Module>/Avala.<Module>.Contracts` | Optional public contracts other modules may depend on. |
| `src/Modules/Simulator/Avala.Simulator` | A provider plugin that plays scripted Claude Code sessions through the public agent contracts only, for demos and tests without tokens. Its plugin entry lives in the core, since it has no views. |
| `tests/Avala.ArchitectureTests` | The rules below, enforced on every build. |
| `tests/Avala.ArchitectureTests.Fixtures` | A compliant sample module and a module that breaks every rule on purpose. |
| `tests/Avala.Testing` | Helpers shared by the test projects: result assertions, a recording bus, a scripted agent provider, temporary folders and git repositories, generated diagrams. |
| `tests/Avala.<Project>.Tests` | Unit tests. |
| `tests/Avala.Integration.Tests` | End-to-end tests that compose the real modules through their public plugin entries, over a real git repository and SQLite. Not part of `Avala.UnitTests.slnf`. |

## Rules

Enforced by `tests/Avala.ArchitectureTests`:

- The host references only the SDK, the runtime and the shell, and depends on no module.
- A module depends only on the SDK, its own projects and other modules' `Contracts`.
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
- The DDD and layer rules of the [core design](design/core.md#architecture-rules-to-add).
- Only `Infrastructure` types of the modules depend on `Microsoft.EntityFrameworkCore`.
- Database work never runs on the UI thread. Every `Infrastructure` type that declares a field whose type derives from `DbContext` must be declared in a source file that calls `Task.Run`. The rule checks that the call is present in that file, not that every database operation goes through it; the stores route every operation through a single `RunAsync` that awaits `Task.Run`, as the [core design](design/core.md#sqlite-and-blocking) requires.
- The architecture tests reference every source project, so no project escapes the rules.

Enforced by the compiler through `BannedSymbols.txt` and the threading analyzers:

- Nothing blocks a thread: no `Thread.Sleep`, `Wait`, `Result`, `GetResult`, synchronous waits or synchronous file I/O.
- No `async void`.
- No direct `Fire` on a Stateless machine: transitions go through the guarded `TryFire`.

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

A project becomes a plugin with `<AvalaPlugin>true</AvalaPlugin>`. Its build output is copied to `artifacts/plugins/<AssemblyName>`. The host loads every folder there, or the folder named by `AVALA_PLUGINS_PATH`, each in its own load context, while sharing the SDK and Avalonia with the host.

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
