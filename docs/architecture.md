# Architecture

Avala is a desktop harness for coding agents, written in C# on .NET 10 with Avalonia. It runs each agent in its own git worktree, answers its requests through explicit policies, judges its work by the repository's own checks and accounts for every token it spends. This page is the big picture: what the parts are and how they fit together.

## A modular monolith of plugins

Avala ships as one process, but the process knows almost nothing about what it runs. The host is an Avalonia application, a composition root and a plugin loader: it scans its `plugins` folder, instantiates every `IPlugin` it finds and lets each one register its services, handlers and views. The host references no module.

```
src/
  Avala.Sdk               contracts every plugin builds on, no UI framework
  Avala.Sdk.UI            contracts for plugin views, the view registry
  Avala.Runtime           the event bus, the process runner, logs, appearance
  Avala.Shell             the main window's view models and regions
  Avala.Host              the application, composition root and plugin loader
  Shared/                 components, storage and command-line reading every module may use
  Modules/<Module>/
    Avala.<Module>             the module's core: domain, use cases, infrastructure, view models
    Avala.<Module>.UI          its views and its plugin entry
    Avala.<Module>.Contracts   optional public contracts other modules may use
```

The modules are Agents, Jobs, Workspaces, Verification, Permissions, Supervision, Budgets, Resources, Observability, Canvas, Autopilot, Delegation, Transcripts, Recording, the Workbench, the canvas renderers (Rendering and Mermaid), the Simulator and the Claude Code provider.

- **A module depends only on the SDK, its own projects and other modules' `Contracts`.** Everything else in a module is `internal`; its single public type outside its contracts is its plugin entry.
- **Folders are named after what the code does**, such as `Submission`, `TurnChecks` or `Storage`, never after layers. The layers still exist, Domain, Application, Infrastructure and ViewModels, and a single layer map in the architecture tests assigns every namespace to one, so dependencies keep pointing inward.
- **All plugins share one load context**, so a contract has one type identity for every module: the `IAgentProvider` the simulator implements is the one Agents asks for.

A new capability, such as an issue tracker or another way to isolate work, arrives as a new module, without touching the core. It talks to the rest of Avala in three ways only:

| Mechanism | Use it to |
| --- | --- |
| Integration events on the bus | React to what happened |
| Contracts injected through dependency injection | Ask for an answer or an action |
| Extension points the core defines and plugins implement | Let the core use something it does not know, such as `IAgentProvider`, `ICompletionGate`, `IApprovalStrategy`, `IJobAdmission` or Autopilot's `IJobSource` |

## Domain

Each module's domain is plain, deterministic C#: no I/O, no async methods, no exceptions. Aggregates are sealed classes with factory methods and no public setters; every public operation returns a `Result<TEvent, TError>`, absence is an `Option<T>` rather than `null`, and each module that can reject an operation has one error enum. Lifecycles such as a job's are state machines built on Stateless, and their [diagrams](diagrams/) are generated from the code.

## The event bus

Modules learn what happened through integration events on an in-process bus, implemented in `Avala.Runtime` over `System.Threading.Channels`. Publishing never blocks: one router reads events in publishing order and posts each to the mailbox of every handler of its type. Each handler has its own mailbox and reader loop, so it sees its events one at a time, in order, and keeps plain state without locks; a slow or failing handler delays or fails only itself. Events state facts: nothing asks a question over the bus and waits for a reply, since a contract makes that dependency explicit.

The same discipline holds everywhere: concurrency between components goes through the bus or channels only, and nothing waits on time where it could await the event that states the thing happened.

## Agents and the provider contract

`Avala.Agents.Contracts` is the only thing a provider plugin depends on. A provider implements `IAgentProvider`: it starts an `IAgentSession` that streams agnostic events, such as turns, messages, reasoning, file edits, commands, permission requests, forms, usage and limits, and accepts turns, permission decisions, form answers and interruptions. The Agents module checks every event of every turn against one `Turn` aggregate, so a provider translates a protocol and never re-derives lifecycle rules.

Providers do not declare a fixed list of flags. Each capability is a **component**: a small sealed record such as `Interruptible`, `Resumable`, `AcceptsTools`, `AsksForms` or `ReportsLimits` with the windows it reports, attached to a `CapabilitySet` per connection. The core's systems query the components they know and ignore the rest, so behavior depends on declared capabilities, never on a provider's name. A plugin may define components of its own.

