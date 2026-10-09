using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Resources;

internal sealed record DesignAgentTreeViewModel(string Job, string Connection, int Processes, string Memory, string Cpu, string Ports) : IAgentTreeViewModel
{
    public DesignAgentTreeViewModel()
        : this("Fix JPY rounding in invoice totals", "claude-work", 4, "412.6 MB", "18%", "41000")
    {
    }

    public string Provider => "claude-code";
}

internal sealed class DesignOrphanViewModel : IOrphanViewModel
{
    public Option<JobId> Job => SampleJobs.FlakyCheckout;

    public IReadOnlyList<string> Processes { get; } = ["node (48213)", "chrome-headless (48230)"];

    public bool IsLeftRunning => true;

    public bool CanReap => true;

    public string Disposal => "left running";

    public string At => "2026-10-09 15:08";
}

internal sealed class DesignStaleWorktreeViewModel : IStaleWorktreeViewModel
{
    public string Path => "~/.avala/worktrees/shop-web/old-checkout-spike";

    public string Reason => "not known to any job";
}

internal sealed class DesignResourceIndicatorViewModel : IResourceIndicatorViewModel
{
    public string Memory => "1,204.3 MB";

    public int Leftovers => 2;

    public bool HasLeftovers => true;
}

internal sealed class DesignResourcesViewModel : IResourcesViewModel
{
    public string Title => "Resources";

    public IReadOnlyList<IAgentTreeViewModel> Trees { get; } =
    [
        new DesignAgentTreeViewModel(),
        new DesignAgentTreeViewModel("Fix flaky CheckoutForm test", "claude-work", 7, "791.2 MB", "42%", "41010, 41011"),
    ];

    public IReadOnlyList<IOrphanViewModel> Orphans { get; } = [new DesignOrphanViewModel()];

    public IReadOnlyList<IStaleWorktreeViewModel> StaleWorktrees { get; } = [new DesignStaleWorktreeViewModel()];

    public IReadOnlyList<string> Leases { get; } = ["41000-41009 for ~/.avala/worktrees/shop-api/fix-jpy-rounding", "41010-41019 for ~/.avala/worktrees/shop-web/flaky-checkout"];

    public IReadOnlyList<string> Conflicts { get; } = [];

    public string Memory => "1,204.3 MB";

    public string Cpu => "60%";

    public int Processes => 11;

    public string Disk => "2,318.0 MB";

    public string Ports => "41000, 41010, 41011";

    public string Error => string.Empty;

    public IAsyncRelayCommand<IOrphanViewModel> ReapCommand { get; } = new AsyncRelayCommand<IOrphanViewModel>(_ => Task.CompletedTask);

    public IAsyncRelayCommand ReconcileCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand CleanCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}
