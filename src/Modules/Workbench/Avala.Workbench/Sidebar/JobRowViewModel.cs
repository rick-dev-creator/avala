using System.Collections.ObjectModel;
using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using CommunityToolkit.Mvvm.ComponentModel;

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
}

internal interface ISidebarEntry : ISidebarRow;

internal interface IJobRowViewModel : ISidebarEntry
{
    JobStatus Status { get; }

    bool IsScratch { get; }

    bool ShowsElapsed { get; }

    DateTimeOffset Since { get; }
}

[INotifyPropertyChanged]
internal sealed partial class JobRowViewModel : IJobRowViewModel
{
    private readonly StatusDotViewModel dot = new(StatusKind.Working);

    public JobRowViewModel(BoardJob job, bool scratch = false)
    {
        Job = job.Job;
        IsScratch = scratch;
        Parent = job.Summary.Parent;
        Submitted = job.Summary.Submitted;
        Title = FactPhrases.Title(job.Summary.Instruction);
        Fact = string.Empty;
        Update(job);
    }

    public JobId Job { get; }

    public Option<JobId> Parent { get; }

    public bool IsChild => Parent.IsSome;

    public bool IsScratch { get; }

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
    public partial bool ShowsElapsed { get; private set; }

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
    }
}
