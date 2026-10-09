using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Avala.Jobs.Contracts;
using Avala.Workbench.Board;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Sidebar;

[INotifyPropertyChanged]
internal sealed partial class SidebarViewModel
{
    private readonly Dictionary<JobId, JobRowViewModel> rows = [];
    private ImmutableDictionary<JobId, BoardJob> shown = ImmutableDictionary<JobId, BoardJob>.Empty;

    public ObservableCollection<JobRowViewModel> NeedsYou { get; } = [];

    public ObservableCollection<JobRowViewModel> Running { get; } = [];

    public ObservableCollection<JobRowViewModel> ReadyForReview { get; } = [];

    public ObservableCollection<JobRowViewModel> Done { get; } = [];

    [ObservableProperty]
    public partial JobRowViewModel? Selected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingDecisions))]
    public partial int PendingDecisions { get; private set; }

    public bool HasPendingDecisions => PendingDecisions > 0;

    public void Show(ImmutableDictionary<JobId, BoardJob> jobs)
    {
        foreach (var job in jobs.Values.Where(job => !shown.TryGetValue(job.Job, out var before) || !ReferenceEquals(before, job)))
        {
            Place(job);
        }

        shown = jobs;
        PendingDecisions = jobs.Values.Sum(job => job.PendingDecisions);
    }

    [RelayCommand]
    private void Select(JobRowViewModel row) => Selected = row;

    private void Place(BoardJob job)
    {
        if (rows.TryGetValue(job.Job, out var row))
        {
            var before = row.Group;
            row.Update(job);

            if (before != row.Group)
            {
                GroupOf(before).Remove(row);
                Insert(row);
            }

            return;
        }

        row = new JobRowViewModel(job);
        rows.Add(job.Job, row);
        Insert(row);
    }

    private void Insert(JobRowViewModel row)
    {
        var group = GroupOf(row.Group);
        var position = group.TakeWhile(other => other.Submitted >= row.Submitted).Count();
        group.Insert(position, row);
    }

    private ObservableCollection<JobRowViewModel> GroupOf(JobGroup group) => group switch
    {
        JobGroup.NeedsYou => NeedsYou,
        JobGroup.ReadyForReview => ReadyForReview,
        JobGroup.Done => Done,
        _ => Running,
    };
}
