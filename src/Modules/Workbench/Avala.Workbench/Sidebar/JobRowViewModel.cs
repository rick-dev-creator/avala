using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Workbench.Board;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Sidebar;

internal interface IJobRowViewModel
{
    JobId Job { get; }

    string Title { get; }

    JobStatus Status { get; }

    string Fact { get; }

    int PendingDecisions { get; }

    bool HasPendingDecisions { get; }

    bool IsSelected { get; }

    IStatusDotViewModel Dot { get; }
}

[INotifyPropertyChanged]
internal sealed partial class JobRowViewModel : IJobRowViewModel
{
    private readonly StatusDotViewModel dot = new(StatusKind.Working);

    public JobRowViewModel(BoardJob job)
    {
        Job = job.Job;
        Submitted = job.Summary.Submitted;
        Title = FactPhrases.Title(job.Summary.Instruction);
        Fact = string.Empty;
        Update(job);
    }

    public JobId Job { get; }

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

    public void Update(BoardJob job)
    {
        Status = job.Status;
        Group = job.Group;
        Fact = FactPhrases.Of(job.Fact);
        PendingDecisions = job.PendingDecisions;
        dot.Kind = FactPhrases.Dot(job);
    }
}
