using System.Collections.ObjectModel;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Following;
using Avala.Workbench.Presenting;
using Avala.Workbench.Spending;

namespace Avala.Workbench.Usage;

internal sealed record UsageState(SpendingState Spending, IReadOnlyList<UsageWindow> Windows);

internal interface IUsageViewModel
{
    string Title { get; }

    IReadOnlyList<IConnectionMeterViewModel> Connections { get; }

    IReadOnlyList<IUsageWindowViewModel> Windows { get; }

    IReadOnlyList<IJobMeterViewModel> Jobs { get; }

    IReadOnlyList<IInterventionViewModel> Interventions { get; }
}

internal sealed class UsageViewModel(UsageReader reader, UsageWindows windows, LiveFeed feed) : IUsageViewModel, IPage, IActivatable, IPresentation, IDisposable
{
    private readonly ObservableCollection<ConnectionMeterViewModel> connections = [];
    private readonly ObservableCollection<UsageWindowViewModel> periods = [];
    private readonly ObservableCollection<JobMeterViewModel> jobs = [];
    private readonly ObservableCollection<InterventionViewModel> interventions = [];

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    public string Title => "Usage";

    public string Icon => "IconUsage";

    public IReadOnlyList<IConnectionMeterViewModel> Connections => connections;

    public IReadOnlyList<IUsageWindowViewModel> Windows => periods;

    public IReadOnlyList<IJobMeterViewModel> Jobs => jobs;

    public IReadOnlyList<IInterventionViewModel> Interventions => interventions;

    public Task Following => feed.Following;

    public void Activate() => feed.Start(ReadAsync, Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    private async ValueTask<UsageState> ReadAsync(CancellationToken cancellationToken) =>
        new(reader.Read(), await windows.ReadAsync(cancellationToken));

    private void Show(UsageState state)
    {
        var titles = state.Spending.Jobs.ToDictionary(cost => cost.Job.Job, cost => cost.Job.Summary.Instruction);
        connections.ShowOnly(state.Spending.Connections.Select(connection => new ConnectionMeterViewModel(connection)));
        periods.ShowOnly(state.Windows.Select(window => new UsageWindowViewModel(window)));
        jobs.ShowOnly(state.Spending.Jobs.Select(cost => new JobMeterViewModel(cost)));
        interventions.ShowOnly(state.Spending.Interventions.Select(intervention => new InterventionViewModel(intervention, titles[intervention.Job])));
    }
}
