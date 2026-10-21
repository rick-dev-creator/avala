using Avala.Components.Privacy;
using Avala.Components.Status;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk.Presentation;
using Avala.Workbench.Conversation;
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

internal sealed record DesignBranchPlaceViewModel(string Key, int Depth, BranchKind Kind, SubagentState State) : IBranchPlaceViewModel
{
    public DesignBranchPlaceViewModel()
        : this("design", 1, BranchKind.InProcess, SubagentState.Working)
    {
    }

    public string KindText => BranchPhrases.Kind(Kind);

    public string StateText => SubagentStates.Text(State);

    public Avala.Components.Graphs.EdgeKind Edge => BranchPhrases.Edge(State);

    public bool IsWorking => State == SubagentState.Working;

    public bool IsWaiting => State == SubagentState.Waiting;

    public bool IsAlert => State is SubagentState.Failed or SubagentState.Lost;

    public bool IsSettled => State is SubagentState.Completed or SubagentState.Stopped;

    public int Branches { get; init; }

    public bool HasBranches => Branches > 0;

    public bool IsFolded { get; init; }

    public HarnessMark Harness { get; init; } = new("Claude Code", "claude-work", string.Empty, "#D97757");

    public bool IsCrossHarness => Kind == BranchKind.CrossHarness;

    public bool RunsInside => Kind is BranchKind.InProcess or BranchKind.Command;

    public IRelayCommand FoldCommand { get; } = new RelayCommand(() => { });
}

internal sealed class DesignOrchestratorCardViewModel : IOrchestratorCardViewModel
{
    public IBranchPlaceViewModel Place { get; init; } = new DesignBranchPlaceViewModel("root", 0, BranchKind.Orchestrator, SubagentState.Working) { Branches = 6 };

    public JobId Job => SampleJobs.CheckoutSplit;

    public string Title => "Migrate payments to stripe-go v79";

    public string Harness => "Claude Code";

    public string Connection => "claude-work";

    public string Detail => "ledger-api · Supervised · waiting on 2 sub-agents";

    public string Doing => "Waiting for its sub-agents";

    public DateTimeOffset Since => DateTimeOffset.UnixEpoch;

    public bool IsTiming => false;

    public string Took => "12m 40s";

    public string Budget => "Budget 3 USD";

    public string Spent => "1.47 USD spent";

    public IReadOnlyList<BudgetShare> Shares { get; } = [new(0.8, true), new(0.8, false), new(0.8, false), new(0.6, false)];

    public string ShareNote => "kept 0.80 USD · carved 0.80 USD · 0.80 USD · 0.60 USD";

    public bool HasBudget { get; init; } = true;

    public IStatusDotViewModel Dot { get; } = new StatusDotViewModel(StatusKind.Working);

    public bool IsViewing { get; init; } = true;

    public IRelayCommand<JobId> OpenCommand { get; init; } = OverviewPhrases.Idle;

    public ContextGlance Context { get; init; } = new(true, "42%", 0.58, false, true);

    public string Tokens { get; init; } = "1.1M tokens";
}

internal sealed record DesignSubagentViewModel(string Name, string Activity, SubagentState State) : ISubagentViewModel
{
    public DesignSubagentViewModel()
        : this("Find the refund helpers", "Grep RefundParams in internal/", SubagentState.Working)
    {
    }

    public IBranchPlaceViewModel Place { get; init; } = new DesignBranchPlaceViewModel(Name, 1, BranchKind.InProcess, State);

    public JobId Job => SampleJobs.CheckoutSplit;

    public string Note { get; init; } = string.Empty;

    public bool HasNote => Note.Length > 0;

    public string Meter { get; init; } = string.Empty;

    public DateTimeOffset Since => DateTimeOffset.UnixEpoch;

    public bool IsTiming => false;

    public IReadOnlyList<string> Trail { get; init; } = [];

    public bool HasTrail => Trail.Count > 0;

    public bool IsOpen { get; init; }

    public IRelayCommand ToggleCommand { get; } = new RelayCommand(() => { });

