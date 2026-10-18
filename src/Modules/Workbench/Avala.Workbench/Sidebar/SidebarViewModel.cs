using System.Collections.Immutable;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Projects.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Board;
using Avala.Workbench.Navigation;
using Avala.Workbench.Presenting;
using Avala.Workbench.Projects;
using Avala.Workbench.Submitting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Sidebar;

internal interface ISidebarViewModel : ISidebarCommands
{
    IReadOnlyList<ISidebarEntry> NeedsYou { get; }

    IReadOnlyList<IProjectGroupViewModel> Projects { get; }

    IProjectGroupViewModel Chats { get; }

    ISidebarRow? Selected { get; }

    bool IsEmpty { get; }

    IAsyncRelayCommand CreateProjectCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class SidebarViewModel : ISidebarViewModel, IActivatable, IPresentation, IDisposable
{
    private readonly BoardFeed feed;
    private readonly JobFocus focus;
    private readonly ProjectShelf shelf;
    private readonly SidebarShelves shelves;
    private readonly Dictionary<JobId, JobRowViewModel> rows = [];
    private readonly Dictionary<ContestId, (ContestRowViewModel Row, JobId First, string Repository)> contests = [];
    private ImmutableDictionary<JobId, BoardJob> shown = ImmutableDictionary<JobId, BoardJob>.Empty;
    private ImmutableDictionary<JobId, BoardContest> shownContests = ImmutableDictionary<JobId, BoardContest>.Empty;
    private ProjectMap? shownMap;
    private ImmutableDictionary<JobId, Documentation.Contracts.JobDocumentation>? shownDocs;
    private ImmutableDictionary<JobId, BoardRemote> shownRemotes = ImmutableDictionary<JobId, BoardRemote>.Empty;
    private ImmutableHashSet<JobId> shownDrafts = [];
    private object? selected;

    public SidebarViewModel(BoardFeed feed, JobFocus focus, ScratchFolders scratch, ProjectShelf shelf)
    {
        this.feed = feed;
        this.focus = focus;
        this.shelf = shelf;
        shelves = new SidebarShelves(shelf, scratch, SelectCommand);
    }

    public IReadOnlyList<ISidebarEntry> NeedsYou => shelves.NeedsYou;

    public IReadOnlyList<IProjectGroupViewModel> Projects => shelves.Projects;

    public IProjectGroupViewModel Chats => shelves.Chats;

    public Task Following => feed.Following;

    public long Revision => feed.Revision;

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public ISidebarRow? Selected => selected as ISidebarRow;

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; } = true;

    public void Activate()
    {
        feed.Start(Show);
        _ = feed.LoadAsync(shelf.CollapsedAsync, shelves.Collapse);
    }

    public void Deactivate() => feed.Stop();

    public void Dispose() => Deactivate();

    public IReadOnlyList<Func<CancellationToken, Task>> Show(ImmutableDictionary<JobId, BoardJob> jobs)
    {
        var contested = feed.Contests;
        var map = feed.Projects;
        var regroup = !ReferenceEquals(map, shownMap);
        var changed = jobs.Values.Where(job => !shown.TryGetValue(job.Job, out var before) || !ReferenceEquals(before, job)).ToList();

        if (regroup)
        {
            shelves.Show(map);
        }

        var added = changed.Where(job => !rows.ContainsKey(job.Job)).Select(job => job.Job).ToHashSet();

        foreach (var job in changed)
        {
            Place(job, contested);
        }

        foreach (var contest in Touched(contested, changed))
        {
            Contest(contest, jobs);
        }

        if (regroup)
        {
            Regroup();
        }

        if ((!ReferenceEquals(feed.Docs, shownDocs) || changed.Any(job => job.Status is JobStatus.Approved or JobStatus.Discarded)))
        {
            shelves.RefreshDocs();
        }

        Phones(jobs, added);
        Drafts(added);
        shelves.Prune();
        shelves.Attend();
        Target(regroup ? rows.Values : rows.Values.Where(row => added.Contains(row.Job)));
        shown = jobs;
        shownContests = contested;
        shownMap = map;
        shownDocs = feed.Docs;
        IsEmpty = shelves.IsEmpty;

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
                other.Row.Highlight(row);
            }

