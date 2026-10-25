# Fleet servers and opt-in anonymous telemetry

## Status

Proposed

## Context

Avala keeps everything on the machine it runs on. Observability records tokens, cost at API prices, turns and limits per connection, session and job, and Resources measures the memory, CPU, processes, disk and worktrees of Avala's agents, but each machine sees only its own.

A person who runs Avala on several machines cannot answer simple questions:

- Which jobs are running on a repository right now, on every machine?
- How much has a repository cost so far, and how does that cost trend over time?
- Are two machines changing the same files of the same repository?
- Do two machines share one account's usage window without knowing it?

Avala's maintainers cannot answer questions about Avala as a product either: how many tokens it processes, at which peaks, with which harnesses, models and efforts, on what hardware, and how much memory and disk it takes. Today a packaged release sends only `installed` and `active` events through Aptabase, and the README promises that repositories, jobs, prompts, paths, harnesses, connections, accounts, costs and features are never sent.

Avala's purpose is agents you can trust without watching. Any data that leaves a machine has to keep that trust: it is chosen by the person, shown exactly before it is sent, and never more than what was chosen.

## Decision

Three tiers, kept apart: different data, different endpoints and a separate choice for each.

| Tier | Operated by | Holds | Turned on by |
| --- | --- | --- | --- |
| Local | Each person | Everything about their own machine, as today | Nothing to turn on |
| Fleet | A person or team, on their own infrastructure, with `Avala.Server` | Full detail of their instances: repositories, jobs, usage, costs, files touched | Whoever sets up the fleet |
| Global telemetry | Avala's maintainers, with a backend of its own | Anonymous daily aggregates from every install that opts in | Each install, opt-in |

The fleet never forwards anything to global telemetry, and global telemetry never learns that a repository or a job exists.

The two servers are different products. `Avala.Server`, the fleet server, serves only an API: it has no web interface, and Avala itself is the interface that reads it. The global telemetry backend is separate, with its own web console, and only Avala's maintainers run it.

### Identity across machines

- **Repository**: its normalized `origin` address, such as `github.com/owner/name`; without a remote, the hash of its root commit. Scratch repositories stay local.
- **Device**: a random ID made on first use, never derived from hardware, user name or paths, with a name the person chooses, such as "desktop" or "laptop".
- **Job**: the device ID with the local `JobId`, with its branch, title and parent for delegation.
- **Account**: a hash of the harness account, so the fleet knows when two devices share one usage window.

### The Fleet module

A new module, `Fleet`, built on the existing extension points only. It listens to integration events, such as `UsageRecorded`, `JobProgressed` and the verdicts of Verification, and reads other modules through their contracts, such as `IUsageHistory` and the contracts of Resources. It never reads another module's database.

- **An outbox in `fleet.db`.** Each fact becomes an event with its own ID, appended locally and sent when the destination is reachable, so a laptop offline keeps working and catches up later. Sending an event twice never counts it twice.
- **Transport, first without a server.** Each device appends its events to its own log in a folder the person syncs, such as with Syncthing or a private git repository. Every Avala reads every device's log and builds the same view. No conflicts arise, since each device writes only its own file, and no port is opened.
- **Transport, then with a server.** `Avala.Server` receives the same events over HTTPS, reached through the person's own network, such as Tailscale, and answers the queries Avala's Fleet section asks. Avala itself still never opens a public port.

What a fleet receives is chosen in levels:

| Level | Adds |
| --- | --- |
| Metrics | IDs, tokens, cost, states, verdicts, attempts and interventions |
| Context | Job titles, branches and the paths of files changed, needed for the map and for conflict warnings |
| Full | Transcripts, for backup and search |

The person chooses by answering Avala's questions in Settings, never by editing a file.

### What a fleet offers

- **A Fleet section** in Avala, with two scopes a person switches between: **This instance**, from its own records as today, and **All my instances**, read from `Avala.Server`, each instance named and told apart. Both show the jobs of every device by repository, cost per repository to date, trends per day, week and month, a projection for the month, cost per verified job, the cost of retries, discarded jobs and scouts, the cache hit ratio and the model and effort that pass a repository's checks at the lowest cost.
- **Subscription value.** Usage at API prices next to what the subscription actually costs, prorated.
- **A map.** A graph of repositories, jobs, devices and connections, with delegation, handoffs and follow-ups as edges, sized by cost, with a time slider, and a view linking jobs by the files they change.
- **Conflict warnings.** Two jobs on different devices that change the same files of the same repository are flagged while they run, from paths alone.
- **Later**, each through its own decision: a ceiling per day or per repository across the fleet, through `IJobAdmission`; handoff and Auto choosing with an account's usage on every device; a job continued on another device by pushing its branch with the brief Handoffs already builds; and a queue of jobs taken by whichever device has room.

### Global telemetry

An install that opts in sends one payload a day to Avala's maintainers, aggregated on the machine by hour in UTC. Two switches, each off by default and offered on the privacy step of the welcome tour: **Model usage** and **Performance and hardware**.