Sessions open on **connections**: one harness with one account, credential and configuration, several per harness if you like. Avala offers its own harness tools, such as the canvas, delegation and follow-ups, to every provider that accepts them, and each harness adapts to that offer through its plugin. A conformance kit checks every provider against the contract.

Claude Code is the first real provider: it runs the `claude` CLI in its streaming JSON mode and serves Avala's permission prompt and harness tools from an MCP server inside the session.

## The simulator and recordings

Everything Avala does is proven through the **simulator**, a provider plugin that plays scripted Claude Code sessions through the same public contracts as a real provider, without a model and without tokens. Its scenarios are declarative data: reasoning, streamed replies, real file edits, commands with real child processes, permission requests, forms, plan approvals, usage and limits, canvases, delegation and resumed conversations. It also registers a second simulated harness, so behavior across harnesses runs without a real one.

The **Recording** plugin decorates every provider and, when enabled, records each session as a file the simulator replays. Recordings committed under `tests/recordings` with their expected outcomes are regression tests, and the screenshots in the README are taken from the real application driven headless by the simulator.

## The trust layer

Avala's purpose is agents you can trust without watching. Four modules carry that, each a plugin built on the extension points above.

**Verification.** A job is done only when the checks its repository declares pass in its worktree. Verification registers a completion gate that runs those checks after every finished turn and keeps the evidence of every attempt; the review shows that verdict first.

**Permissions.** Every permission request and every question an agent asks goes through an explicit policy, built-in guards and defaults around the rules the repository commits, at the level of autonomy the job allows. Sessions always open asking before every edit and command, so nothing bypasses the policy. Every decision is audited with the reason it was made, including the ones left to a person.

**Supervision.** An agent that goes silent is noticed and its job held, with the facts that were measured, instead of hanging forever. A session that dies is caught by Jobs and held as lost.

**Budgets.** Caps on cost, tokens, memory and provider limits stop a job before it overspends; a child job's budget is carved out of its parent's, and the machine admits only as many running jobs as its limit allows. Every intervention is recorded.

Around them, **Resources** samples the processes, ports and disk every job uses, reaps what a session leaves behind, leases ports per worktree and reclaims old worktrees; **Observability** keeps usage, cost and limits per provider, account, connection, session and job; **Autopilot** runs a backlog unattended and approves a job automatically only on clean evidence; and **Delegation** lets an orchestrating agent hand work to child jobs that are governed and verified like any other.

## Persistence

Each module owns its data in its own SQLite database under the data folder, through EF Core with migrations, and no module reads another module's data. Every fact a screen presents as part of a job's record, such as the job, its usage, its evidence, its audit and its conversation, survives a restart; what belongs to a live session, such as a pending permission, dies with it. Database work never runs on the UI thread, and an architecture test checks that every module's model matches its latest migration.

## The Workbench

The Workbench is the module of the user interface, built on other modules' contracts only. It follows MVVM, view model first: view models live in the module's core assembly and reference no UI framework, and views live in its `.UI` assembly, so another UI technology means replacing the `.UI` assemblies and nothing else. A view only binds, with compiled bindings, theme resources and commands; the view registry picks the view for each view model. The main window is drawn as named **regions**, an idea taken from Prism: a page declares its regions, plugins register view models into them, and a region's context, such as the selected job, reaches every component in it, so a page never knows the components other modules contribute. Components that live apart talk through small UI messages over an injected messenger. Every view model has a design-time implementation, so every view renders in the designer, and every component has scripted acceptance tests that drive it as a user would, headless.

## Quality gates

The rules are code, and a failing rule explains itself.

- **Architecture tests** in `tests/Avala.ArchitectureTests` enforce the module boundaries, the layer map, the domain's purity, the view rules, the absence of comments, type and component size limits, the concurrency and timing rules, and that every script is C#. Each rule runs against the production code, a compliant fixture module and a violating fixture module, so it can never pass vacuously.
- **Analyzers** run on every project as errors: banned symbols for blocking calls, timed waits and coordination primitives, the threading analyzers, SonarAnalyzer.CSharp and the .NET code metrics rules, with their limits in `SonarLint.xml` and `CodeMetricsConfig.txt`.
- **CI** builds and tests on Linux, Windows and macOS on every push to `main` and every pull request, using only `dotnet` steps.

