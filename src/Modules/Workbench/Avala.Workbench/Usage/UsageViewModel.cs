using System.Collections.ObjectModel;
using Avala.Sdk;
using Avala.Workbench.Following;
using Avala.Workbench.Presenting;
using Avala.Workbench.Spending;

namespace Avala.Workbench.Usage;

internal sealed record UsageState(SpendingState Spending, IReadOnlyList<UsageWindow> Windows);

internal sealed class UsageViewModel(UsageReader reader, UsageWindows windows, LiveFeed feed) : IPage, IActivatable, IDisposable
{
    public string Title => "Usage";

    public ObservableCollection<ConnectionMeterViewModel> Connections { get; } = [];

    public ObservableCollection<UsageWindowViewModel> Windows { get; } = [];

    public ObservableCollection<JobMeterViewModel> Jobs { get; } = [];

    public ObservableCollection<InterventionViewModel> Interventions { get; } = [];

    public Task Following => feed.Following;

    public void Activate() => feed.Start(ReadAsync, Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    private async ValueTask<UsageState> ReadAsync(CancellationToken cancellationToken) =>
        new(reader.Read(), await windows.ReadAsync(cancellationToken));

    private void Show(UsageState state)
    {
        var titles = state.Spending.Jobs.ToDictionary(cost => cost.Job.Job, cost => cost.Job.Summary.Instruction);
        Connections.ShowOnly(state.Spending.Connections.Select(connection => new ConnectionMeterViewModel(connection)));
        Windows.ShowOnly(state.Windows.Select(window => new UsageWindowViewModel(window)));
        Jobs.ShowOnly(state.Spending.Jobs.Select(cost => new JobMeterViewModel(cost)));
        Interventions.ShowOnly(state.Spending.Interventions.Select(intervention => new InterventionViewModel(intervention, titles[intervention.Job])));
    }
}
