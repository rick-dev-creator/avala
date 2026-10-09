using System.Collections.ObjectModel;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Fleet;
using Avala.Workbench.Following;
using Avala.Workbench.Presenting;
using Avala.Workbench.Sidebar;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Overview;

internal sealed record DelegationState(IReadOnlyList<JobSummary> Orchestrators, Option<DelegationTree> Tree);

[INotifyPropertyChanged]
internal sealed partial class DelegationViewModel(DelegationReader reader, LiveFeed feed) : IPresentation, IDisposable
{
    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    private JobSummary? chosen;

    public ObservableCollection<OrchestratorViewModel> Orchestrators { get; } = [];

    public ObservableCollection<DelegationNodeViewModel> Children { get; } = [];

    public ObservableCollection<DelegationRefusalViewModel> Refused { get; } = [];

    [ObservableProperty]
    public partial OrchestratorViewModel? Selected { get; private set; }

    public Task Following => feed.Following;

    public void Activate() => feed.Start(ReadAsync, Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    [RelayCommand]
    private void Select(OrchestratorViewModel orchestrator)
    {
        Volatile.Write(ref chosen, orchestrator.Summary);
        Selected = orchestrator;
        feed.Refresh();
    }

    private async ValueTask<DelegationState> ReadAsync(CancellationToken cancellationToken)
    {
        var orchestrators = await reader.OrchestratorsAsync(cancellationToken);
        var root = Volatile.Read(ref chosen) is { } picked && orchestrators.Any(job => job.Job == picked.Job)
            ? picked
            : orchestrators.Count > 0 ? orchestrators[^1] : null;

        return new DelegationState(
            orchestrators,
            root is null ? Option<DelegationTree>.None : await reader.TreeAsync(root.Job, cancellationToken));
    }

    private void Show(DelegationState state)
    {
        Orchestrators.ShowOnly(state.Orchestrators.Select(job => new OrchestratorViewModel(job)));
        Selected = state.Tree.Match(tree => Orchestrators.FirstOrDefault(orchestrator => orchestrator.Job == tree.Root.Job), () => null);
        Children.ShowOnly(state.Tree.Match(tree => tree.Children.Select(node => new DelegationNodeViewModel(node)), () => []));
        Refused.ShowOnly(state.Tree.Match(tree => tree.Refused.Select(record => new DelegationRefusalViewModel(record)), () => []));
    }
}

internal sealed class OrchestratorViewModel(JobSummary summary)
{
    public JobSummary Summary { get; } = summary;

    public JobId Job { get; } = summary.Job;

    public string Title { get; } = FactPhrases.Title(summary.Instruction);

    public JobStatus Status { get; } = summary.Status;
}
