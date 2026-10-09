using System.Collections.ObjectModel;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Fleet;
using Avala.Workbench.Following;
using Avala.Workbench.Navigation;
using Avala.Workbench.Presenting;
using Avala.Workbench.Sidebar;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Overview;

internal sealed record DelegationState(IReadOnlyList<JobSummary> Orchestrators, Option<DelegationTree> Tree);

internal interface IDelegationViewModel : IActivatable, IPresentation
{
    IReadOnlyList<IOrchestratorViewModel> Orchestrators { get; }

    IReadOnlyList<IDelegationNodeViewModel> Children { get; }

    IReadOnlyList<IDelegationRefusalViewModel> Refused { get; }

    IOrchestratorViewModel? Selected { get; }

    IOrchestratorCardViewModel? Root { get; }

    string Caption { get; }

    IRelayCommand<IOrchestratorViewModel> SelectCommand { get; }

    IRelayCommand<JobId> OpenCommand { get; }
}

internal interface IOrchestratorViewModel
{
    JobId Job { get; }

    string Title { get; }

    JobStatus Status { get; }
}

[INotifyPropertyChanged]
internal sealed partial class DelegationViewModel(DelegationReader reader, LiveFeed feed, JobFocus focus) : IDelegationViewModel, IDisposable
{
    private readonly ObservableCollection<OrchestratorViewModel> orchestrators = [];
    private readonly ObservableCollection<DelegationNodeViewModel> children = [];
    private readonly ObservableCollection<DelegationRefusalViewModel> refused = [];
    private IOrchestratorViewModel? chosen;

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    public IReadOnlyList<IOrchestratorViewModel> Orchestrators => orchestrators;

    public IReadOnlyList<IDelegationNodeViewModel> Children => children;

    public IReadOnlyList<IDelegationRefusalViewModel> Refused => refused;

    [ObservableProperty]
    public partial IOrchestratorViewModel? Selected { get; private set; }

    [ObservableProperty]
    public partial IOrchestratorCardViewModel? Root { get; private set; }

    [ObservableProperty]
    public partial string Caption { get; private set; } = string.Empty;

    public Task Following => feed.Following;

    public void Activate() => feed.Start(ReadAsync, Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    [RelayCommand]
    private void Select(IOrchestratorViewModel? orchestrator)
    {
        if (orchestrator is not null)
        {
            Volatile.Write(ref chosen, orchestrator);
            Selected = orchestrator;
            feed.Refresh();
        }
    }

    [RelayCommand]
    private void Open(JobId job) => focus.Select(job);

    private async ValueTask<DelegationState> ReadAsync(CancellationToken cancellationToken)
    {
        var found = await reader.OrchestratorsAsync(cancellationToken);
        var root = Volatile.Read(ref chosen) is { } picked && found.FirstOrDefault(job => job.Job == picked.Job) is { } kept
            ? kept
            : found.Count > 0 ? found[^1] : null;

        return new DelegationState(
            found,
            root is null ? Option<DelegationTree>.None : await reader.TreeAsync(root.Job, cancellationToken));
    }

    private void Show(DelegationState state)
    {
        orchestrators.Reconcile(state.Orchestrators, orchestrator => orchestrator.Job, job => job.Job, job => new OrchestratorViewModel(job), (_, _) => { });
        Selected = state.Tree.Match<IOrchestratorViewModel?>(tree => orchestrators.FirstOrDefault(orchestrator => orchestrator.Job == tree.Root.Job), () => null);
        children.ShowOnly(state.Tree.Match(tree => tree.Children.Select(node => new DelegationNodeViewModel(node)), () => []));
        refused.ShowOnly(state.Tree.Match(tree => tree.Refused.Select(record => new DelegationRefusalViewModel(record)), () => []));
        Root = state.Tree.Match<IOrchestratorCardViewModel?>(tree => new OrchestratorCardViewModel(tree), () => null);
        Caption = state.Tree.Match(
            tree => $"{FactPhrases.Title(tree.Root.Instruction)} · {OverviewPhrases.Count(state.Orchestrators.Count, "orchestrator")}, {OverviewPhrases.Count(tree.Children.Count, "sub-agent")}",
            () => "No job has delegated work yet");
    }
}

internal sealed class OrchestratorViewModel(JobSummary summary) : IOrchestratorViewModel
{
    public JobId Job { get; } = summary.Job;

    public string Title { get; } = FactPhrases.Title(summary.Instruction);

    public JobStatus Status { get; } = summary.Status;
}
