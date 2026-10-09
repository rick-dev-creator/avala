using Avala.Components.Status;
using Avala.Jobs.Contracts;
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
        : this("~/.avala/worktrees/ledger-api/switch-queue-fifo", "not known to any job")
    {
    }

    public string Path { get; } = path;

    public string Reason { get; } = reason;
}

internal sealed class DesignResourceIndicatorViewModel : IResourceIndicatorViewModel
{
    public string Memory => "7,168.0 MB";

    public int Leftovers => 2;

    public bool HasLeftovers => true;
}

internal sealed class DesignResourcesViewModel : IResourcesViewModel
{
    public string Title => "Resources";

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
        new DesignStaleWorktreeViewModel("~/.avala/worktrees/ledger-api/paginate-audit-export", "missing from the disk"),
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

    public string Disk => "11,264.0 MB";

    public string Ports => "41000, 41010, 41011, 41020, 41030";

    public string Error => string.Empty;

    public int LeftBehind => 5;

    public IAsyncRelayCommand<IOrphanViewModel> ReapCommand { get; } = new AsyncRelayCommand<IOrphanViewModel>(_ => Task.CompletedTask);

    public IAsyncRelayCommand ReconcileCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand CleanCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}
