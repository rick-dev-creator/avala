# Architecture

Avala is a modular monolith. The host knows nothing about the features it runs: every module is a plugin discovered at runtime.

## Layout

| Path | Role |
| --- | --- |
| `src/Avala.Sdk` | Public contracts for plugins, including the ports the host implements for them, such as `IUiDispatcher` and `IFileOpener`. No Avalonia, no other Avala dependency. |
| `src/Avala.Sdk.UI` | Public contracts for plugin views. Depends only on the SDK. |
| `src/Avala.Shell` | Shell view models. Depends only on the SDK. |
| `src/Avala.Runtime` | Runtime services shared by every module: the event bus, the process runner and the process trees, with their platform containment in `Containment`. Depends only on the SDK. |
| `src/Avala.Host` | Avalonia application, composition root and plugin loader. References only the SDK, the runtime and the shell. |
| `src/Modules/<Module>/Avala.<Module>` | Module core: domain, use cases, infrastructure and view models, in feature folders. Everything is `internal`. |
| `src/Modules/<Module>/Avala.<Module>.UI` | Module views and its single public type: the plugin entry. |
| `src/Modules/<Module>/Avala.<Module>.Contracts` | Optional public contracts other modules may depend on. |
| `src/Modules/Simulator/Avala.Simulator` | A provider plugin that plays scripted Claude Code sessions through the public agent contracts only, for demos and tests without tokens: it resumes its conversations, draws through the injected canvas tool, calls the harness tools the harness executes, several at once if a scenario says so, and waits for their results, delegates as an orchestrator, asks questions and plan approvals through forms, reports the account of each connection and replays recorded sessions. Its plugin entry lives in the core, since it has no views. |
| `src/Modules/Simulator/Avala.Simulator.Workload` | A tiny program the simulator starts as its real child processes, with the `dotnet` host, so its scenarios run the same on every platform: it works, prints a variable, listens on its leased port, spawns a detached child or waits until the harness is gone. Top-level statements only, so it declares no type; its tests start it as a process. |
| `src/Modules/Recording/Avala.Recording` | A plugin that decorates every agent provider and, when `recording.json` in the data folder enables it, records each session as a file the simulator replays. No contracts and no views. |
| `src/Modules/Workbench/Avala.Workbench` | The module of the user interface: the job board and the conversation projection, in its application layer, and every view model of the main window, built on other modules' `Contracts` only, see [the Workbench](design/core.md#workbench). No Avalonia. |
| `src/Modules/Workbench/Avala.Workbench.UI` | The Workbench's views and its plugin entry, which registers the board, the view models, the main window as the shell's first page and the global pages after it. |
| `tests/Avala.ArchitectureTests` | The rules below, enforced on every build. |
| `tests/Avala.ArchitectureTests.Fixtures` | A compliant sample module and a module that breaks every rule on purpose. |
| `tests/Avala.Testing` | Helpers shared by the test projects: result assertions, a recording bus, a scripted agent provider whose identity, capabilities, resume, account and launches a test chooses, process trees that record what they start and kill it, the workload program's commands, temporary folders and git repositories, committed rule files behind `IBaseFiles`, event watches with a safety timeout, generated diagrams, the regression recordings, and `TestUiDispatcher`, a UI thread of its own with its synchronization context, which runs what view models dispatch and lets a test await a condition on their state. |
| `tests/recordings` | Recorded sessions committed as regression tests, each with its expected outcome, see the [core design](design/core.md#regression-fixtures). Refresh the ones recorded from the simulator with `AVALA_UPDATE_RECORDINGS=1 dotnet test --solution Avala.slnx`. |
| `tests/Avala.<Project>.Tests` | Unit tests. |
| `tests/Avala.Integration.Tests` | End-to-end tests that compose the real modules through their public plugin entries, over a real git repository and SQLite. Not part of `Avala.UnitTests.slnf`. |
| `tests/Avala.Host.Tests` | Simulation tests of the real application: the composition root built from the published plugin folder, driven by the simulator over a real git repository, and its view models driven the way a view binds them. Not part of `Avala.UnitTests.slnf`. |

## Screaming architecture

Inside a module, folders and namespaces are named after what the code does: its domain model, its use cases and the infrastructure they reach. Never after technical layers. Namespaces follow folders. The Jobs core reads like this:

| Folder | Holds | Layer |
| --- | --- | --- |
| `Jobs` | The `Job` aggregate, its attempts, lifecycle, value objects, error enum and domain events | Domain |
| `Submission` | Creating and submitting a job, and the `IJobs` entry other modules call | Application |
| `Launching` | Preparing the workspace, from a checkpoint of its parent for a child job, opening the agent session and sending the instruction, and opening a held job's session again when a human continues it | Application |
| `TurnChecks` | Checking a finished turn against the completion gates, and holding a job whose session was lost | Application |
| `Recovery` | Resuming active jobs at startup | Application |
| `Holding` | Holding a running job for a typed reason and halting its agent session | Application |
| `Ledger` | Storing a job and announcing its progress, with the `IJobStore` port, and `JobQueues`, which runs the work on each job in order | Application |
| `Review` | The commands a human gives a job that waits for them, continuing, approving, sending back and discarding, and `Approvals`, which delivers an approved job through its repository's strategy, or a child job into its parent's worktree | Application |
| `Delivery` | The approval strategies the core ships, `keep` and `merge` | Application |
| `Catalog` | `JobCatalog`, the queries of jobs for the views, their histories, children and trees, behind `IJobCatalog` | Application |
| `Storage` | The EF Core store behind `IJobStore` | Infrastructure |
| `JobFiles` | `JobFileReader`, which reads the connection and the approval strategy a repository prefers from `.avala/jobs.json` of the base commit, behind the `IRepositoryDefaults` port of `Launching`, and accepts the `autopilot` and `delegation` sections other modules read | Infrastructure |

Jobs has no view model: the job list, the conversation and every other screen live in the [Workbench](design/core.md#workbench), which reads Jobs through its contracts. `Avala.Jobs.UI` keeps only the plugin entry: moving it into the core would rename the plugin folder, and the `Avala.Jobs.UI` folder an earlier build left in `artifacts/plugins` would then load Jobs a second time.

The other cores follow the same idea: Agents has `Turns`, `Sessions`, which also opens each session's process tree, `Connections`, which resolves the connection a session opens on, `ConnectionFiles`, which reads `connections.json`, and `Credentials`, its first credential sources; Workspaces has `Workspaces`, `Provisioning`, which also reconciles the worktree root with the store, `BaseFiles`, `Changes`, the diff of a workspace and the merge of its work into its base branch, `Git` and `Storage`; Resources has `Usage` and `Leases`, its domain, `Tracking`, `Sampling`, `Reaping`, `Leasing` and `Housekeeping`, and `Settings` and `Disks`, its infrastructure; Canvas has `Canvases`, `Drawing`, `Gallery`, `Streaming` and `Throttling`; Observability has `Usage`, `Tracking`, `Metrics` and `Storage`, its stored history; Verification has `Checks`, `Verifying` and `Evidence`; Permissions has `Policies`, `Governance`, `Answering` and `PolicyFiles`; Supervision has `Watching`, `Supervising`, `Settings` and `Storage`, its stored interventions; Budgets has `Caps`, which also carves a child's budget out of its parent's, `Enforcement`, `Admission`, which limits the jobs running at once, `BudgetFiles` and `Storage`, its stored interventions and carves; Autopilot has `Loops`, `Evidence` and `Backlogs`, its domain, `Looping`, the loops and their commands, `Approving`, the automatic approval on clean evidence, `Sourcing`, the job sources, and `FollowUps`, the tool agents propose follow-ups through, and `RepositoryFiles` and `Storage`, the rule and backlog files it reads and the marks it keeps; Delegation has `Policy`, its domain, the declared rules, routing and refusals, `Delegating`, the `delegate` tool, its policy and the desk that handles its calls, `Reporting`, which integrates a settled child and gathers its evidence, `Records`, the delegation records and the results returned to the orchestrator, and `RepositoryFiles`, the `delegation` section it reads; the simulator has `Scenarios`, `Playback`, `FileSystem`, `Recordings`, where it reads the recordings it replays, and `Workloads`, which starts the workload program through the session's launcher; Recording has `Recordings`, the domain of a recording and its redaction, `Capturing`, the decorator and its ports, `Settings` and `Storage`, the files and their format; the Workbench has `Timeline`, the conversation projection, `Board`, the jobs as the views see them and the handler that keeps them, `Steering`, the composer's commands, `Replies`, the answers to permissions and forms, `Reviewing`, the facts and commands of the review sheet, `Inspection`, the facts of the inspector, and, for the global pages, `Following`, `Fleet`, `Spending`, `RepositoryRules`, `Machine`, `Upkeep` and `Submitting`, its application layer, and `Navigation`, `Sidebar`, `Conversation`, `Cards`, `Review`, `Decisions`, `Inspector`, `Presenting`, `Overview`, `Usage`, `Settings`, `Resources` and `NewJob`, its view models. Plugin entries stay at the root of their project, and `Contracts` projects keep their own names.

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
- Code-behind holds only presentation concerns: it references no service, module contract or other view model, takes no constructor dependencies, and stays under 400 lines. A XAML view stays under 800 lines and a view model under 400.
- Every view model has an interface its view binds to with compiled bindings (`x:DataType`), and a design-time implementation the view declares as its design-time `DataContext`, so every view renders in the designer.
- Pages are composed of named regions filled with components registered by plugins.
- No class takes more than four constructor dependencies. Records holding data are exempt.
- Every class is `sealed`. Only framework types may be inherited: Avalonia types for views and the application, and EF Core's `DbContext` for each module's database.
- No non-private member exposes a nullable type in its signature: properties, fields, parameters and return types, generic arguments such as `Task<T?>` included. Absence is an `Option<T>`. Exempt: members that implement framework interfaces or override framework members, such as Avalonia's `IDataTemplate`; properties of view models, since an empty selection is `null` in Avalonia; the `Optional` bridge; unconstrained generic type parameters; and generated code.
- No comments, in C# or in XAML.
- No type spans more than 600 lines, counting every part of a partial type.
- Every script is C#, and CI steps only invoke `dotnet`.
- The SDK, the runtime, the shell and every module core have a unit test project.
- No test is lost to an attribute the runner cannot read. Every custom attribute of every built test assembly is decoded from its metadata, constructor signature and arguments included, enum values and `typeof` arguments too, and none may name a type of an `Avala.*` assembly that is missing from the test project's output folder. The runner reads test attributes before any fixture loads the plugin folder, so such a type cannot load, and the runner drops the tests it carries. The rule reads the test assemblies the build produced, so it runs after `dotnet build Avala.slnx`, as `dotnet test --solution` and the CI do.
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

## Extending Avala

A new capability, such as GitHub, Linear or another way to isolate work than git worktrees, arrives as a new module under `src/Modules`, without touching the core. It talks to the rest of Avala in exactly three ways:

| Mechanism | Use it to | Examples |
| --- | --- | --- |
| Integration events on the bus | React to what happened | A GitHub plugin opens a pull request when `JobProgressed` reports an approved job. A Linear plugin moves an issue when a job starts or reaches review. A notifier reacts to `JobHeld`. |
| Contracts injected through DI | Ask for an answer or ask for an action | A GitHub plugin reads a job's branch through `IWorkspaces`. A Linear plugin submits a job through `IJobs.SubmitAsync`. |
| Extension points the core defines and plugins implement | Let the core use something it does not know | `IAgentProvider`, `IAgentProviderDecorator`, `ICredentialSource`, `ICompletionGate`, `IJobAdmission`, `IApprovalStrategy`, Autopilot's `IJobSource` and the runtime's `IProcessEnvironment` today, and the harness tools a module registers, which Agents hands to every provider that accepts tools, the module answering the calls of the tools it executes: Jobs delivers an approved job through the strategy its repository names, so a GitHub plugin that opens a pull request is one more strategy; Jobs runs every registered gate, so a gate that waits for GitHub's CI needs no change in Jobs; Agents starts every provider through every registered decorator, which is how the Recording plugin records sessions; Agents resolves a connection's credential through the source it names, so a keychain plugin adds a source without touching Agents or an adapter; Jobs launches a submitted job once every admission let it, which is how Budgets limits the jobs running at once; and every process tree gets the variables every environment contributor gives its folder, which is how Resources hands out port leases. An autopilot loop takes its tasks from every registered job source, so GitHub or Linear issues arrive as one more source; Autopilot itself is a plugin built on these mechanisms only, its follow-up tool included, and so is Delegation, whose `delegate` tool submits child jobs through `IJobs` and reports their evidence back. Later, other isolation strategies, chosen by declared capabilities. |

- Events state facts. Never ask for something over the bus and wait for a reply: that hides a dependency a contract would make explicit.
- A plugin depends only on the SDK and other modules' `Contracts`, never on their internals, as the [rules](#rules) enforce.
- A plugin owns its data, in its own database. The mapping between a Linear issue and its job lives in the Linear plugin; the core never learns what an issue is.
- A new extension point follows the agent contract's discipline: the interface in a `Contracts` project, a simulated or fake implementation, and tests every implementation must pass.

### Composing the interface across modules

The interface follows the same rule as the rest: a module never references another module's internals. A screen made of several modules' parts is composed view model first, in three ways, preferred in this order.

| Mechanism | Use it when | Example |
| --- | --- | --- |
| A region and its context | A page shows parts it does not need to know, about the thing it has in focus | The shell declares `Inspector`; Workbench, Resources and Observability each register a section; the shell sets the region's context to the selected job and every section shows that job |
| A UI message | Something that happened in one region must reach parts elsewhere | "Open the review of this job", published by a sidebar row and handled by the page that hosts the review |
| A typed view model from another module's contracts | A view model must host and drive another module's component | The review hosts Observability's job usage meter, created through a factory for the job under review |

- **Regions.** A page declares named regions; a plugin registers its view models into them through the registrar, with an order. A region holds view models, never views: the view registry resolves each one's view from its module's `.UI` assembly. A region activates and deactivates the view models it holds.
- **Region names are typed** constants in `Avala.Sdk.UI`, never strings, and an architecture test checks that every region a plugin registers into exists.
- **Region context first.** A page sets its region's context, such as the selected job, and every view model in the region receives it through `IRegionAware`. Sections never subscribe to selection messages.
- **UI messages** travel over an injected `IMessenger`. A message is a small immutable record in the publishing module's `Contracts`, under `Presentation`, listed in the data catalog like any other data. It states what happened in the interface and never replaces a command to the core.
- **Typed composition** is the exception. The owning module exposes the view model's interface and a factory in its `Contracts`, under `Presentation`; the interface depends only on `INotifyPropertyChanged` and `ICommand` from .NET, so contracts stay free of any UI framework. The implementation stays internal to its module, and its view stays in its module's `.UI` assembly.
- **Design time.** Regions have design-time content too, so the shell renders in the designer filled with design-time view models.

We take Prism's concepts, regions, region context and the event aggregator, and implement the minimum Avala needs in `Avala.Sdk.UI` and the shell, rather than depending on Prism itself.

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

A project becomes a plugin with `<AvalaPlugin>true</AvalaPlugin>` and `<EnableDynamicLoading>true</EnableDynamicLoading>`. Every module entry is one: Agents, Workspaces, the Jobs UI, Canvas, Observability, Verification, Permissions, Supervision, Budgets, Resources, Autopilot, Delegation, Recording, the Workbench UI and the simulator. Its build output is copied to `artifacts/plugins/<AssemblyName>`. The host loads every folder there, or the folder named by `AVALA_PLUGINS_PATH`, in folder name order.

All plugins share the host's default load context, so every assembly is loaded once:

- An assembly the host already ships, such as the SDK, the runtime, dependency injection or Avalonia, comes from the host.
- Any other assembly, such as a module's `Contracts`, EF Core or a native library like SQLite, is resolved from the plugin folders through each plugin's `.deps.json`, the first time anyone asks for it.
- A contract therefore has one type identity for every module: the `IAgentProvider` the simulator implements is the one Agents asks for.

Isolating each plugin in its own load context would load a `Contracts` assembly once per plugin and break every cross-module contract. Isolation can come back, as one shared context for module contracts plus private contexts, if a third-party plugin ever needs a dependency that conflicts with another plugin.

The host knows no module: the loader only scans folders and instantiates the `IPlugin` types it finds. `tests/Avala.Host.Tests` composes the application from the published folder exactly as the host does at startup. Like the host, it does not ship the module contracts it compiles against, so they come from the plugin folder, which its assembly fixture loads before any test runs. Test attributes are read before that fixture, so they must not name a contract type, such as an enum value of `Autonomy` in an `InlineData` or a `typeof` of a contract in a class attribute: the runner cannot load it and drops the tests of that class. Depending on the runner, the run has reported that as class cleanup failures or stayed green with fewer tests, so an [architecture rule](#rules) rejects such an attribute in every test assembly, whatever the runner does with it; pass the name of an enum value and parse it in the test instead. The same holds for a static field whose type is a contract struct, such as `ConnectionName`: discovery loads every exported test class, and the field's type with it, before the fixture runs, so the whole assembly fails to load. No rule checks it yet; use a property, which is resolved only when called.

## Testing

Unit tests are the default. Integration tests are added only when a behavior cannot be verified otherwise.

A test that starts real processes reaps them before it deletes the folders they ran in: on Windows a running process's current directory cannot be deleted. Even then, Windows releases a killed process's handles, and its console host's, a moment after the process the test waited for has exited, with nothing to await, so `TemporaryFolder.DisposeAsync` retries a failed deletion every 100 ms for up to five seconds. Tests whose processes ran in a temporary folder dispose it with `await using`.

View models are tested twice. Their unit tests, next to the module's other tests, give them fakes of the contracts they use. The host simulation tests compose the real application with a `TestUiDispatcher` in place of Avalonia's, resolve the shell's page, activate it and drive it through `Bound`, which reads properties and runs commands by the names a view binds, since the view models are internal to their module. Every read and every command runs on the test's UI thread, and a test awaits the state it expects with `UntilAsync`, never a delay.

Every `XViewModel` needs its `XView`, even before the views of phase 10 are designed: a view model gets a plain placeholder view in the same change, bound to what it exposes, which phase 10 restyles. An item a view model lists, such as a sidebar row or a timeline entry, is a view model with its own view too, resolved through the view registry, so each kind of entry has one view model and one template.

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