    public IRelayCommand<JobId> OpenCommand { get; } = OverviewPhrases.Idle;

    public string StateText => SubagentStates.Text(State);
}

internal sealed record DesignDelegationNodeViewModel(JobId Job, string Title, int Depth, StatusKind Kind, string Harness, string Connection, string Activity, string Spend, bool HasCarve, double SpentShare) : IDelegationNodeViewModel
{
    public DesignDelegationNodeViewModel()
        : this(SampleAgents.CallSites, "Update call sites in internal/payments", 1, StatusKind.Working, "Claude Code", "claude-work", "Edited internal/payments/refund.go · 9 of 14 files", "0.41 USD of 0.80 USD", true, 0.51)
    {
    }

    public IBranchPlaceViewModel Place { get; init; } = new DesignBranchPlaceViewModel(Title, Depth, BranchKind.Child, SubagentState.Working) { Branches = 1 };

    public JobStatus Status => Kind == StatusKind.ReadyForReview ? JobStatus.AwaitingReview : Kind == StatusKind.NeedsYou ? JobStatus.NeedsHelp : JobStatus.Running;

    public string Role { get; init; } = string.Empty;

    public string State => OverviewPhrases.State(Kind);

    public string StateText => Place.StateText;

    public IStatusDotViewModel Dot { get; } = new StatusDotViewModel(Kind);

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

    public bool NeedsYou => Kind is StatusKind.NeedsYou or StatusKind.Held;

    public DateTimeOffset Since => DateTimeOffset.UnixEpoch;

    public bool IsTiming => false;

    public string Took { get; init; } = "4m 12s";

    public bool IsSettled { get; init; }

    public bool IsOpen { get; init; }

    public IRelayCommand ToggleCommand { get; } = new RelayCommand(() => { });

    public IPeekViewModel? Peek { get; init; }

    public bool IsPeeking => Peek is not null;

    public IRelayCommand PeekCommand { get; } = new RelayCommand(() => { });
}

internal sealed record DesignFoldedBranchesViewModel(string Text) : IFoldedBranchesViewModel
{
    public DesignFoldedBranchesViewModel()
        : this("2 completed · 1 stopped")
    {
    }

    public IBranchPlaceViewModel Place { get; init; } = new DesignBranchPlaceViewModel("settled", 1, BranchKind.InProcess, SubagentState.Completed) { Branches = 2, IsFolded = true };
}

internal sealed record DesignWaitingAgentViewModel() : IWaitingAgentViewModel
{
    public JobId Job => SampleAgents.WebhookTests;

    public string Name => "Rewrite webhook signature tests";

    public string What => "asks: go generate ./internal/webhooks/...";

    public IRelayCommand<JobId> OpenCommand { get; } = OverviewPhrases.Idle;
}

internal sealed class DesignSubagentTreeViewModel : ISubagentTreeViewModel
{
    public IReadOnlyList<IAgentBranch> Branches { get; init; } =
    [
        new DesignOrchestratorCardViewModel(),
        new DesignDelegationNodeViewModel(SampleAgents.WebhookTests, "Rewrite webhook signature tests", 1, StatusKind.NeedsYou, "Codex", "codex-pro", "Wants to run go generate ./internal/webhooks/...", "0.22 USD of 0.80 USD", true, 0.28)
        {
            Place = new DesignBranchPlaceViewModel("webhooks", 1, BranchKind.CrossHarness, SubagentState.Waiting),
            Recent = [new RecentActionViewModel("Run go generate ./internal/webhooks/...", DateTimeOffset.UnixEpoch, false)],
            Tokens = "41k tokens",
        },
        new DesignSubagentViewModel("Port the idempotency keys", "Read internal/idempotency/keys.go", SubagentState.Lost)
        {
            Note = "its work was lost when its turn or session ended",
            Meter = "11 tool uses · 32.1k tokens",
        },
        new DesignDelegationNodeViewModel(),
        new DesignSubagentViewModel("Find the refund helpers", "Grep RefundParams in internal/", SubagentState.Working)
        {
            Place = new DesignBranchPlaceViewModel("helpers", 2, BranchKind.InProcess, SubagentState.Working),
            Meter = "6 tool uses · 18.2k tokens",
        },
        new DesignFoldedBranchesViewModel("1 completed · 1 stopped"),
    ];

