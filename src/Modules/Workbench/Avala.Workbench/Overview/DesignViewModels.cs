using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Sdk.Presentation;
using Avala.Workbench.Presenting;
using Avala.Workbench.Usage;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Overview;

internal sealed class DesignAgentViewModel(JobId job, string title, StatusKind kind, string fact) : IAgentViewModel
{
    public DesignAgentViewModel()
        : this(SampleJobs.JpyRounding, "Fix JPY rounding in invoice totals", StatusKind.Working, "editing money/minor.go, then re-running the money tests")
    {
    }

    public JobId Job { get; } = job;

    public string Title { get; } = title;

    public JobStatus Status { get; } = kind switch
    {
        StatusKind.Checking => JobStatus.Checking,
        StatusKind.NeedsYou or StatusKind.Held => JobStatus.NeedsHelp,
        StatusKind.ReadyForReview => JobStatus.AwaitingReview,
        _ => JobStatus.Running,
    };

    public string Fact { get; } = fact;

    public string State { get; } = OverviewPhrases.State(kind);

    public IStatusDotViewModel Dot { get; } = new StatusDotViewModel(kind);

    public string Summary => $"{State} — {Fact}";
}

internal sealed record DesignConnectionCardViewModel(string Name, string Provider, string Account, bool IsDefault, string Cost, IReadOnlyList<ILimitViewModel> Limits, IReadOnlyList<IAgentViewModel> Agents) : IConnectionCardViewModel
{
    public DesignConnectionCardViewModel()
        : this(
            "claude-work",
            "Claude Code",
            "account work",
            true,
            "9.86 USD",
            [new DesignLimitViewModel()],
            [
                new DesignAgentViewModel(),
                new DesignAgentViewModel(SampleJobs.CheckoutSplit, "Migrate payments to stripe-go v79", StatusKind.Working, "3 sub-agents on Claude Code, Codex and Pi, waiting on 2"),
                new DesignAgentViewModel(SampleJobs.InvoicePdf, "Add invoice PDF endpoint", StatusKind.NeedsYou, "asks a question"),
                new DesignAgentViewModel(SampleAgents.ZodUpdate, "Update zod to 3.23", StatusKind.Checking, "verifying"),
                new DesignAgentViewModel(SampleJobs.LoginRateLimit, "Rate-limit POST /login", StatusKind.ReadyForReview, "verified on attempt 2"),
                new DesignAgentViewModel(SampleAgents.CallSites, "Update call sites", StatusKind.Working, "9 of 14"),
            ])
    {
    }

    public double Used => Limits.Count == 0 ? 0 : Limits.Max(limit => limit.Used);

    public string Use => Limits.Count == 0 ? "no limit" : $"{Limits[0].Window} · {Amounts.Percent(Used)}";

    public bool IsNearLimit => Limits.Any(limit => limit.IsNear);

    public bool IsProminent => Agents.Count >= 2;

    public string Summary => OverviewPhrases.Hub(Account, Cost, IsDefault);
}

internal sealed class DesignConnectionsViewModel : IConnectionsViewModel
{
    public IReadOnlyList<IConnectionCardViewModel> Connections { get; } =
    [
        new DesignConnectionCardViewModel(),
        new DesignConnectionCardViewModel(
            "claude-personal",
            "Claude Code",
            "account personal",
            false,
            "4.21 USD",
            [new DesignLimitViewModel("5h", "5-hour window", 0.31, "31%", "resets 2026-10-09 17:05", 0.9, false)],
            [
                new DesignAgentViewModel(SampleJobs.FlakyCheckout, "Fix flaky CheckoutForm test", StatusKind.NeedsYou, "wants to run a command"),
                new DesignAgentViewModel(SampleJobs.SyncQueue, "Extract sync queue into a module", StatusKind.Held, "held: stalled"),
            ]),
        new DesignConnectionCardViewModel(
            "codex-team",
            "Codex",
            "account team",
            false,
            "1.12 USD",
            [new DesignLimitViewModel("usage", "Usage window", 0.24, "24%", "resets 2026-10-10 09:00", 0.9, false)],
            [new DesignAgentViewModel(SampleAgents.WebhookTests, "Rewrite webhook signature tests", StatusKind.NeedsYou, "wants to run a command")]),
        new DesignConnectionCardViewModel(
            "pi-local",
            "Pi",
            "local",
            false,
            "no cost",
            [],
            [new DesignAgentViewModel(SampleAgents.ApiReference, "Regenerate the API reference", StatusKind.ReadyForReview, "verified")]),
    ];

    public string FileNote => string.Empty;

    public string Caption => "10 agents · 4 connections on 3 harnesses · hover for a summary, click to open";

    public IRelayCommand<JobId> OpenCommand { get; } = new RelayCommand<JobId>(_ => { });

    public long Revision => 0;