            selected = row;
            OnPropertyChanged(nameof(Selected));
            focus.Select(row.Job);
        }
    }

    [RelayCommand]
    private async Task CreateProjectAsync(CancellationToken cancellationToken)
    {
        if ((await shelf.CreateAsync(cancellationToken)).TryGetValue(out var project, out _))
        {
            shelves.RenameWhenShown(project);
        }
    }

    private async Task MoveToNewAsync(JobId job, CancellationToken cancellationToken)
    {
        if ((await shelf.CreateAsync(cancellationToken)).TryGetValue(out var project, out _))
        {
            await shelf.MoveAsync(job, project, cancellationToken);
            shelves.RenameWhenShown(project);
        }
    }

    private void Target(IEnumerable<JobRowViewModel> targeted)
    {
        foreach (var row in targeted.Where(row => row.CanMove))
        {
            row.ArchiveCommand = row.ArchiveCommand == Shelving.Idle ? new AsyncRelayCommand(token => ArchiveAsync(row, refusal => row.Refusal = refusal, token)) : row.ArchiveCommand;
            var current = shelves.Map.Of(row.Job, row.Repository).Map(project => project.Project);
            var job = row.Job;

            row.MoveTargets =
            [
                .. shelves.Map.Projects
                    .Where(project => current != project.Project)
                    .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(project => MoveTarget.To(project.Name, token => shelf.MoveAsync(job, project.Project, token))),
                MoveTarget.To(ProjectPhrases.NewProject, token => MoveToNewAsync(job, token)),
            ];
        }
    }

    private async Task ArchiveAsync(ISidebarEntry entry, Action<string> refuse, CancellationToken cancellationToken) =>
        refuse((await shelf.ArchiveAsync(entry.Shelved, cancellationToken)).Match(ProjectPhrases.Refusal, () => string.Empty));

    private void Shelve(ISidebarEntry entry, string key)
    {
        if (!entry.Shelved.Any(shelves.Map.Archived.Contains))
        {
            shelves.Place(entry, key);

            return;
        }

        var neighbour = ReferenceEquals(selected, entry) ? shelves.NeighbourOf(entry) : Option<ISidebarEntry>.None;
        shelves.Remove(entry);

        if (neighbour.Match<ISidebarEntry?>(next => next, () => null) is { } next)
        {
            Select(next);
        }
    }

    private void Phones(ImmutableDictionary<JobId, BoardJob> jobs, HashSet<JobId> added)
    {
        var remotes = feed.Remotes;

        foreach (var row in rows.Values.Where(row => added.Contains(row.Job) || !ReferenceEquals(shownRemotes.GetValueOrDefault(row.Job), remotes.GetValueOrDefault(row.Job))))
        {
            row.IsOnPhone = !row.IsChild && remotes.GetValueOrDefault(row.Job, BoardRemote.None).Wanted.Match(
                on => on,
                () => jobs.TryGetValue(row.Job, out var job) && job.Summary.RemoteControlled);
        }

        shownRemotes = remotes;
    }

    private void Drafts(HashSet<JobId> added)
    {
        var drafted = feed.Drafted;

        foreach (var row in rows.Values.Where(row => added.Contains(row.Job) || !ReferenceEquals(drafted, shownDrafts)))
        {
            row.HasDraft = drafted.Contains(row.Job);
        }

        shownDrafts = drafted;
    }

    private IEnumerable<ContestRecord> Touched(ImmutableDictionary<JobId, BoardContest> contested, List<BoardJob> changed) =>
        contested.Values
            .Where(found => !shownContests.TryGetValue(found.Contest.Candidates[0].Job, out var before) || !ReferenceEquals(before, found)
                || found.Contest.Candidates.Any(candidate => changed.Exists(job => job.Job == candidate.Job)))
            .Select(found => found.Contest)
            .DistinctBy(contest => contest.Contest);

    private void Place(BoardJob job, ImmutableDictionary<JobId, BoardContest> contested)
    {
        if (contested.ContainsKey(job.Job) || job.Summary.Companion)
        {
            Withdraw(job.Job);

            return;
        }

        if (rows.TryGetValue(job.Job, out var row))
        {
            row.Update(job);

            if (OwnerOf(row) is null)
            {
                Locate(row);
            }

            return;
        }

        row = new JobRowViewModel(job);
        rows.Add(job.Job, row);
        Insert(row);
        Adopt(row);
    }

    private void Regroup()
    {
        foreach (var row in rows.Values.Where(row => OwnerOf(row) is null))
        {
            Locate(row);
        }

        foreach (var contest in contests.Values)
        {
            Shelve(contest.Row, shelves.KeyOf(contest.First, contest.Repository));
        }
    }

    private void Withdraw(JobId candidate)
    {
        if (rows.Remove(candidate, out var row))
        {
            if (OwnerOf(row) is { } owner)
            {
                owner.Children.Remove(row);
            }
            else
            {
                shelves.Remove(row);
            }
        }
    }

    private void Contest(ContestRecord contest, ImmutableDictionary<JobId, BoardJob> jobs)
    {
        var row = contests.TryGetValue(contest.Contest, out var known) ? known.Row : new ContestRowViewModel(contest);
        contests[contest.Contest] = (row, contest.Candidates[0].Job, contest.Repository);

        foreach (var candidate in contest.Candidates)
        {
            Withdraw(candidate.Job);
        }

        row.Update(contest, jobs);
        row.ArchiveCommand = row.ArchiveCommand == Shelving.Idle ? new AsyncRelayCommand(token => ArchiveAsync(row, refusal => row.Refusal = refusal, token)) : row.ArchiveCommand;
        Shelve(row, shelves.KeyOf(contest.Candidates[0].Job, contest.Repository));
    }

    private void Insert(JobRowViewModel row)
    {
        if (OwnerOf(row) is { } owner)
        {
            owner.Children.Insert(owner.Children.TakeWhile(other => other.Submitted <= row.Submitted).Count(), row);

            return;
        }

        Locate(row);
    }

    private void Locate(JobRowViewModel row) => Shelve(row, shelves.KeyOf(row.Job, row.Repository));

    private void Adopt(JobRowViewModel owner)
    {
        foreach (var child in rows.Values.Where(other => other.Parent == owner.Job).ToList())
        {
            shelves.Remove(child);
            Insert(child);
        }
    }

    private JobRowViewModel? OwnerOf(JobRowViewModel row) =>
        row.Parent.Match(parent => rows.GetValueOrDefault(parent), () => null);
}
