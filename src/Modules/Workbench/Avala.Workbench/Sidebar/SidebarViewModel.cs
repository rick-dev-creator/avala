using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Board;
using Avala.Workbench.Navigation;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Sidebar;

internal interface ISidebarViewModel
{
    IReadOnlyList<IJobRowViewModel> NeedsYou { get; }

    IReadOnlyList<IJobRowViewModel> Running { get; }

    IReadOnlyList<IJobRowViewModel> ReadyForReview { get; }

    IReadOnlyList<IJobRowViewModel> Done { get; }

    IJobRowViewModel? Selected { get; }

    bool IsEmpty { get; }

    IRelayCommand<IJobRowViewModel> SelectCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class SidebarViewModel : ISidebarViewModel, IActivatable, IPresentation, IDisposable
{
    private readonly BoardFeed feed;
    private readonly JobFocus focus;
    private readonly Dictionary<JobId, JobRowViewModel> rows = [];
    private readonly ObservableCollection<JobRowViewModel> needsYou = [];
    private readonly ObservableCollection<JobRowViewModel> running = [];
    private readonly ObservableCollection<JobRowViewModel> readyForReview = [];
    private readonly ObservableCollection<JobRowViewModel> done = [];
    private ImmutableDictionary<JobId, BoardJob> shown = ImmutableDictionary<JobId, BoardJob>.Empty;

    public SidebarViewModel(BoardFeed feed, JobFocus focus)
    {
        this.feed = feed;
        this.focus = focus;
    }

    public IReadOnlyList<IJobRowViewModel> NeedsYou => needsYou;

    public IReadOnlyList<IJobRowViewModel> Running => running;

    public IReadOnlyList<IJobRowViewModel> ReadyForReview => readyForReview;

    public IReadOnlyList<IJobRowViewModel> Done => done;

    public Task Following => feed.Following;

    public long Revision => feed.Revision;

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    [ObservableProperty]
    public partial IJobRowViewModel? Selected { get; private set; }

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; } = true;

    public void Activate() => feed.Start(Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => Deactivate();

    public IReadOnlyList<Func<CancellationToken, Task>> Show(ImmutableDictionary<JobId, BoardJob> jobs)
    {
        foreach (var job in jobs.Values.Where(job => !shown.TryGetValue(job.Job, out var before) || !ReferenceEquals(before, job)))
        {
            Place(job);
        }

        shown = jobs;
        IsEmpty = rows.Count == 0;

        return [];
    }

    [RelayCommand]
    private void Select(IJobRowViewModel? row)
    {
        if (row is not null)
        {
            foreach (var other in rows.Values)
            {
                other.IsSelected = other.Job == row.Job;
            }

            Selected = row;
            focus.Select(row.Job);
        }
    }

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
        JobGroup.NeedsYou => needsYou,
        JobGroup.ReadyForReview => readyForReview,
        JobGroup.Done => done,
        _ => running,
    };
}