    public event EventHandler<Presented>? Presented
    {
        add { }
        remove { }
    }

    public void Activate()
    {
    }

    public void Deactivate()
    {
    }
}

internal sealed class DesignOrchestratorViewModel(JobId job, string title, JobStatus status) : IOrchestratorViewModel
{
    public DesignOrchestratorViewModel()
        : this(SampleJobs.CheckoutSplit, "Migrate payments to stripe-go v79", JobStatus.Running)
    {
    }

    public JobId Job { get; } = job;

    public string Title { get; } = title;

    public JobStatus Status { get; } = status;
}

internal sealed class DesignOrchestratorCardViewModel : IOrchestratorCardViewModel
{
    public JobId Job => SampleJobs.CheckoutSplit;

    public string Title => "Migrate payments to stripe-go v79";

    public string Harness => "Claude Code";

    public string Connection => "claude-work";

    public string Detail => "ledger-api · Supervised · waiting on 2 sub-agents";

    public string Budget => "Budget 3 USD";

    public string Spent => "1.47 USD spent across the tree";

    public IReadOnlyList<BudgetShare> Shares { get; } = [new(0.8, true), new(0.8, false), new(0.8, false), new(0.6, false)];

    public string ShareNote => "kept 0.8 USD · carved 0.8 USD · 0.8 USD · 0.6 USD";

    public bool HasBudget => true;

    public IStatusDotViewModel Dot { get; } = new StatusDotViewModel(StatusKind.Working);
}

internal sealed record DesignDelegationNodeViewModel(JobId Job, string Title, int Depth, StatusKind Kind, string Harness, string Connection, string Activity, string Spent, string Carve, double SpentShare) : IDelegationNodeViewModel
{
    public DesignDelegationNodeViewModel()
        : this(SampleAgents.CallSites, "Update call sites in internal/payments", 1, StatusKind.Working, "Claude Code", "claude-work", "Edited internal/payments/refund.go · 9 of 14 files", "0.41 USD", "0.8 USD", 0.51)
    {
    }

    public JobStatus Status => Kind == StatusKind.ReadyForReview ? JobStatus.AwaitingReview : Kind == StatusKind.NeedsYou ? JobStatus.NeedsHelp : JobStatus.Running;

    public string State => OverviewPhrases.State(Kind);

    public IStatusDotViewModel Dot { get; } = new StatusDotViewModel(Kind);
}

internal sealed class DesignDelegationRefusalViewModel : IDelegationRefusalViewModel
{
    public string Instruction => "Rewrite the payment step in Svelte";

    public string Reason => "the repository declares no delegation to that connection";
}

internal sealed class DesignDelegationViewModel : IDelegationViewModel
{
    public IReadOnlyList<IOrchestratorViewModel> Orchestrators { get; } = [new DesignOrchestratorViewModel()];

    public IReadOnlyList<IDelegationNodeViewModel> Children { get; } =
    [
        new DesignDelegationNodeViewModel(),
        new DesignDelegationNodeViewModel(SampleAgents.WebhookTests, "Rewrite webhook signature tests", 1, StatusKind.NeedsYou, "Codex", "codex-team", "Wants to run go generate ./internal/webhooks/...", "0.22 USD", "0.8 USD", 0.28),
        new DesignDelegationNodeViewModel(SampleAgents.ApiReference, "Regenerate the API reference", 1, StatusKind.ReadyForReview, "Pi", "pi-local", "Branch ready to merge into avala/stripe-v79", "0.32 USD", "0.6 USD", 0.53),
    ];

    public IReadOnlyList<IDelegationRefusalViewModel> Refused { get; } = [];

    public IOrchestratorViewModel? Selected => Orchestrators[0];

    public IOrchestratorCardViewModel? Root { get; } = new DesignOrchestratorCardViewModel();

    public string Caption => "Migrate payments to stripe-go v79 · 1 orchestrator, 3 sub-agents";

    public IRelayCommand<IOrchestratorViewModel> SelectCommand { get; } = new RelayCommand<IOrchestratorViewModel>(_ => { });

    public IRelayCommand<JobId> OpenCommand { get; } = new RelayCommand<JobId>(_ => { });

    public long Revision => 0;

    public event EventHandler<Presented>? Presented
    {
        add { }
        remove { }
    }

    public void Activate()
    {
    }

    public void Deactivate()
    {
    }
}

internal sealed class DesignOverviewViewModel : IOverviewViewModel
{
    public string Title => "Overview";

    public IConnectionsViewModel Connections { get; } = new DesignConnectionsViewModel();

    public IDelegationViewModel Delegation { get; } = new DesignDelegationViewModel();

    public bool ShowsDelegation => false;

    public IRelayCommand ShowConnectionsCommand { get; } = new RelayCommand(() => { });

    public IRelayCommand ShowDelegationCommand { get; } = new RelayCommand(() => { });
}
