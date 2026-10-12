using System.Collections.Immutable;
using System.Collections.ObjectModel;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Board;
using Avala.Workbench.Navigation;
using Avala.Workbench.Presenting;
using Avala.Workbench.Submitting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Sidebar;

internal interface ISidebarViewModel
{
    IReadOnlyList<ISidebarEntry> NeedsYou { get; }

    IReadOnlyList<ISidebarEntry> Running { get; }

    IReadOnlyList<ISidebarEntry> Waiting { get; }

    IReadOnlyList<ISidebarEntry> ReadyForReview { get; }

    IReadOnlyList<ISidebarEntry> Done { get; }

    ISidebarRow? Selected { get; }

    bool IsEmpty { get; }

    IRelayCommand<object> SelectCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class SidebarViewModel : ISidebarViewModel, IActivatable, IPresentation, IDisposable
{
    private readonly BoardFeed feed;
    private readonly JobFocus focus;
    private readonly ScratchFolders scratch;
    private readonly Dictionary<JobId, JobRowViewModel> rows = [];
    private readonly Dictionary<ContestId, ContestRowViewModel> contests = [];
    private readonly ObservableCollection<ISidebarEntry> needsYou = [];
    private readonly ObservableCollection<ISidebarEntry> running = [];
    private readonly ObservableCollection<ISidebarEntry> waiting = [];
    private readonly ObservableCollection<ISidebarEntry> readyForReview = [];
    private readonly ObservableCollection<ISidebarEntry> done = [];
    private ImmutableDictionary<JobId, BoardJob> shown = ImmutableDictionary<JobId, BoardJob>.Empty;
    private ImmutableDictionary<JobId, BoardContest> shownContests = ImmutableDictionary<JobId, BoardContest>.Empty;

    public SidebarViewModel(BoardFeed feed, JobFocus focus, ScratchFolders scratch)
    {
        this.feed = feed;
        this.focus = focus;
        this.scratch = scratch;
    }

    public IReadOnlyList<ISidebarEntry> NeedsYou => needsYou;

    public IReadOnlyList<ISidebarEntry> Running => running;

    public IReadOnlyList<ISidebarEntry> Waiting => waiting;

    public IReadOnlyList<ISidebarEntry> ReadyForReview => readyForReview;

    public IReadOnlyList<ISidebarEntry> Done => done;

    public Task Following => feed.Following;

    public long Revision => feed.Revision;

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    private object? selected;

    public ISidebarRow? Selected => selected as ISidebarRow;

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; } = true;

    public void Activate() => feed.Start(Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => Deactivate();

    public IReadOnlyList<Func<CancellationToken, Task>> Show(ImmutableDictionary<JobId, BoardJob> jobs)
    {
        var contested = feed.Contests;
        var changed = jobs.Values.Where(job => !shown.TryGetValue(job.Job, out var before) || !ReferenceEquals(before, job)).ToList();

        foreach (var job in changed)
        {
            Place(job, contested);
        }

        foreach (var contest in Touched(contested, changed))
        {
            Contest(contest, jobs);
        }

        shown = jobs;
        shownContests = contested;
        IsEmpty = rows.Count == 0 && contests.Count == 0;

        return [];
    }

    [RelayCommand]
    private void Select(object? chosen)
    {
        if (chosen is ISidebarRow row)
        {
            foreach (var other in rows.Values)
            {
                other.IsSelected = ReferenceEquals(other, row);
            }

            foreach (var other in contests.Values)
            {
                other.Highlight(row);
            }

            selected = row;
            OnPropertyChanged(nameof(Selected));
            focus.Select(row.Job);
        }
    }

    private IEnumerable<ContestRecord> Touched(ImmutableDictionary<JobId, BoardContest> contested, List<BoardJob> changed) =>
        contested.Values
            .Where(found => !shownContests.TryGetValue(found.Contest.Candidates[0].Job, out var before) || !ReferenceEquals(before, found)
                || found.Contest.Candidates.Any(candidate => changed.Exists(job => job.Job == candidate.Job)))
            .Select(found => found.Contest)
            .DistinctBy(contest => contest.Contest);

    private void Place(BoardJob job, ImmutableDictionary<JobId, BoardContest> contested)
    {
        if (contested.ContainsKey(job.Job))
        {
            Withdraw(job.Job);

            return;
        }

        if (rows.TryGetValue(job.Job, out var row))
        {
            var before = row.Group;
            row.Update(job);

            if (before != row.Group && OwnerOf(row) is null)
            {
                GroupOf(before).Remove(row);
                Insert(row);
            }

            return;
        }

        row = new JobRowViewModel(job, scratch.Holds(job.Summary.Repository));
        rows.Add(job.Job, row);
        Insert(row);
        Adopt(row);
    }

    private void Withdraw(JobId candidate)
    {
        if (rows.Remove(candidate, out var row))
        {
            _ = OwnerOf(row) is { } owner ? owner.Children.Remove(row) : GroupOf(row.Group).Remove(row);
        }
    }

    private void Contest(ContestRecord contest, ImmutableDictionary<JobId, BoardJob> jobs)
    {
        var known = contests.TryGetValue(contest.Contest, out var row);
        row ??= new ContestRowViewModel(contest);
        contests[contest.Contest] = row;
        var before = row.Group;

        foreach (var candidate in contest.Candidates)
        {
            Withdraw(candidate.Job);
        }

        row.Update(contest, jobs);

        if (!known || before != row.Group)
        {
            GroupOf(before).Remove(row);
            Insert(row, row.Group);
        }
    }

    private void Insert(JobRowViewModel row)
    {
        if (OwnerOf(row) is { } owner)
        {
            owner.Children.Insert(owner.Children.TakeWhile(other => other.Submitted <= row.Submitted).Count(), row);

            return;
        }

        Insert(row, row.Group);
    }

    private void Insert(ISidebarEntry row, JobGroup at)
    {
        var group = GroupOf(at);
        group.Insert(group.TakeWhile(other => other.Submitted >= row.Submitted).Count(), row);
    }

    private void Adopt(JobRowViewModel owner)
    {
        foreach (var child in rows.Values.Where(other => other.Parent == owner.Job).ToList())
        {
            GroupOf(child.Group).Remove(child);
            Insert(child);
        }
    }

    private JobRowViewModel? OwnerOf(JobRowViewModel row) =>
        row.Parent.Match(parent => rows.GetValueOrDefault(parent), () => null);

    private ObservableCollection<ISidebarEntry> GroupOf(JobGroup group) => group switch
    {
        JobGroup.NeedsYou => needsYou,
        JobGroup.Waiting => waiting,
        JobGroup.ReadyForReview => readyForReview,
        JobGroup.Done => done,
        _ => running,
    };
}
