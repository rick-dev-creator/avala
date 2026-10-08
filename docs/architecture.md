# Architecture

Avala is a modular monolith. The host knows nothing about the features it runs: every module is a plugin discovered at runtime.

## Layout

| Path | Role |
| --- | --- |
| `src/Avala.Sdk` | Public contracts for plugins. No Avalonia, no other Avala dependency. |
| `src/Avala.Sdk.UI` | Public contracts for plugin views. Depends only on the SDK. |
| `src/Avala.Shell` | Shell view models. Depends only on the SDK. |
| `src/Avala.Host` | Avalonia application, composition root and plugin loader. References only the SDK and the shell. |
| `src/Modules/<Module>/Avala.<Module>` | Module core: domain, application and view models. Everything is `internal`. |
| `src/Modules/<Module>/Avala.<Module>.UI` | Module views and its single public type: the plugin entry. |
| `src/Modules/<Module>/Avala.<Module>.Contracts` | Optional public contracts other modules may depend on. |
| `tests/Avala.ArchitectureTests` | The rules below, enforced on every build. |
| `tests/Avala.<Project>.Tests` | Unit tests. |

## Rules

Enforced by `tests/Avala.ArchitectureTests`:

- The host references only `Avala.Sdk`, `Avala.Sdk.UI` and `Avala.Shell`, and depends on no module.
- A module depends only on the SDK, its own projects and other modules' `Contracts`.
- Module cores expose no public type. A module UI exposes exactly one public type, its `IPlugin` entry.
- Every module ships exactly one plugin project.
- View models live in assemblies that do not reference Avalonia.
- Every `XViewModel` has an `XView` and the other way around. Views are resolved view-model-first through the view registry.
- Code-behind holds nothing but a constructor calling `InitializeComponent()`.
- Every class is `sealed`. Only Avalonia types may be inherited, so views and the application are the sole subclasses.
- No comments, in C# or in XAML.
- No type spans more than 600 lines, counting every part of a partial type.
- The shell and every module core have a unit test project.
- The architecture tests reference every source project, so no project escapes the rules.

Enforced by the compiler through `BannedSymbols.txt` and the threading analyzers:

- Nothing blocks a thread: no `Thread.Sleep`, `Wait`, `Result`, `GetResult`, synchronous waits or synchronous file I/O.
- No `async void`.

## Plugins

A project becomes a plugin with `<AvalaPlugin>true</AvalaPlugin>`. Its build output is copied to `artifacts/plugins/<AssemblyName>`. The host loads every folder there, or the folder named by `AVALA_PLUGINS_PATH`, each in its own load context, while sharing the SDK and Avalonia with the host.

## Testing

Unit tests are the default. Integration tests are added only when a behavior cannot be verified otherwise.

```
dotnet build Avala.slnx
dotnet test --solution Avala.slnx
```
