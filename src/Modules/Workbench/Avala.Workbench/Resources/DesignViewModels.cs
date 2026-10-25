using Avala.Components.Privacy;
using Avala.Components.Meters;
using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Resources;

internal sealed record DesignAgentTreeViewModel(string Job, string Connection, int Processes, string Memory, string Cpu, string Ports, StatusKind Kind) : IAgentTreeViewModel
{
    public DesignAgentTreeViewModel()
        : this("Fix JPY rounding in invoice totals", "claude-work", 4, "921.6 MB", "5%", "41000", StatusKind.Working)
    {
    }

    public string Provider => "claude-code";

    public IStatusDotViewModel Dot { get; } = new StatusDotViewModel(Kind);
}

internal sealed class DesignOrphanViewModel(Option<JobId> job, IReadOnlyList<string> processes, bool isLeftRunning, string memory) : IOrphanViewModel
{
    public DesignOrphanViewModel()
        : this(SampleJobs.SyncQueue, ["gradle (48211)"], true, "1,126.4 MB")
    {
    }

    public Option<JobId> Job { get; } = job;

    public IReadOnlyList<string> Processes { get; } = processes;

    public bool IsLeftRunning { get; } = isLeftRunning;

    public bool CanReap => IsLeftRunning && Job.IsSome;

    public string Disposal => IsLeftRunning ? "left running" : "killed";

    public string At => "2026-10-09 15:08";

    public string Memory { get; } = memory;
}

internal sealed class DesignStaleWorktreeViewModel(string path, string reason) : IStaleWorktreeViewModel
{
    public DesignStaleWorktreeViewModel()
        : this("/home/dana/.avala/worktrees/ledger-api/switch-queue-fifo", "not known to any job")
    {
    }

    public Personal Path { get; } = Personal.Path(path);

    public string Reason { get; } = reason;
}

internal sealed class DesignResourceIndicatorViewModel : IResourceIndicatorViewModel
{
    public string Scope => Scopes.Indicator;

    public string Memory => "7,168.0 MB";

    public string Leftovers => "1 process left running · 1 stale worktree";

    public bool HasLeftovers => true;

    public IMeterViewModel Cpu { get; } = Meter();

    public IRelayCommand OpenCommand { get; } = new RelayCommand(() => { });

    private static MeterViewModel Meter()
    {
        var meter = new MeterViewModel("CPU", Option<double>.None);
        meter.Show(0.38);

        return meter;
    }
}

internal sealed class DesignResourcesViewModel : IResourcesViewModel
{
    public IReadProblemViewModel ReadProblem { get; init; } = new DesignReadProblemViewModel();

    public string Title => "Resources";

    public string Scope => Scopes.Resources;

    public IReadOnlyList<IAgentTreeViewModel> Trees { get; } =
    [
        new DesignAgentTreeViewModel("Update zod to 3.23", "claude-work", 9, "2,457.6 MB", "21%", "41030", StatusKind.Checking),
        new DesignAgentTreeViewModel(),
        new DesignAgentTreeViewModel("Add invoice PDF endpoint", "claude-work", 3, "819.2 MB", "1%", "41020", StatusKind.NeedsYou),
        new DesignAgentTreeViewModel("Fix flaky CheckoutForm test", "claude-personal", 7, "1,638.4 MB", "3%", "41010, 41011", StatusKind.NeedsYou),
    ];

    public IReadOnlyList<IOrphanViewModel> Orphans { get; } =
    [
        new DesignOrphanViewModel(),
        new DesignOrphanViewModel(Option<JobId>.None, ["postgres (39004)"], true, "180.0 MB"),
    ];

    public IReadOnlyList<IStaleWorktreeViewModel> StaleWorktrees { get; } =
    [
        new DesignStaleWorktreeViewModel(),
        new DesignStaleWorktreeViewModel("/home/dana/.avala/worktrees/ledger-api/paginate-audit-export", "missing from the disk"),
    ];

    public IReadOnlyList<string> Leases { get; } =
    [
        "41000-41009 for ~/.avala/worktrees/ledger-api/fix-jpy-rounding",
        "41010-41019 for ~/.avala/worktrees/web-console/flaky-checkout",
        "41020-41029 for ~/.avala/worktrees/ledger-api/invoice-pdf",
        "41030-41039 for ~/.avala/worktrees/web-console/zod-3-23",
    ];

