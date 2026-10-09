# Architecture

Avala is a modular monolith. The host knows nothing about the features it runs: every module is a plugin discovered at runtime.

## Layout

| Path | Role |
| --- | --- |
| `src/Avala.Sdk` | Public contracts for plugins, including the ports the host implements for them, such as `IUiDispatcher` and `IFileOpener`. No Avalonia, no other Avala dependency. |
| `src/Avala.Sdk.UI` | Public contracts for plugin views and the `ViewRegistry`, the data template that resolves a view model's view, by its exact type or by an interface it implements. Depends only on the SDK and Avalonia. |
| `src/Avala.Shell` | Shell view models and the regions of the main window. Depends only on the SDK and the shared view models. |
| `src/Shared/Avala.Components` | Shared view models reused across modules, such as the status dot, status pill, meter and keycap hint, each with its interface and design-time implementation. No UI framework. |
| `src/Shared/Avala.Components.UI` | Their views, in a folder per view, and Avala's [design system](#design-system): the theme, the fonts and the icons, and the presentation pieces every module's views share: `StreamingText`, the text a streaming reply grows in, and the converters of `States`. |
| `src/Avala.Runtime` | Runtime services shared by every module: the event bus, the process runner and the process trees, with their platform containment in `Containment`. Depends only on the SDK. |
| `src/Avala.Host` | Avalonia application, composition root and plugin loader. References only the SDK, the runtime and the shell. |
| `src/Modules/<Module>/Avala.<Module>` | Module core: domain, use cases, infrastructure and view models, in feature folders. Everything is `internal`. |
| `src/Modules/<Module>/Avala.<Module>.UI` | Module views and its single public type: the plugin entry. |
| `src/Modules/<Module>/Avala.<Module>.Contracts` | Optional public contracts other modules may depend on. |
| `src/Modules/Simulator/Avala.Simulator` | A provider plugin that plays scripted Claude Code sessions through the public agent contracts only, for demos and tests without tokens: it resumes its conversations, draws through the injected canvas tool, calls the harness tools the harness executes, several at once if a scenario says so, and waits for their results, delegates as an orchestrator, asks questions and plan approvals through forms, reports the account of each connection and replays recorded sessions. Its plugin entry lives in the core, since it has no views. |
| `src/Modules/Simulator/Avala.Simulator.Workload` | A tiny program the simulator starts as its real child processes, with the `dotnet` host, so its scenarios run the same on every platform: it works, prints a variable, listens on its leased port, spawns a detached child or waits until the harness is gone. Top-level statements only, so it declares no type; its tests start it as a process. |
| `src/Modules/Recording/Avala.Recording` | A plugin that decorates every agent provider and, when `recording.json` in the data folder enables it, records each session as a file the simulator replays. No contracts and no views. |
| `src/Modules/Workbench/Avala.Workbench` | The module of the user interface: the job board and the conversation projection, in its application layer, and every view model of the main window, built on other modules' `Contracts` only, see [the Workbench](design/core.md#workbench). No Avalonia. |
| `src/Modules/Workbench/Avala.Workbench.UI` | The Workbench's views, a folder per view, and its plugin entry, which registers the board, the view models, the jobs page as the shell's first page and the global pages after it, the job list into the shell's `Sidebar` region, the toolbar with the pending decisions, their popover and a new job into `Toolbar`, the inspector's sections into `Inspector` and the resource indicator into `SidebarFooter`. |
| `src/Modules/Workbench/Avala.Workbench.Contracts` | The Workbench's UI messages, under `Presentation`: `JobSelected`, published when a job is chosen in the sidebar. |
| `tests/Avala.ArchitectureTests` | The rules below, enforced on every build. |
| `tests/Avala.ArchitectureTests.Fixtures` | A compliant sample module and a module that breaks every rule on purpose. |
| `tests/Avala.Testing` | Helpers shared by the test projects: result assertions, a recording bus, a scripted agent provider whose identity, capabilities, resume, account and launches a test chooses, process trees that record what they start and kill it, the workload program's commands, temporary folders and git repositories, committed rule files behind `IBaseFiles`, `PluginComposition`, which registers one plugin beside stand-ins for the contracts it consumes and builds it with validation so a module's tests prove its registration resolves, event watches with a safety timeout, generated diagrams, the regression recordings, and `TestUiDispatcher`, a UI thread of its own with its synchronization context, which runs what view models dispatch and lets a test await a condition on their state. |
| `tests/recordings` | Recorded sessions committed as regression tests, each with its expected outcome, see the [core design](design/core.md#regression-fixtures). Refresh the ones recorded from the simulator with `AVALA_UPDATE_RECORDINGS=1 dotnet test --solution Avala.slnx`. |
| `tests/Avala.<Project>.Tests` | Unit tests. |
| `tests/Avala.Integration.Tests` | End-to-end tests that compose the real modules through their public plugin entries, over a real git repository and SQLite. Not part of `Avala.UnitTests.slnf`. |
| `tests/Avala.Host.Tests` | Simulation tests of the real application: the composition root built from the published plugin folder, driven by the simulator over a real git repository, its view models driven the way a view binds them, and the shell rendered headless. Not part of `Avala.UnitTests.slnf`. |
| `tests/Avala.Testing.UI` | Helpers for headless view scripts: `HeadlessApp`, the application with Avala's theme, included from its XAML as the host's `App` does, and a view registry, `HeadlessUi` and `ViewScript`. |
| `tests/Avala.ArchitectureTests.Fixtures.UI` | The views of the compliant and violating fixtures, and a view model that knows Avalonia. |

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

- The host references only the SDK, the runtime, the shell and the shared components, and depends on no module. The shared view models depend only on the SDK, their views only on the SDK, `Avala.Sdk.UI` and their view models, and every module may depend on both.
- A module depends only on the SDK, its own projects and other modules' `Contracts`.
- A module's `Contracts` depend only on the SDK and other modules' `Contracts`: identifiers such as `JobId` or `SessionId` are a vocabulary the modules share, never their internals.
- A module exposes exactly one public type outside its `Contracts`: its `IPlugin` entry.
- `InternalsVisibleTo` targets only the project's own module and its test project. The shell and the runtime may also open to the host and the host's tests.
- The [view rules](#view-rules) below.
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
- Nothing waits on time instead of an event, in `src/`, `tests/` or `scripts/`: no `Thread.Sleep`, `Task.Delay`, `SpinWait`, `Thread.SpinWait`, `System.Threading.Timer`, `System.Timers.Timer` or `PeriodicTimer`. A delay hides a missing event and makes tests slow and nondeterministic. Await the event or signal that states the thing happened: an integration event, a component's refreshed signal, a `TaskCompletionSource` or a channel; to wait until cancelled, await `UntilCancelledAsync` on the token. Behavior that is genuinely about time, such as the simulator's pace or a sampling interval, uses `TimeProvider.CreateTimer`, testable with `FakeTimeProvider`. Each violation is reported with its file, line and these alternatives. The only exceptions are the `RS0030` rows of the [exceptions table](#analyzer-exceptions) that name a file and a symbol, which the rule reads, and a documented exception that no longer occurs fails the rule too.

Enforced by the compiler through `BannedSymbols.txt` and the threading analyzers:

- Nothing blocks a thread: no `Thread.Sleep`, `Wait`, `Result`, `GetResult`, synchronous waits or synchronous file I/O.
- Nothing waits on time, in source and test projects alike: `Thread.Sleep`, every `Task.Delay` overload, `SpinWait`, `Thread.SpinWait`, the `System.Threading.Timer` constructors, `System.Timers.Timer` and `PeriodicTimer` are banned with a message that names the alternatives above.
- No `async void`.
- No direct `Fire` on a Stateless machine: transitions go through the guarded `TryFire`.
- No coordination primitive in `src/`: `src/BannedSymbols.Concurrency.txt` bans the types above for every source project, so a violation fails the build before the architecture tests run. A `lock` on a plain object names no banned type, so only the architecture rule catches it.

Enforced by the compiler through the code quality analyzers, on every project and script:

- [SonarAnalyzer.CSharp](https://github.com/SonarSource/sonar-dotnet), open source under the LGPL, with its default rules, the Sonar way, as warnings and so as errors. Three maintainability rules are added to the defaults, with their limits in `SonarLint.xml`: cognitive complexity of at most 15 per method and 3 per property accessor (`S3776`), at most 3 nested control flow statements (`S134`) and at most 80 lines per method (`S138`). The rules that conflict with a convention are off and listed among the [analyzer exceptions](#analyzer-exceptions). Expression complexity (`S1067`) is left off, since it rejects the guard chains `S3358` is off for, and so are Sonar's own cyclomatic complexity (`S1541`) and coupling (`S1200`), which the .NET rules below already measure.
- The .NET code metrics rules, with their limits in `CodeMetricsConfig.txt`: cyclomatic complexity of at most 20 per member (`CA1502`), a maintainability index of at least 20 per member and type, the start of Visual Studio's green band (`CA1505`), and class coupling of at most 40 types per member and 95 per type, the analyzer's defaults (`CA1506`).

Every rule is checked against the production modules, a compliant fixture module and a violating fixture module. A rule must pass on the first two and find exactly the expected violations in the third, so it can never pass vacuously.

### View rules

`tests/Avala.ArchitectureTests/Views` checks every view and view model of `src`, the compliant fixture and the violating fixture. The fixtures' views live in `Avala.ArchitectureTests.Fixtures.UI`; the violating views are kept out of the XAML compiler, since a broken view would not build, and read as files. Each failure names the type or the file and line, and says how to fix it. A view model is a class named `XViewModel`; its interface is `IXViewModel` and its design-time implementation `DesignXViewModel`, which no other rule counts as a view model.

| Rule | Checks |
| --- | --- |
| `ViewForEveryViewModel`, `ViewModelForEveryView` | Every `XViewModel` has an `XView` and the other way around. |
| `CompiledBindings` | Every view declares `x:DataType`; compiled bindings are the default in every project, so a broken binding fails the build. |
| `DesignTimeDataContext` | Every view declares its design-time `DataContext`, as `<Design.DataContext>` or `d:DataContext`. |
| `ViewModelInterfaces`, `DesignTimeImplementations` | Every view model implements `IXViewModel`, and every `IXViewModel` has a `DesignXViewModel`. The interfaces do not extend `INotifyPropertyChanged`: CommunityToolkit's `[INotifyPropertyChanged]` refuses a class whose interfaces already declare it, and compiled bindings still observe the class. |
| `NoUiFrameworkInViewModels` | No view model, view model interface or design-time implementation lives in an assembly that references a UI framework: Avalonia, MAUI, Uno, WPF, Windows Forms, WinUI, Blazor's components, Terminal.Gui, Xamarin.Forms, Eto or GTK. |
| `PresentationOnlyCodeBehind` | A view's code-behind uses only `System` and `Avalonia` namespaces, names no view model, takes no constructor parameters and declares one type. |
| `ComponentSize` | A XAML file has at most 800 lines, a code-behind at most 400 and a view model at most 400. |
| `DeclaredRegions` | Only a static class named `<Page>Regions` creates a `RegionName`, so every region a plugin registers into is a declared one. |
| `NoParentOrSiblingReferences` | Over the graph of view models that reference each other through their fields, within one assembly, no view model references its parent or a sibling under the same parent. |
| `ScriptedAcceptanceTests` | Every view model and every view has a test class named after it with the suffix `Scripts`, such as `MeterViewModelScripts` and `MeterViewScripts`, anywhere under `tests`. |
| `ThemeResourcesOnly` | No view hard-codes a color, brush, font size, family or weight, corner radius or shadow, in an attribute or a style setter: it uses the theme's resources or typography classes. `Transparent` and a zero radius are allowed. The message names the matching resource when the theme has one, such as "use {DynamicResource AccentBrush} from the theme instead of #8DA2FB". |
| `CommandsNotEventHandlers` | No XAML attribute attaches an event handler, such as `Click` or `PointerPressed`: a user action is a command binding. |
| `NamedIconButtons` | A button whose content is only an icon declares `AutomationProperties.Name`. |
| `TypedRegionReferences` | A XAML attribute that names a region, or holds a declared region's key, uses `{x:Static}` of its typed name, never a string. |

Not enforced yet: a spacing scale, rules on visible text and duplicated styles.

#### No rule is scoped

The Workbench's view models and placeholder views were written before these rules, and `PendingRetrofit` listed, by rule and module, the findings the production test skipped until the retrofit of phase 10: `ViewModelInterfaces`, `DesignTimeDataContext`, `ScriptedAcceptanceTests` and `ThemeResourcesOnly` for the Workbench. The retrofit is done and the list is empty. `TheRetrofitScopeOnlyShrinks` keeps it that way: an entry that finds no violation fails it, and so does an entry that is not part of its `Ceiling`, which is empty too.

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
- **Region names are typed** constants, never strings, and an architecture test checks that every region a plugin registers into exists.
- **Region context first.** A page sets its region's context, such as the selected job, and every view model in the region receives it through `IRegionAware`. Sections never subscribe to selection messages.
- **UI messages** travel over an injected `IMessenger`. A message is a small immutable record in the publishing module's `Contracts`, under `Presentation`, listed in the data catalog like any other data. It states what happened in the interface and never replaces a command to the core.
- **Typed composition** is the exception. The owning module exposes the view model's interface and a factory in its `Contracts`, under `Presentation`; the interface depends only on `INotifyPropertyChanged` and `ICommand` from .NET, so contracts stay free of any UI framework. The implementation stays internal to its module, and its view stays in its module's `.UI` assembly.
- **Design time.** Regions have design-time content too, so the shell renders in the designer filled with design-time view models.

We take Prism's concepts, regions, region context and the event aggregator, and implement the minimum Avala needs in the SDK and the shell, rather than depending on Prism itself.

#### How regions are built

The contracts live in `Avala.Sdk.Regions`, in the framework-free SDK rather than `Avala.Sdk.UI`, because the view models that receive a region's context live in module cores that know no UI framework, and another UI technology reuses them unchanged:

| Type | Role |
| --- | --- |
| `RegionName` | A region's identity. Declared once, as a static property of a static class named `<Page>Regions`; `ShellRegions` declares the main window's `Toolbar`, `Sidebar`, `SidebarFooter`, `Content` and `Inspector`, after the approved window design. |
| `registrar.AddToRegion<TViewModel>(region, order)` | An extension of `IPluginRegistrar` that registers a `RegionContribution`, resolved from the view model the plugin registered in the container. |
| `IRegionAware<TContext>` | A view model that receives the region's context as an `Option<TContext>`: `None` when the region has no context or one of another type. |
| `IRegions` | Sets or clears a region's context, such as the inspector's selected job. |
| `IPresentation` | The signal a component raises with a new `Revision` once it has applied a change, so a test awaits it instead of polling or waiting. |

The shell implements the rest. `Region` holds a region's view models in ascending order, activates and deactivates those that are `IActivatable` and delivers its context. `RegionContexts` implements `IRegions`; it holds no view model, so a view model may depend on it without a cycle in the container, and it hands a context set before the shell exists to the region once attached. `ShellViewModel` builds the five regions from every contribution; its content region holds the pages, those registered as `IPage` and those registered into `ShellRegions.Content`, which must be pages, and shows the selected one. The shell activates its regions and the selected page when it is activated, and raises `Presented` after a page is selected or a region context is delivered. The shell also registers an `IMessenger` for UI messages and listens on it for `PageRequested`, the SDK's message by which a page asks to be shown, such as the jobs page when a job is chosen in the sidebar while another page is open; a request for a page the shell does not hold changes nothing. `ShellView`, in the host, draws the approved main window: the sidebar column with the logo and the `Toolbar` region on one line, the `Sidebar` region under it, and at its foot the `SidebarFooter` region above the page links, each region a list of view models whose views the registry resolves. A page says where it belongs through two members `IPage` gives by default: `Icon`, the key of its glyph in the theme, and `Placement`, `Navigation` to be listed among the page links or `Hidden` for a page reached otherwise, such as the jobs page through its rows, a new job through the toolbar or the resources through their indicator; the links never write an empty selection back, so a hidden page stays shown. The shell shows the sidebar column only when it has content or there are several pages, the inspector only when it has sections and something in focus, `IsInspectorShown`, sliding it in with the gentle spring, so a page opens and closes the inspector by setting and clearing its context, and an empty state when there is no page. `DesignShellViewModel` fills every region with shared components, so the shell renders in the designer.

## Design system

Avala's design system is the approved [visual language](design/ui-brief.md), translated to Avalonia in `src/Shared/Avala.Components.UI/Theme` and included by the application as one style, `AvalaTheme.axaml`, over Fluent:

| File | Holds |
| --- | --- |
| `Tokens.axaml` | Colors and brushes, dark first: surfaces (window `#101114`, panel `#18191D`, float `#212228`), fills, text levels (`#EDEDEF`, `#A3A3AD`, `#80808A`), separators, the accent `#8DA2FB`, attention `#E5A13A` and failure `#EF6461`, and the Fluent keys Avala overrides, such as the focus visual and the toggle switch. The application requests the dark variant; light comes once the visual language is approved. |
| `Metrics.axaml` | Radii (6 control, 8 button, 10 row, 14 card, 20 sheet), the spacing scale (4 to 48), widths, the type scale and the shadows of each material level. |
| `Typography.axaml` | Inter for the interface and JetBrains Mono for code, embedded in `Fonts` with its license, `JetBrainsMono-OFL.txt`, and the text classes of the type scale: `title`, `heading`, `reading`, `body`, `caption`, `section`, `mono`, `medium`, `strong` and the tones. |
| `Materials.axaml` | The three material levels. The window is opaque; panels such as the sidebar and the inspector draw `PanelMaterial`, an acrylic material that shows the platform's translucency, vibrancy on macOS and Mica on Windows, through the window's `TransparencyLevelHint`, and falls back to the solid panel color on Linux or wherever the platform gives none. Floating cards and popovers are solid with their shadow on every platform, since Avalonia blurs only behind a window. |
| `Motion.axaml` | The durations and easings of the design, its springs approximated by the cubic curves its prototype uses, and the presets as classes: `pulse` (working), `spin` and `spin-slow` (checking, a running tool or plan step), `arrive-snappy` (a new entry or card), `arrive-gentle` (the inspector), `fade-in`, `fold-in` (a row opening to its detail), `eased`, `think-dot` with `second` and `third` (the live thinking dots, staggered), `shimmer-window` and `shimmer-text` (the light that sweeps "Thinking") and `caret` (the streaming caret). Streaming text arrives through `StreamingText`, which fades each new chunk in over 260 ms and settles once the reply ends. Reduced motion is not handled yet. |
| `Controls.axaml` | Buttons (default secondary, `primary`, `ghost`, `destructive`, `line` for a conversation's tool and thought lines, `link`, `glyph` for icon buttons, `send`), toggle buttons (`quiet`, `choice` for a form's options), text boxes, check boxes, toggle switches, segmented tabs, scroll bars, list rows, the focus ring, `tag` and `code` borders and icon paths, `solid` and `small` among them. |
| `Icons.axaml` | Vector geometry for every item kind of a conversation (message, reasoning, file edit, command, search, web, MCP, sub-agent, policy) and the window's glyphs. |

`ExperimentalAcrylicBorder` takes its material as a local value: set through a style, Avalonia 12 throws when the window closes.

## Analyzer exceptions

Comments are banned, so every exception to an analyzer is recorded here.

| Rule | Scope | Reason |
| --- | --- | --- |
| `MVVMTK0032` | Everywhere | It recommends inheriting from `ObservableObject`. View models use `[INotifyPropertyChanged]` to favor composition. |
| `CA1000` | Everywhere | `Result<TValue, TError>.Success` and `Failure` are the canonical static factories of a generic result. |
| `VSTHRD003` | Everywhere | It guards against deadlocks under Visual Studio's `JoinableTaskFactory`, which Avala does not use. Awaiting a stored task, such as the event bus loop, is correct here. |
| `CA1716` | Everywhere | It reserves Visual Basic keywords such as `Option`. Avala is C# only, and `Option<T>` is the established name of the pattern. |
| `S108` | Everywhere | Its remedy for a block left empty on purpose, such as a `catch` that swallows an expected cancellation, is a comment that explains it, and comments are banned. |
| `S3358` | Everywhere | A chain of conditional expressions, one guard per line that ends in the success value, is the established way to validate input into a `Result` declaratively, and the rule reads every such chain as nested ternaries. Cognitive complexity, `S3776`, still bounds each chain. |
| `S5034` | Everywhere | It keys on the called method, so two separate calls of the same `ValueTask` method, or one call inside a loop, read as one `ValueTask` consumed twice. `CA2012` checks the real misuse. |
| `CA1506` | `tests/` | A scenario test composes the application through its real contracts on purpose: its coupling measures the scenario, not a design to split. |
| `S3903` | `scripts/` | Scripts are file-based apps with top-level statements, which cannot share a file with a file-scoped namespace, the only form the code style allows. |
| `S4036` | `scripts/` | Scripts run `git` from the runner's `PATH` by design, as the product does through its process runner. |
| `RS0030` | `GuardedTransitions.cs` | The guarded transition helper is the single place allowed to call `Fire`, right after `CanFire`. |
| `RS0030` | xUnit's generated entry point | Third-party generated code that blocks on the test platform's task. |
| `RS0030` | `tests/Avala.Testing/TemporaryFolder.cs`, `Task.Delay` | Windows releases a killed process's handles, and its console host's, a moment after the process exits, and announces it with no event, so `DisposeAsync` retries a failed deletion after a short delay. The timing rule reads this row and allows `Task.Delay` in this file only, nothing else. |
| All analyzers | `Avala.ArchitectureTests.Fixtures` and `Avala.ArchitectureTests.Fixtures.UI` | The fixtures break rules on purpose. |

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

A test that starts real processes reaps them before it deletes the folders they ran in: on Windows a running process's current directory cannot be deleted. Even then, Windows releases a killed process's handles, and its console host's, a moment after the process the test waited for has exited, with nothing to await, so `TemporaryFolder.DisposeAsync` retries a failed deletion every 100 ms for up to five seconds, the one [documented exception](#analyzer-exceptions) to the rule against waiting on time. Tests whose processes ran in a temporary folder dispose it with `await using`.

View models are tested twice. Their unit tests, next to the module's other tests, give them fakes of the contracts they use. The host simulation tests compose the real application with a `TestUiDispatcher` in place of Avalonia's, resolve the shell's page, activate it and drive it through `Bound`, which reads properties and runs commands by the names a view binds, since the view models are internal to their module. Every read and every command runs on the test's UI thread. An asynchronous command is awaited through its own task, `Bound.ExecuteAsync` run by `TestUiDispatcher.RunAsync`, and a page or component that changes on its own states it through `IPresentation`: `TestUiDispatcher.PresentedAsync` checks the expected state at once and again on every `Presented` signal, and reports the last state it saw if it never comes. The pages that follow the core through `LiveFeed` present after every state they show, and `WorkbenchViewModel` after every change of the board it shows.

The simulation runs on no real time. `SimulatedRun` composes the application through `CompositionRoot.Create` with a `FakeTimeProvider`, started at the current time and advancing one tick on every read, so timestamps stay ordered, and replaces the simulator plugin with one whose pace is zero. Behavior about time is driven by the test: a silence window by `SilentForAsync`, which waits until the job runs, its resume token arrived and `IEventFeed.DeliveredAsync` says every handler has handled what was published so far, then advances the clock by the window; a pause until a limit resets by advancing to its end; a sampling interval by advancing one interval after startup. The speed of the machine changes how long a test takes, never what it observes. Tests that start servers lease ports from a range of their own, offset by the test process, since two applications on one machine lease ports independently.

### Scripted acceptance tests

Every finished component and page has scripted acceptance tests, in a class named after it with the suffix `Scripts`, which an [architecture rule](#view-rules) requires. Each test is one acceptance criterion played as the user would: it selects, types, presses a key or invokes a command, then asserts what the user would see. Two helpers write them:

- `ViewModelScript`, in `Avala.Testing`, drives a view model with fakes or its design-time data: `Given(viewModel)`, `When(action)`, `Invoke("SaveCommand")` by the name a view binds, `Then(assertion)`, `ThenNotified(properties)`, which checks the change notifications a view relies on, and `WhenPresentedAsync(action)`, which awaits the component's `Presented` signal.
- `ViewScript`, in `Avala.Testing.UI`, drives a view headless with Avalonia.Headless and Avala's theme: `Show(viewModel)` resolves the view through the registry, `Present(window, viewModel)` opens a window such as the shell, then `Click`, `Type` and `Press` play input, and `TextOf`, `Shows`, `HasClass`, `Find` and `VisibleTexts` read what is on screen. `HeadlessUi`, an assembly fixture, runs each script on Avalonia's thread and closes its windows afterwards; the session is not disposed, since disposing it hangs the runner.

A page that composes regions has scripts of its composition too: `ShellViewScripts` renders the design-time shell with every region filled, the application composed from the published plugins with its first page in the content region and its pages listed in the sidebar, and a choice in the sidebar region reaching the inspector region through the region context. `WorkbenchCompositionScripts` does the same for the Workbench over regions a test fills: a job chosen in the sidebar opens on the page and reaches every inspector section, usage recorded later shows in the inspector while the sidebar keeps its row, and closing the inspector empties its sections; `WorkbenchPluginTests` checks which region each of the plugin's view models goes to. The Workbench's headless view scripts live beside its view model scripts in `Avala.Workbench.Tests`, which registers the plugin's views in the headless application's registry.

Waits are deterministic and driven by events, never by polling state or by time. A component states in its contract when it has applied a change: `IPresentation.Presented`, with its revision, raised by production code. A script awaits that signal or a core event through `EventWatch`, then asserts. Headless scripts need no wait at all: input and layout run synchronously on Avalonia's thread. A safety timeout guards only against a hang and, when it fires, reports the last state it observed, as `TestUiDispatcher.UntilAsync` does. Behavior about time runs on a `FakeTimeProvider`.

Every `XViewModel` needs its `XView`, even before the views of phase 10 are designed: a view model gets a plain placeholder view in the same change, bound to what it exposes, which phase 10 restyles. An item a view model lists, such as a sidebar row or a timeline entry, is a view model with its own view too, resolved through the view registry, so each kind of entry has one view model and one template.

```
dotnet build Avala.slnx
dotnet test --solution Avala.slnx
```

## Quality metrics

The CI builds and tests on Linux and Windows, then measures coverage, code metrics, technical debt and lines of code on every push to `main` and every pull request, with open-source tools and no external service. Everything appears in the run summary, and the reports are kept as the run's `coverage`, `metrics` and `badges` artifacts.

```
dotnet tool restore
dotnet test --solution Avala.UnitTests.slnf --coverage --coverage-output-format cobertura --results-directory TestResults/coverage/linux
dotnet run scripts/coverage-paths.cs -- TestResults/coverage
dotnet tool run reportgenerator -reports:"TestResults/coverage/*/*.cobertura.xml" -targetdir:TestResults/report -reporttypes:"TextSummary;Html;Badges;Cobertura" -assemblyfilters:"+Avala.*;-*.Tests;-Avala.Testing" -riskhotspotassemblyfilters:"+Avala.*;-*.Tests;-Avala.Testing" -filefilters:"-*.g.cs"
dotnet run scripts/code-metrics.cs
dotnet run scripts/metrics.cs -- --badges TestResults/badges
```

- **Coverage** comes from the unit tests only, listed in `Avala.UnitTests.slnf`. The architecture tests run without instrumentation, because the coverage tooling rewrites the code they inspect. Generated code and the shared test helpers of `Avala.Testing` are excluded. ReportGenerator writes the HTML report with its risk hotspots, its own coverage badges and one merged Cobertura file the grade reads.
- **Coverage is measured on every operating system the CI runs.** Code for one platform, such as the Windows job objects and TCP tables, only runs on that platform, so each build job collects the unit tests' coverage on its own system and uploads it as `unit-coverage-<os>`. `scripts/coverage-paths.cs` first rewrites the source paths of the reports relative to the repository, because each runner checks out to a different folder and Windows writes backslashes; with the same paths, ReportGenerator merges the reports line by line instead of counting a file once per system. The quality job downloads every report and merges them before anything is measured, so the coverage, the risk hotspots and the grade all come from the merged report. A local run on one system measures that system only: Windows-only code counts as uncovered on Linux.
- **macOS code is a thin call around pure parsing.** No CI job runs macOS, so the macOS containment and listening ports only run `ps` and `lsof` and hand the output to `MacListings`, whose parsing and process-family walk are unit tested on every system. What stays macOS-only is the call itself.
- **Code metrics** are the maintainability index, cyclomatic complexity, class coupling, depth of inheritance and lines of every production assembly, namespace, type and member. `scripts/code-metrics.cs` computes them with `CodeAnalysisMetricData` from Microsoft.CodeAnalysis.AnalyzerUtilities, the library behind `Microsoft.CodeAnalysis.Metrics`, whose `Metrics.exe` runs only on Windows, and writes them in the same layout to `TestResults/metrics/CodeMetrics.xml`.
- **Lines of code** are physical lines of C#, counted the same way as the reference figure for T3 Code: about 907,000 lines of non-test TypeScript at commit `a4c9494b0`, on 2026-10-08. The goal is a better product in no more than 15% of that.

### Technical debt grade

`scripts/metrics.cs` rates four indicators from A to E, and the grade is the worst of the four: the debt of a codebase is as high as its weakest measure.

| Indicator | Source | A | B | C | D | E |
| --- | --- | --- | --- | --- | --- | --- |
| Line coverage of the unit tests | Merged Cobertura report | ≥ 80% | ≥ 70% | ≥ 60% | ≥ 50% | < 50% |
| Mean maintainability index of the production methods | Code metrics | ≥ 80 | ≥ 70 | ≥ 60 | ≥ 50 | < 50 |
| Highest cyclomatic complexity of a production method | Code metrics | ≤ 10 | ≤ 15 | ≤ 20 | ≤ 25 | > 25 |
| Risk hotspots per 1,000 methods of the coverage report | Merged Cobertura report | ≤ 1 | ≤ 5 | ≤ 10 | ≤ 20 | > 20 |

A risk hotspot is a method whose CRAP score, `complexity² × (1 − coverage)³ + complexity`, is above 30, the limit its authors proposed, with the complexity and the line coverage the coverage tool measures for the method. The summary lists the ten riskiest. `CA1502` fails the build above a complexity of 20, so the third indicator cannot fall below C.

### Badges

`scripts/metrics.cs -- --badges <folder>` writes six SVG badges in the flat style of shields.io, drawn by the script itself: coverage, maintainability, the highest complexity and the debt grade, each in the color of its rating, then the production lines of C# and their share of T3 Code's 907,000 lines. On a push to `main`, after the quality job, the `badges` job publishes them with `scripts/publish-badges.cs` on the `badges` branch, which holds nothing else, and the README shows them from there. The job alone has `contents: write`, runs one at a time, and the script refuses to run outside a push to `main`. It builds the commit with git's plumbing, so the checkout and `main` are never touched, adds nothing when the badges are unchanged, and pushes without force on top of the previous badges, so it can only ever move `badges` forward.