```json
{
  "install": "random-id",
  "version": "0.2.0",
  "day": "2026-10-25",
  "usage": [
    { "hour": 14, "harness": "claude-code", "model": "claude-opus-5-5", "effort": "high",
      "tokensIn": 120000, "tokensOut": 18000, "cacheRead": 900000, "cacheWrite": 40000,
      "turns": 14, "jobs": 3, "verified": 2, "failed": 1, "handoffs": 0 }
  ],
  "machine": { "os": "linux", "arch": "x64", "cpuCores": "8-15", "ramGb": "32-63", "diskType": "ssd", "freeDiskGb": "100-499" },
  "avala": { "memoryMb": { "p50": "256-511", "peak": "512-1023" }, "cpuPercent": { "p50": "0-9", "peak": "50-74" }, "startupMs": "1000-1999", "concurrentJobs": { "peak": 3 } },
  "worktrees": { "count": "5-9", "sizeMb": { "avg": "100-499", "max": "1000-4999" }, "prepareMs": { "p50": "1000-1999" }, "reclaimed": 4 },
  "agents": { "memoryPerJobMb": { "p50": "512-1023", "peak": "2048-4095" }, "leftoversReaped": 2 }
}
```

`usage` is sent only with Model usage on; `machine`, `avala`, `worktrees` and `agents` only with Performance and hardware on.

Rules that keep it anonymous:

- **Closed lists.** Harnesses and models are sent only from a list Avala knows; anything else is sent as `"other"`, so a private deployment's name never leaves.
- **Ranges, not values.** Hardware, memory, CPU, disk and durations are sent as ranges, and percentiles are computed on the machine. Never the CPU or GPU model, the exact memory, the host name, the distribution's name or exact version, a path, a time zone or a series by the minute.
- **No cost.** The global backend prices tokens by model itself, so nothing depends on a person's plan.
- **No address kept.** The global backend never stores the sender's IP address.
- **Small groups hidden.** Any group or combination of fewer than ten installs is never shown, such as one rare model at one effort on one platform.
- **Exactly what is sent.** Settings shows the day's payload before it is sent, and lets the person reset the install ID.
- **The existing off switches still win.** `DO_NOT_TRACK=1` and `AVALA_TELEMETRY=0` turn it off, and a build from source sends nothing, since only a packaged release holds the key.

The Aptabase events stay as they are, under their own switch.

### Avala.Server, the fleet server

`Avala.Server`, in ASP.NET on .NET 10, is self-hosted and serves only an API: it receives each instance's events, keeps them, and answers the queries of Avala's Fleet section, such as the jobs of every instance by repository, cost and trends, the map and the conflicts. It has no web interface; every view is drawn by Avala. Each instance signs in to it with a token its owner issues, and the server knows nothing of the global telemetry backend.

### The global telemetry backend

A separate backend, not `Avala.Server`, that only Avala's maintainers run. It ingests the anonymous daily payloads and serves a web console: tokens processed, peaks, the mix of harnesses, models and efforts, versions, active installs, hardware ranges and Avala's memory and worktree sizes, and optionally a public page of totals. It shares nothing with `Avala.Server` but the payload's schema.

A fleet server may send the global backend a heartbeat of its own, only if its owner turns it on: its version, its number of instances and whether it is healthy, and nothing about what it holds.

Abuse of an anonymous endpoint is limited, not prevented: a rate limit per install ID, caps on plausible values, such as tokens per hour, and outliers trimmed from the totals.

### Delivery

1. **Fleet without a server**: the `Fleet` module, its outbox, logs per device in a synced folder and the Fleet section with This instance and All my instances.
2. **`Avala.Server`**: its API, and the Fleet section reading it, with the map and conflict warnings.
3. **Global telemetry**: the opt-in switches, the payload, and the global backend with its web console.
4. **The fleet heartbeat** and the later fleet features, each with its own decision.

Each phase is proven through the simulator: two simulated devices sharing one folder for the fleet, and a fake `ITelemetry` that captures the payload, asserting what is sent and, above all, what never is.

## Alternatives considered

- **Opt-out telemetry.** More data, but it breaks the README's public promise in a product whose purpose is trust. Rejected.
- **Extending the Aptabase events with usage properties.** Little to build, but its dashboards cannot sum tokens, compute peaks or hide small groups, and it would blur the line between counting installs and measuring usage. Rejected.
- **OpenTelemetry export only.** Cheap, and a person can point it at Grafana, but it knows nothing of repositories across devices, the map or conflicts. Kept as a possible later export of the fleet, not as its design.
- **A server from the start.** Delays the first useful version behind hosting, authentication and operations. The synced folder comes first, with events shaped for the server.
- **A web dashboard on the fleet server.** A second interface to build and keep in step with Avala's own, for people who already run Avala on every instance. Rejected: Avala is the fleet's interface.
- **One server for the fleet and the global telemetry.** It would put a person's detailed records and the maintainers' anonymous aggregates in one codebase and one schema, so a mistake could move one into the other. Rejected: they are separate backends.
- **Sending raw values and anonymizing on the server.** The server would hold what identifies a machine. Rejected: data is reduced before it leaves.

## Consequences

- The README, `docs/guide/privacy-and-usage-statistics.md` and the release notes change to say what each switch sends, before any of this ships.
- A new module, `Fleet`, with its own database, a new deployable, `Avala.Server`, with its own release process in `docs/release.md`, and the global telemetry backend, released apart from Avala.
- The maintainers run and pay for a public ingestion endpoint and publish a short data policy for it.
- Repository identity by `origin` treats forks and mirrors as different repositories, and a repository without a remote is matched only by its root commit.
- Conflict warnings and the map need the Context level, so a fleet kept at Metrics sees costs and trends but not where work overlaps.
