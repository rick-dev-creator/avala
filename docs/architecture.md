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
| `tests/Avala.ArchitectureTests` | The rules below, enforced on every build. |
| `tests/Avala.ArchitectureTests.Fixtures` | A compliant sample module and a module that breaks every rule on purpose. |
| `tests/Avala.Testing` | Helpers shared by the test projects: result assertions, generated diagrams. |
| `tests/Avala.<Project>.Tests` | Unit tests. |

## Rules

Enforced by `tests/Avala.ArchitectureTests`:

- The host references only the SDK, the runtime and the shell, and depends on no module.
- A module depends only on the SDK, its own projects and other modules' `Contracts`.
- A module exposes exactly one public type outside its `Contracts`: its `IPlugin` entry.
- `InternalsVisibleTo` targets only the project's own module and its test project. The shell and the runtime may also open to the host.
- View models live in assemblies that do not reference Avalonia.
- Every `XViewModel` has an `XView` and the other way around. Views are resolved view-model-first through the view registry.
- Code-behind holds nothing but a constructor calling `InitializeComponent()`.
- Every constructor parameter of a view model is an interface: view models delegate their commands to injected services that tests can replace.
- Every class is `sealed`. Only Avalonia types may be inherited, so views and the application are the sole subclasses.
- No comments, in C# or in XAML.
- No type spans more than 600 lines, counting every part of a partial type.
- Every script is C#, and CI steps only invoke `dotnet`.
- The SDK, the runtime, the shell and every module core have a unit test project.
- The DDD and layer rules of the [core design](design/core.md#architecture-rules-to-add).
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
