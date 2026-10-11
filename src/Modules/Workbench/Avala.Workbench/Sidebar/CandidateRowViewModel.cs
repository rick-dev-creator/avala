using System.Globalization;
using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Workbench.Board;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Sidebar;

internal interface ICandidateRowViewModel : ISidebarRow
{
    int Ordinal { get; }
}

[INotifyPropertyChanged]
internal sealed partial class CandidateRowViewModel : ICandidateRowViewModel
{
    private readonly StatusDotViewModel dot = new(StatusKind.Working);

    public CandidateRowViewModel(int ordinal, BoardJob job)
    {
        Ordinal = ordinal;
        Job = job.Job;
        Submitted = job.Summary.Submitted;
        Title = string.Create(CultureInfo.InvariantCulture, $"Candidate {ordinal}");
        Fact = string.Empty;
        Update(job);
    }

    public int Ordinal { get; }

    public JobId Job { get; }

    public string Title { get; }

    public DateTimeOffset Submitted { get; }

    public bool IsChild => true;

    public IStatusDotViewModel Dot => dot;

    public IReadOnlyList<ISidebarRow> Children => [];

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
        Fact = FactPhrases.Of(job.Fact);
        PendingDecisions = job.PendingDecisions;
        dot.Kind = FactPhrases.Dot(job);
    }
}
