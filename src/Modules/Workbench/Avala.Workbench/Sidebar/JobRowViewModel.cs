using System.Collections.ObjectModel;
using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Sidebar;

internal interface ISidebarRow
{
    JobId Job { get; }

    string Title { get; }

    string Fact { get; }

    int PendingDecisions { get; }

    bool HasPendingDecisions { get; }

    bool IsSelected { get; }

    bool IsChild { get; }

    DateTimeOffset Submitted { get; }

    IStatusDotViewModel Dot { get; }

    IReadOnlyList<ISidebarRow> Children { get; }

    bool CanArchive { get; }

    IAsyncRelayCommand ArchiveCommand { get; }

    string Refusal { get; }

    bool HasRefusal { get; }
}

internal interface ISidebarEntry : ISidebarRow
{
    JobGroup Group { get; }

    IReadOnlyList<JobId> Shelved { get; }
}

internal interface IJobRowViewModel : ISidebarEntry
{
    JobStatus Status { get; }

    bool CanMove { get; }

    IReadOnlyList<MoveTarget> MoveTargets { get; }

    bool ShowsElapsed { get; }

    DateTimeOffset Since { get; }

    bool IsOnPhone { get; }
}

[INotifyPropertyChanged]
internal sealed partial class JobRowViewModel : IJobRowViewModel
{
    private readonly StatusDotViewModel dot = new(StatusKind.Working);

    public JobRowViewModel(BoardJob job)
    {
        Job = job.Job;
        Repository = job.Summary.Repository;
        MoveTargets = [];
        Parent = job.Summary.Parent;
        Submitted = job.Summary.Submitted;
        Title = FactPhrases.Title(job.Summary.Instruction);
        Fact = string.Empty;
        Refusal = string.Empty;
        ArchiveCommand = Shelving.Idle;
        Update(job);
    }

    public JobId Job { get; }

    public Option<JobId> Parent { get; }

    public bool IsChild => Parent.IsSome;

    public string Repository { get; }

    public bool CanMove => !IsChild;

    public IReadOnlyList<JobId> Shelved => [Job];

    public bool HasRefusal => Refusal.Length > 0;

    public ObservableCollection<JobRowViewModel> Children { get; } = [];

    IReadOnlyList<ISidebarRow> ISidebarRow.Children => Children;

    public DateTimeOffset Submitted { get; }

    public string Title { get; }

    public IStatusDotViewModel Dot => dot;

    [ObservableProperty]
    public partial JobStatus Status { get; private set; }

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
    public partial IReadOnlyList<MoveTarget> MoveTargets { get; set; }

    [ObservableProperty]
    public partial bool CanArchive { get; private set; }

    [ObservableProperty]
    public partial IAsyncRelayCommand ArchiveCommand { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRefusal))]
    public partial string Refusal { get; set; }

    [ObservableProperty]
    public partial bool ShowsElapsed { get; private set; }

    [ObservableProperty]
    public partial bool IsOnPhone { get; set; }

    [ObservableProperty]
    public partial DateTimeOffset Since { get; private set; }

    public void Update(BoardJob job)
    {
        Status = job.Status;
        Group = job.Group;
        Fact = FactPhrases.Of(job.Fact);
        PendingDecisions = job.PendingDecisions;
        dot.Kind = FactPhrases.Dot(job);
        ShowsElapsed = job.Status == JobStatus.Running && job.Transcript.LiveSince.IsSome;
        Since = job.Transcript.LiveSince.Match(since => since, () => Since);
        CanArchive = !IsChild && job.Group != JobGroup.NeedsYou && Shelving.Archivable(job.Status);
        Refusal = CanArchive ? string.Empty : Refusal;
    }
}
