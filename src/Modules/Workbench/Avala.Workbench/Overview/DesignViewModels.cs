using Avala.Jobs.Contracts;
using Avala.Sdk.Presentation;
using Avala.Workbench.Presenting;
using Avala.Workbench.Usage;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Overview;

internal sealed class DesignAgentViewModel(JobId job, string title, JobStatus status, string fact) : IAgentViewModel
{
    public DesignAgentViewModel()
        : this(SampleJobs.JpyRounding, "Fix JPY rounding in invoice totals", JobStatus.Running, "2 of 4")
    {
    }

    public JobId Job { get; } = job;

    public string Title { get; } = title;

    public JobStatus Status { get; } = status;

    public string Fact { get; } = fact;
}

internal sealed class DesignConnectionCardViewModel(string name, string account, bool isDefault, string cost, ILimitViewModel limit, IReadOnlyList<IAgentViewModel> agents) : IConnectionCardViewModel
{
    public DesignConnectionCardViewModel()
        : this(
            "claude-work",
            "rick@acme.dev",
            true,
            "3.2140 USD",
            new DesignLimitViewModel(),
            [new DesignAgentViewModel(), new DesignAgentViewModel(SampleJobs.FlakyCheckout, "Fix flaky CheckoutForm test", JobStatus.Running, "wants to run a command")])
    {
    }

    public string Name { get; } = name;

    public string Provider => "Claude Code";

    public string Account { get; } = account;

    public bool IsDefault { get; } = isDefault;

    public string Cost { get; } = cost;

    public IReadOnlyList<ILimitViewModel> Limits { get; } = [limit];

    public IReadOnlyList<IAgentViewModel> Agents { get; } = agents;
}

internal sealed class DesignConnectionsViewModel : IConnectionsViewModel
{
    public IReadOnlyList<IConnectionCardViewModel> Connections { get; } =
    [
        new DesignConnectionCardViewModel(),
        new DesignConnectionCardViewModel(
            "claude-personal",
            "rick@hey.com",
            false,
            "0.8400 USD",
            new DesignLimitViewModel("5h", 0.31, "31% used", "resets 2026-10-09 17:05", string.Empty, false),
            [new DesignAgentViewModel(SampleJobs.LodashUpdate, "Update lodash to 4.17.21", JobStatus.Checking, "verifying")]),
    ];

    public string FileNote => string.Empty;

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
        : this(SampleJobs.CheckoutSplit, "Split CheckoutPage into steps", JobStatus.Running)
    {
    }

    public JobId Job { get; } = job;

    public string Title { get; } = title;

    public JobStatus Status { get; } = status;
}

internal sealed class DesignDelegationNodeViewModel(string title, int depth, JobStatus status, string connection, string activity, string spent) : IDelegationNodeViewModel
{
    public DesignDelegationNodeViewModel()
        : this("Extract the address step", 1, JobStatus.Approved, "claude-work", "integrated into its parent", "0.3100 USD")
    {
    }

    public JobId Job => SampleJobs.InvoicePdf;

    public string Title { get; } = title;

    public int Depth { get; } = depth;

    public JobStatus Status { get; } = status;

    public string Connection { get; } = connection;

    public string Harness => "Claude Code";

    public string Activity { get; } = activity;

    public string Spent { get; } = spent;

    public string Carve => "1 USD";
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
        new DesignDelegationNodeViewModel("Extract the payment step", 1, JobStatus.Running, "claude-personal", "2 of 3", "0.1200 USD"),
        new DesignDelegationNodeViewModel("Write the payment step's tests", 2, JobStatus.Preparing, "claude-personal", "starting", "no cost"),
    ];

    public IReadOnlyList<IDelegationRefusalViewModel> Refused { get; } = [new DesignDelegationRefusalViewModel()];

    public IOrchestratorViewModel? Selected => Orchestrators[0];

    public IRelayCommand<IOrchestratorViewModel> SelectCommand { get; } = new RelayCommand<IOrchestratorViewModel>(_ => { });

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
