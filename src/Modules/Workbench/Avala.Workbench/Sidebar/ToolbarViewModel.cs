using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Board;
using Avala.Workbench.Decisions;
using Avala.Workbench.Navigation;
using Avala.Workbench.NewJob;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Sidebar;

internal interface IToolbarViewModel
{
    IDecisionsViewModel Decisions { get; }

    bool IsDecisionsOpen { get; }

    int PendingDecisions { get; }

    bool HasPendingDecisions { get; }

    IRelayCommand ToggleDecisionsCommand { get; }

    IRelayCommand CloseDecisionsCommand { get; }

    IRelayCommand NewJobCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ToolbarViewModel(IDecisionsViewModel decisions, BoardFeed feed, JobFocus focus, NewJobViewModel newJob)
    : IToolbarViewModel, IActivatable, IPresentation, IDisposable
{
    public IDecisionsViewModel Decisions => decisions;

    public Task Following => feed.Following;

    public long Revision => feed.Revision;

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    [ObservableProperty]
    public partial bool IsDecisionsOpen { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingDecisions))]
    public partial int PendingDecisions { get; private set; }

    public bool HasPendingDecisions => PendingDecisions > 0;

    public void Activate()
    {
        feed.Start(Show);
        decisions.Activate();
    }

    public void Deactivate()
    {
        decisions.Deactivate();
        feed.Stop();
    }

    public void Dispose() => Deactivate();

    public IReadOnlyList<Func<CancellationToken, Task>> Show(ImmutableDictionary<JobId, BoardJob> jobs)
    {
        PendingDecisions = jobs.Values.Sum(job => job.PendingDecisions);

        return [];
    }

    [RelayCommand]
    private void ToggleDecisions()
    {
        IsDecisionsOpen = !IsDecisionsOpen;
        decisions.Refresh();
    }

    [RelayCommand]
    private void CloseDecisions() => IsDecisionsOpen = false;

    [RelayCommand]
    private void NewJob()
    {
        IsDecisionsOpen = false;
        focus.Show(newJob);
    }
}
