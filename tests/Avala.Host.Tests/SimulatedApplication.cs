using System.Globalization;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Budgets.Contracts;
using Avala.Canvas.Contracts;
using Avala.Delegation.Contracts;
using Avala.ClaudeCode;
using Avala.Host.Composition;
using Avala.Jobs.Contracts;
using Avala.Mermaid.UI;
using Avala.Observability.Contracts;
using Avala.Permissions.Contracts;
using Avala.Resources.Contracts;
using Avala.Runtime;
using Avala.Supervision.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;
using Avala.Sdk.Processes;
using Avala.Sdk.UI;
using Avala.Simulator;
using Avala.ForgeSimulator;
using Avala.Recording;
using Avala.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Host.Tests;

internal sealed class SimulatedApplication : IAsyncDisposable
{
    private readonly CancellationTokenSource subscriptions = new();
    private readonly ProcessesInFlight processes;

    public SimulatedApplication(CompositionRoot root, ProcessesInFlight processes)
    {
        Root = root;
        this.processes = processes;
        Progress = Watch<JobProgressed>();
        Activity = Watch<AgentActivity>();
        Canvases = Watch<CanvasUpdated>();
        Decisions = Watch<PermissionDecided>();
        Holds = Watch<JobHeld>();
        Resumable = Watch<JobResumable>();
        Supervision = Watch<SupervisorIntervened>();
        Budgets = Watch<BudgetIntervened>();
        Usage = Watch<UsageRecorded>();
        Forms = Watch<FormDecided>();
        Autonomies = Watch<AutonomyApplied>();
        Opened = Watch<SessionOpened>();
        Orphans = Watch<OrphansFound>();
        Reclaimed = Watch<WorktreeReclaimed>();
        Leased = Watch<PortsLeased>();
        Queued = Watch<JobQueued>();
        Sampled = Watch<ResourcesSampled>();
        Started = new(Feed.SubscribeAsync<StartupCompleted>(subscriptions.Token), Feed.SubscribeAsync<StartupProgressed>(subscriptions.Token), TestContext.Current.CancellationToken) { Processes = processes };
        Reported = Watch<ChildReported>();
        Delivered = Watch<ReportDelivered>();
    }

    public EventWatch<StartupCompleted> Started { get; }

    public EventWatch<ChildReported> Reported { get; }

    public EventWatch<ReportDelivered> Delivered { get; }

    public EventWatch<OrphansFound> Orphans { get; }

    public EventWatch<WorktreeReclaimed> Reclaimed { get; }

    public EventWatch<PortsLeased> Leased { get; }

    public EventWatch<JobQueued> Queued { get; }

    public EventWatch<ResourcesSampled> Sampled { get; }

    public EventWatch<SessionOpened> Opened { get; }

    public EventWatch<FormDecided> Forms { get; }

    public EventWatch<AutonomyApplied> Autonomies { get; }

    public CompositionRoot Root { get; }

    public EventWatch<JobProgressed> Progress { get; }

    public EventWatch<AgentActivity> Activity { get; }

    public EventWatch<CanvasUpdated> Canvases { get; }

    public EventWatch<PermissionDecided> Decisions { get; }

    public EventWatch<JobHeld> Holds { get; }

    public EventWatch<JobResumable> Resumable { get; }

    public EventWatch<SupervisorIntervened> Supervision { get; }

    public EventWatch<BudgetIntervened> Budgets { get; }

    public EventWatch<UsageRecorded> Usage { get; }

    public Task<IReadOnlyList<string>> BrokenAsync() => InvariantCheck.SettledAsync(Root.Services, TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync()
    {
        await subscriptions.CancelAsync();
        await Root.DisposeAsync();
        subscriptions.Dispose();
    }

    public EventWatch<TEvent> Watch<TEvent>()
        where TEvent : IIntegrationEvent =>
        new(Feed.SubscribeAsync<TEvent>(subscriptions.Token), Feed.SubscribeAsync<JobProgressed>(subscriptions.Token), LoggedAsync, TestContext.Current.CancellationToken) { Processes = processes };

    private Task<string> LoggedAsync(CancellationToken cancellationToken) =>
        RunLog.ProblemsAsync(Root.Services.GetRequiredService<AvalaPaths>().Logs, cancellationToken);

    private IEventFeed Feed => Root.Services.GetRequiredService<IEventFeed>();
}