    public string Agents => "6 agents";

    public string Working => "2 working";

    public string NeedsYou => "1 needs you";

    public bool HasNeedsYou => true;

    public string Spent => "1.47 USD";

    public IReadOnlyList<IWaitingAgentViewModel> Waiting { get; } =
        [new WaitingAgentViewModel(SampleAgents.WebhookTests, "Rewrite webhook signature tests", "Wants to run go generate ./internal/webhooks/...", OverviewPhrases.Idle)];
}

internal sealed class DesignDelegationRefusalViewModel : IDelegationRefusalViewModel
{
    public string Instruction => "Rewrite the payment step in Svelte";

    public string Reason => "the repository declares no delegation to that connection";
}

internal sealed class DesignOrchestrationViewModel : IOrchestrationViewModel
{
    public JobId Job { get; init; } = SampleJobs.CheckoutSplit;

    public string Title { get; init; } = "Migrate payments to stripe-go v79";

    public string Repository { get; init; } = "ledger-api";

    public string Size { get; init; } = "4 jobs · 3 agents";

    public string NeedsYou { get; init; } = "1 needs you";

    public string Tokens { get; init; } = "1.1M tokens";

    public string Spent { get; init; } = "1.47 USD";

    public bool IsActive { get; init; } = true;

    public bool IsCollapsed { get; init; }

    public IRelayCommand ToggleCommand { get; } = new RelayCommand(() => { });

    public ISubagentTreeViewModel Tree { get; init; } = new DesignSubagentTreeViewModel();

    public IReadOnlyList<IDelegationRefusalViewModel> Refused { get; init; } = [];
}

internal sealed class DesignDelegationViewModel : IDelegationViewModel
{
    public IReadOnlyList<IOrchestrationViewModel> Trees { get; init; } =
    [
        new DesignOrchestrationViewModel(),
        new DesignOrchestrationViewModel
        {
            Job = SampleAgents.ZodUpdate,
            Title = "Profile the slow checkout and fix the hot path",
            Repository = "acme-dashboard",
            Size = "2 jobs",
            NeedsYou = string.Empty,
            Tokens = "184k tokens",
            Spent = "0.84 USD",
            IsCollapsed = true,
        },
    ];

    public DelegationScope Scope => DelegationScope.Active;

    public string ActiveLabel => "Active · 2";

    public string FinishedLabel => "Finished · 5";

    public IReadOnlyList<string> Repositories { get; } = [DelegationViewModel.AllRepositories, "acme-dashboard", "ledger-api"];

    public string Repository { get; set; } = DelegationViewModel.AllRepositories;

    public IReadOnlyList<string> Connections { get; } = [DelegationViewModel.AllConnections, "claude", "claude-work"];

    public string Connection { get; set; } = DelegationViewModel.AllConnections;

    public string Caption => "2 orchestrations · 5 at work · 1 needs you";

    public string Empty => DelegationPhrases.NoneYet;

    public IRelayCommand<DelegationScope> ScopeCommand { get; } = new RelayCommand<DelegationScope>(_ => { });

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
    public ISubagentTreeViewModel Tree { get; } = new DesignSubagentTreeViewModel();

    public bool HasTree => true;

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

internal sealed class DesignPeekViewModel : IPeekViewModel
{
    public IReadOnlyList<object> Latest { get; } =
    [
        new DesignToolViewModel(Avala.Agents.Contracts.Events.ItemKind.FileEdit, "internal/webhooks/signature_test.go", "+40 −22"),
        new DesignMessageViewModel("The v79 client signs with a timestamp tolerance, so the fixtures need regenerating.", false),
        new Avala.Workbench.Cards.DesignPermissionCardViewModel(),
    ];

    public IComposerViewModel Composer { get; } = new DesignComposerViewModel(Avala.Jobs.Contracts.JobStatus.Running);
}
