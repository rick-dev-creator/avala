using Avala.Components.Privacy;
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

internal sealed record DesignConnectionCardViewModel(string Name, string Provider, Personal Account, bool IsDefault, string Cost, IReadOnlyList<ILimitViewModel> Limits, IReadOnlyList<IAgentViewModel> Agents) : IConnectionCardViewModel
{
    public DesignConnectionCardViewModel()
        : this(
            "claude-work",
            "Claude Code",
            Personal.Account("dana@acme.dev"),
            true,
            "9.86 USD",
            [new DesignLimitViewModel(), new DesignLimitViewModel("7d", "7-day window", 0.41, "41%", "resets 2026-10-15 21:00", 0.9, false)],
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

    public PersonalPhrase Summary => OverviewPhrases.Hub(Account, Cost, IsDefault);
}

internal sealed class DesignConnectionsViewModel : IConnectionsViewModel
{
    public IReadOnlyList<IConnectionCardViewModel> Connections { get; } =
    [
        new DesignConnectionCardViewModel(),
        new DesignConnectionCardViewModel(
            "claude-personal",
            "Claude Code",
            Personal.Account("dana.martinez@gmail.com"),
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
            Personal.Account("Acme Platform Team"),
            false,
            "1.12 USD",
            [new DesignLimitViewModel("usage", "Usage window", 0.24, "24%", "resets 2026-10-10 09:00", 0.9, false)],
            [new DesignAgentViewModel(SampleAgents.WebhookTests, "Rewrite webhook signature tests", StatusKind.NeedsYou, "wants to run a command")]),
        new DesignConnectionCardViewModel(
            "pi-local",
            "Pi",
            Personal.Plain("local"),
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

    public string ShareNote => "kept 0.80 USD · carved 0.80 USD · 0.80 USD · 0.60 USD";

    public bool HasBudget => true;

    public IStatusDotViewModel Dot { get; } = new StatusDotViewModel(StatusKind.Working);

    public IReadOnlyList<ISubagentViewModel> Subagents { get; init; } = [new DesignSubagentViewModel("Plan the migration", "Listed 14 call sites", SubagentState.Done)];

    public bool HasSubagents => Subagents.Count > 0;

    public bool IsViewing { get; init; } = true;

    public IRelayCommand<JobId> OpenCommand { get; init; } = OverviewPhrases.Idle;

    public ContextGlance Context { get; init; } = new(true, "42%", 0.58, false, true);

    public string Tokens { get; init; } = "1.1M tokens";

    public IReadOnlyList<TreeTally> Tally { get; init; } = [new("1 needs you", StatusKind.NeedsYou), new("1 running", StatusKind.Working), new("1 done", StatusKind.Done)];
}

internal sealed record DesignSubagentViewModel(string Name, string Activity, SubagentState State) : ISubagentViewModel
{
    public string Meter { get; init; } = string.Empty;

    public string StateText => State.ToString().ToLowerInvariant();
}

internal sealed record DesignDelegationNodeViewModel(JobId Job, string Title, int Depth, StatusKind Kind, string Harness, string Connection, string Activity, string Spend, bool HasCarve, double SpentShare) : IDelegationNodeViewModel
{
    public DesignDelegationNodeViewModel()
        : this(SampleAgents.CallSites, "Update call sites in internal/payments", 1, StatusKind.Working, "Claude Code", "claude-work", "Edited internal/payments/refund.go · 9 of 14 files", "0.41 USD of 0.80 USD", true, 0.51)
    {
    }

    public JobStatus Status => Kind == StatusKind.ReadyForReview ? JobStatus.AwaitingReview : Kind == StatusKind.NeedsYou ? JobStatus.NeedsHelp : JobStatus.Running;

    public string Role { get; init; } = string.Empty;

    public string State => OverviewPhrases.State(Kind);

    public IStatusDotViewModel Dot { get; } = new StatusDotViewModel(Kind);

    public IReadOnlyList<ISubagentViewModel> Subagents { get; init; } = [new DesignSubagentViewModel("Find the refund call sites", "Read internal/payments/refund.go", SubagentState.Running) { Meter = "6 tool uses · 18,240 tokens" }];

    public bool HasSubagents => Subagents.Count > 0;

    public bool IsViewing { get; init; }

    public IRelayCommand<JobId> OpenCommand { get; init; } = OverviewPhrases.Idle;

    public ContextGlance Context { get; init; } = new(true, "72%", 0.28, false, false);

    public string Tokens { get; init; } = "96.4k tokens";

    public IReadOnlyList<IRecentActionViewModel> Recent { get; init; } =
    [
        new RecentActionViewModel("Edit internal/payments/refund.go", DateTimeOffset.UnixEpoch, false),
        new RecentActionViewModel("Run go build ./internal/payments/...", DateTimeOffset.UnixEpoch, false),
        new RecentActionViewModel("Look up RefundParams", DateTimeOffset.UnixEpoch, false),
    ];

    public string Doing => Recent.Count > 0 ? Recent[0].Text : Activity;

    public string Spent => Spend.Split(' ')[0] + " USD";

    public int SubagentCount => Subagents.Count;

    public string Agents => OverviewPhrases.Count(SubagentCount, "agent");

    public bool NeedsYou => Kind is StatusKind.NeedsYou or StatusKind.Held;

    public bool IsSettled { get; init; }

    public bool IsOpen { get; init; }

    public IRelayCommand ToggleCommand { get; } = new RelayCommand(() => { });
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
        new DesignDelegationNodeViewModel(SampleAgents.WebhookTests, "Rewrite webhook signature tests", 1, StatusKind.NeedsYou, "Codex", "codex-team", "Wants to run go generate ./internal/webhooks/...", "0.22 USD of 0.80 USD", true, 0.28),
        new DesignDelegationNodeViewModel(SampleAgents.ApiReference, "Regenerate the API reference", 1, StatusKind.ReadyForReview, "Pi", "pi-local", "Branch ready to merge into avala/stripe-v79", "0.32 USD of 0.60 USD", true, 0.53),
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

internal sealed class DesignDelegationPaneViewModel : IDelegationPaneViewModel
{
    public IOrchestratorCardViewModel? Root { get; } = new DesignOrchestratorCardViewModel();

    public IReadOnlyList<IDelegationNodeViewModel> Children { get; } =
    [
        new DesignDelegationNodeViewModel(SampleAgents.WebhookTests, "Rewrite webhook signature tests", 1, StatusKind.NeedsYou, "Claude Code", "claude", "Wants to run go generate ./internal/webhooks/...", "0.22 USD of 0.80 USD", true, 0.28)
        {
            Subagents = [new DesignSubagentViewModel("Read the signature tests", "Waiting for your answer", SubagentState.Waiting)],
            Context = new(true, "88%", 0.12, false, false),
            Tokens = "41k tokens",
            Recent = [new RecentActionViewModel("Run go generate ./internal/webhooks/...", DateTimeOffset.UnixEpoch, false)],
        },
        new DesignDelegationNodeViewModel { IsOpen = true },
        new DesignDelegationNodeViewModel(SampleAgents.IdempotencyKeys, "Port the idempotency keys", 1, StatusKind.Working, "Claude Code", "claude-work", "Run go test ./internal/idempotency/...", "0.37 USD of 0.80 USD", true, 0.46)
        {
            Subagents = [],
            Context = new(true, "64%", 0.36, false, false),
            Tokens = "88k tokens",
            Recent = [new RecentActionViewModel("Run go test ./internal/idempotency/...", DateTimeOffset.UnixEpoch, false)],
        },
        new DesignDelegationNodeViewModel(SampleAgents.PayoutScheduler, "Update the payout scheduler", 1, StatusKind.Working, "Claude Code", "claude", "Edit internal/payouts/schedule.go", "0.58 USD of 0.80 USD", true, 0.73)
        {
            Subagents = [],
            Context = new(true, "22%", 0.78, true, true),
            Tokens = "212k tokens",
            Recent = [new RecentActionViewModel("Edit internal/payouts/schedule.go", DateTimeOffset.UnixEpoch, false)],
        },
    ];

    public IReadOnlyList<IDelegationNodeViewModel> Settled { get; } =
    [
        new DesignDelegationNodeViewModel(SampleAgents.ApiReference, "Regenerate the API reference", 1, StatusKind.Done, "Claude Code", "claude-work", "integrated into its parent", "0.32 USD of 0.60 USD", true, 0.53) { Subagents = [], IsSettled = true },
    ];

    public string SettledLabel => "1 done";

    public bool ShowsSettled => false;

    public IRelayCommand ToggleSettledCommand { get; } = new RelayCommand(() => { });

    public string Caption => "6 jobs · 3 agents";

    public string Footer => "claude-work · 3 jobs   claude · 2 jobs";

    public bool IsOpen => true;

    public IRelayCommand CloseCommand { get; } = new RelayCommand(() => { });
}

internal sealed class DesignDelegationToggleViewModel : IDelegationToggleViewModel
{
    public bool IsShown { get; init; } = true;

    public bool IsOpen { get; init; } = true;

    public IRelayCommand ToggleCommand { get; init; } = new RelayCommand(() => { });
}

internal sealed class DesignRecentActionViewModel : IRecentActionViewModel
{
    public string Text => "Edit internal/payments/refund.go";

    public DateTimeOffset Since => DateTimeOffset.UnixEpoch;

    public bool HasSince => false;
}