    public IReadOnlyList<string> Conflicts { get; } = ["port 41012 held outside ~/.avala/worktrees/web-console/flaky-checkout"];

    public string Memory => "7,168.0 MB";

    public string Cpu => "34%";

    public int Processes => 23;

    public string ProcessCount => "23 processes";

    public string Disk => "11,264.0 MB";

    public string Ports => "41000, 41010, 41011, 41020, 41030";

    public string Error => string.Empty;

    public int LeftBehind => 5;

    public IAsyncRelayCommand<IOrphanViewModel> ReapCommand { get; } = new AsyncRelayCommand<IOrphanViewModel>(_ => Task.CompletedTask);

    public IAsyncRelayCommand ReconcileCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand CleanCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IRelayCommand OpenWorktreesCommand { get; } = new RelayCommand(() => { });
}

internal sealed class DesignWorktreesViewModel : IWorktreesViewModel
{
    public string Title => "Worktrees";

    public string Scope => WorktreePagePhrases.Scope;

    public string Space { get; init; } = "3.4 GB in 4 worktrees";

    public string Limits { get; init; } = "of a 20 GB budget · 38% of the disk free, keeping 10%";

    public string Shortage { get; init; } = string.Empty;

    public string Freed { get; init; } = string.Empty;

    public string Lifetime { get; init; } = "A worktree lives 1.6 days on average before it is freed.";

    public bool ShowsWeek => true;

    public bool ShowsMonth => false;

    public IReadOnlyList<IWorktreeRowViewModel> Largest { get; init; } =
    [
        new WorktreeRowViewModel("Add GitHub login", "82% build output", "1.9 GB"),
        new WorktreeRowViewModel("Rate-limit POST /login", "64% build output", "1.1 GB"),
    ];

    public IReadOnlyList<IWorktreeRowViewModel> Upcoming { get; init; } =
    [
        new WorktreeRowViewModel("Rate-limit POST /login", "approved, not delivered", "in 18 h"),
    ];

    public IReadOnlyList<IWorktreeDayViewModel> Days { get; init; } =
    [
        new WorktreeDayViewModel(new WorktreeDay(new DateOnly(2026, 10, 25), 3_650_722_201, 4, 2, 3, 2_147_483_648), 3_650_722_201, true),
        new WorktreeDayViewModel(new WorktreeDay(new DateOnly(2026, 10, 24), 2_147_483_648, 3, 3, 1, 734_003_200), 3_650_722_201, false),
    ];

    public IReadOnlyList<IWorktreeRowViewModel> Recovered { get; init; } =
    [
        new WorktreeRowViewModel("on its base branch", "3 worktrees", "2.4 GB"),
        new WorktreeRowViewModel("archived", "1 worktree", "0.7 GB"),
    ];

    public IReadOnlyList<IWorktreeRowViewModel> History { get; init; } =
    [
        new WorktreeRowViewModel("0199f2c4", "freed 1.2 GB, on its base branch", "10:42"),
    ];

    public IRelayCommand ShowWeekCommand { get; } = new RelayCommand(() => { });

    public IRelayCommand ShowMonthCommand { get; } = new RelayCommand(() => { });

    public IAsyncRelayCommand FreeNowCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

internal sealed class DesignWorktreeRowViewModel : IWorktreeRowViewModel
{
    public string Title { get; init; } = "Add GitHub login";

    public string Detail { get; init; } = "82% build output";

    public string Amount { get; init; } = "1.9 GB";

    public bool CanOpen { get; init; } = true;

    public IRelayCommand OpenCommand { get; } = new RelayCommand(() => { });
}

internal sealed class DesignWorktreeDayViewModel : IWorktreeDayViewModel
{
    public string Day { get; init; } = "Sun 25 Oct · today";

    public double Share { get; init; } = 0.6;

    public string Peak { get; init; } = "3.4 GB";

    public string Detail { get; init; } = "2 created · 3 freed · 2 GB recovered";

    public bool IsToday { get; init; } = true;
}
