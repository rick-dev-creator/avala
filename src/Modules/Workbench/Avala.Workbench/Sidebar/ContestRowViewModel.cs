using System.Collections.ObjectModel;
using Avala.Components.Status;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Verification.Contracts;
using Avala.Workbench.Board;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Sidebar;

internal interface IContestRowViewModel : ISidebarEntry
{
    bool IsExpanded { get; }

    IRelayCommand ToggleCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ContestRowViewModel : IContestRowViewModel
{
    private readonly StatusDotViewModel dot = new(StatusKind.Working);
    private readonly List<CandidateRowViewModel> candidates = [];

    public ContestRowViewModel(ContestRecord contest)
    {
        Contest = contest.Contest;
        Submitted = contest.Started;
        Title = FactPhrases.Title(contest.Instruction);
        Fact = string.Empty;
        Job = contest.Candidates[0].Job;
        Shelved = [.. contest.Candidates.Select(candidate => candidate.Job)];
        Refusal = string.Empty;
        ArchiveCommand = Shelving.Idle;
    }

    public ContestId Contest { get; }

    public string Title { get; }

    public DateTimeOffset Submitted { get; }

    public bool IsChild => false;

    public IStatusDotViewModel Dot => dot;

    public ObservableCollection<CandidateRowViewModel> Children { get; } = [];

    IReadOnlyList<ISidebarRow> ISidebarRow.Children => Children;

    public IReadOnlyList<CandidateRowViewModel> Candidates => candidates;

    public IReadOnlyList<JobId> Shelved { get; private set; }

    public bool HasRefusal => Refusal.Length > 0;

    [ObservableProperty]
    public partial bool CanArchive { get; private set; }

    [ObservableProperty]
    public partial IAsyncRelayCommand ArchiveCommand { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRefusal))]
    public partial string Refusal { get; set; }

    [ObservableProperty]
    public partial JobId Job { get; private set; }

    [ObservableProperty]
    public partial JobGroup Group { get; private set; }

    [ObservableProperty]
    public partial string Fact { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingDecisions))]
    public partial int PendingDecisions { get; private set; }

    public bool HasPendingDecisions => PendingDecisions > 0;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; private set; }

    public void Highlight(ISidebarRow selected)
    {
        IsSelected = ReferenceEquals(this, selected);

        foreach (var candidate in candidates)
        {
            candidate.IsSelected = ReferenceEquals(candidate, selected);
        }
    }

    public void Update(ContestRecord contest, IReadOnlyDictionary<JobId, BoardJob> jobs)
    {
        foreach (var candidate in contest.Candidates.Where(candidate => jobs.ContainsKey(candidate.Job)))
        {
            Show(candidate, jobs[candidate.Job]);
        }

        BoardJob[] shown = [.. contest.Candidates.Select(candidate => jobs.GetValueOrDefault(candidate.Job)).OfType<BoardJob>()];
        Job = contest.Picked.Match(picked => picked, () => contest.Verdict.Bind(verdict => verdict.Winner).Match(winner => winner, () => contest.Candidates[0].Job));
        PendingDecisions = shown.Sum(job => job.PendingDecisions);
        Group = GroupOf(contest, PendingDecisions);
        Shelved = [.. contest.Candidates.Select(candidate => candidate.Job)];
        CanArchive = shown.Length == contest.Candidates.Count && shown.All(job => Shelving.Archivable(job.Status));
        Fact = ContestPhrases.Row(contest, contest.Verdict.Match(verdict => verdict.Ranking.Count(standing => standing.Facts.Passed), () => shown.Count(Passed)));
        dot.Kind = Group switch
        {
            JobGroup.NeedsYou => StatusKind.NeedsYou,
            JobGroup.ReadyForReview => StatusKind.ReadyForReview,
            JobGroup.Done => contest.Status == ContestStatus.Cancelled ? StatusKind.Failed : StatusKind.Done,
            _ => contest.Status == ContestStatus.Judging ? StatusKind.Checking : StatusKind.Working,
        };
    }

    [RelayCommand]
    private void Toggle()
    {
        IsExpanded = !IsExpanded;
        Children.Clear();

        foreach (var candidate in IsExpanded ? candidates : [])
        {
            Children.Add(candidate);
        }
    }

    private void Show(CandidateRecord entry, BoardJob job)
    {
        if (candidates.Find(known => known.Job == entry.Job) is { } known)
        {
            known.Update(job);

            return;
        }

        var added = new CandidateRowViewModel(entry.Ordinal, job);
        var position = candidates.Count(other => other.Ordinal < entry.Ordinal);
        candidates.Insert(position, added);

        if (IsExpanded)
        {
            Children.Insert(position, added);
        }
    }

    private static JobGroup GroupOf(ContestRecord contest, int pending) =>
        pending > 0 ? JobGroup.NeedsYou
        : contest.Status switch
        {
            ContestStatus.Undecided => JobGroup.NeedsYou,
            ContestStatus.Decided => JobGroup.ReadyForReview,
            ContestStatus.Closed or ContestStatus.Cancelled => JobGroup.Done,
            _ => JobGroup.Running,
        };

    private static bool Passed(BoardJob job) =>
        job.Status is JobStatus.AwaitingReview or JobStatus.Approved
        && job.Verification.Match(report => report.Outcome == VerificationOutcome.Passed, () => false);
}