### Quality metrics

On every push to `main` and every pull request, CI also measures coverage, code metrics, technical debt and lines of code with open-source tools and no external service, and shows them in the run summary.

- **Coverage** comes from the unit tests, listed in `Avala.UnitTests.slnf`, collected on every operating system and merged, so platform-specific code counts where it runs.
- **Code metrics** are the maintainability index, cyclomatic complexity, class coupling and lines of every production assembly, type and member, computed by `scripts/code-metrics.cs`.
- **Lines of code** are physical lines of C# outside tests and generated code. They are compared with [T3 Code](https://github.com/pingdotgg/t3code), about 907,000 lines of non-test TypeScript at commit `a4c9494b0`, as a fixed yardstick: Avala aims for a better product in no more than 15% of that.

### Technical debt grade

`scripts/metrics.cs` rates four indicators from A to E, and the grade is the worst of the four:

| Indicator | A | B | C | D | E |
| --- | --- | --- | --- | --- | --- |
| Line coverage of the unit tests | ≥ 80% | ≥ 70% | ≥ 60% | ≥ 50% | < 50% |
| Mean maintainability index of the production methods | ≥ 80 | ≥ 70 | ≥ 60 | ≥ 50 | < 50 |
| Highest cyclomatic complexity of a production method | ≤ 10 | ≤ 15 | ≤ 20 | ≤ 25 | > 25 |
| Risk hotspots (CRAP score above 30) per 1,000 methods | ≤ 1 | ≤ 5 | ≤ 10 | ≤ 20 | > 20 |

On a push to `main`, the badges in the README are drawn by the same script and published on the `badges` branch.

## Analyzer exceptions

Comments are banned, so every exception to an analyzer is recorded here. The architecture tests read the `RS0030` rows that name a file.

| Rule | Scope | Reason |
| --- | --- | --- |
| `MVVMTK0032` | Everywhere | It recommends inheriting from `ObservableObject`. View models use `[INotifyPropertyChanged]` to favor composition. |
| `CA1000` | Everywhere | `Result<TValue, TError>.Success` and `Failure` are the canonical static factories of a generic result. |
| `VSTHRD003` | Everywhere | It guards against deadlocks under Visual Studio's `JoinableTaskFactory`, which Avala does not use. |
| `CA1716` | Everywhere | It reserves Visual Basic keywords such as `Option`. Avala is C# only. |
| `S108` | Everywhere | Its remedy for a block left empty on purpose is a comment, and comments are banned. |
| `S3358` | Everywhere | A chain of guards ending in the success value is how input is validated into a `Result`; cognitive complexity still bounds it. |
| `S5034` | Everywhere | It reads separate calls of the same `ValueTask` method as one task consumed twice. `CA2012` checks the real misuse. |
| `CA1506` | `tests/` | A scenario test composes the application through its real contracts on purpose. |
| `S3903` | `scripts/` | Scripts are file-based apps with top-level statements, which cannot share a file with a file-scoped namespace. |
| `S4036` | `scripts/` | Scripts run `git`, `dotnet` and the platforms' signing tools from the runner's `PATH` by design. |
| `RS0030` | `GuardedTransitions.cs` | The guarded transition helper is the single place allowed to call `Fire`, right after `CanFire`. |
| `RS0030` | xUnit's generated entry point | Third-party generated code that blocks on the test platform's task. |
| `RS0030` | `tests/Avala.Testing/TemporaryFolder.cs`, `Task.Delay` | Windows releases a killed process's handles a moment after it exits, with no event, so deleting a test folder retries after a short delay. |
| `RS0030` | `src/Avala.Runtime/Diagnostics/LastWords.cs` | A process dying of an unhandled exception writes its last log line synchronously, once. |
| `S3011` | `tests/Avala.Testing.UI/AnimationClock.cs` | Avalonia keeps its animation clock private, so the headless test application stops it by reflection. |
| All analyzers | `Avala.ArchitectureTests.Fixtures` and `Avala.ArchitectureTests.Fixtures.UI` | The fixtures break rules on purpose. |

## Learn more

- [Releasing](release.md): packages, signing and the update check.
- [Diagrams](diagrams/): the lifecycles of jobs, turns, workspaces and canvases, generated from the code.
- [Contributing](../CONTRIBUTING.md): building, testing and sending a change.
