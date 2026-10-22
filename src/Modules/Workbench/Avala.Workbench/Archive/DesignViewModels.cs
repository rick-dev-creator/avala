using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Archive;

internal sealed record DesignArchivedRowViewModel(JobId Job, string Title, string Status, string Ended, StatusKind Kind) : IArchivedRowViewModel
{
    public DesignArchivedRowViewModel()
        : this(SampleJobs.LodashUpdate, "Update lodash to 4.17.21", "Approved", "2 d ago", StatusKind.Done)
    {
    }

    public IStatusDotViewModel Dot { get; } = new StatusDotViewModel(Kind);

    public IRelayCommand OpenCommand { get; init; } = new RelayCommand(() => { });

    public IAsyncRelayCommand RestoreCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

internal sealed class DesignArchivedViewModel : IArchivedViewModel
{
    public IReadProblemViewModel ReadProblem { get; init; } = new DesignReadProblemViewModel();

    public string Title => "Archived";

    public string Summary => ArchivePhrases.Summary(4);

    public bool HasArchived { get; init; } = true;

    public string Empty => ArchivePhrases.Empty;

    public IReadOnlyList<ArchivedGroup> Groups { get; init; } =
    [
        new ArchivedGroup("acme-dashboard", "2",
        [
            new DesignArchivedRowViewModel(SampleJobs.JpyRounding, "Fix JPY rounding in invoice totals", "Approved", "Yesterday", StatusKind.Done),
            new DesignArchivedRowViewModel(SampleJobs.FlakyCheckout, "Try a Redis cache for sessions", "Discarded", "3 d ago", StatusKind.Failed),
        ]),
        new ArchivedGroup("Chats", "2",
        [
            new DesignArchivedRowViewModel(SampleJobs.SyncQueue, "Sketch a CSV parser", "Ready for review", "20 min ago", StatusKind.ReadyForReview),
            new DesignArchivedRowViewModel(SampleJobs.LoginRateLimit, "Compare SQLite WAL modes", "Ready for review", "12 Oct", StatusKind.ReadyForReview),
        ]),
    ];
}

internal sealed class DesignArchivedEntryViewModel : IArchivedEntryViewModel
{
    public bool HasArchived { get; init; } = true;

    public string Count => "14";

    public IRelayCommand OpenCommand { get; init; } = new RelayCommand(() => { });
}

internal sealed class DesignArchivedNoteViewModel : IArchivedNoteViewModel
{
    public bool IsShown { get; init; } = true;

    public IAsyncRelayCommand RestoreCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);
}
