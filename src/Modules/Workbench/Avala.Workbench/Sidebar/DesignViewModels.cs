using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Workbench.Decisions;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Sidebar;

internal sealed record DesignJobRowViewModel(JobId Job, string Title, JobStatus Status, string Fact, StatusKind Kind, int PendingDecisions) : IJobRowViewModel
{
    public DesignJobRowViewModel()
        : this(SampleJobs.InvoicePdf, "Add invoice PDF endpoint", JobStatus.Running, "asks a question", StatusKind.NeedsYou, 1)
    {
    }

    public bool HasPendingDecisions => PendingDecisions > 0;

    public bool IsSelected => Job == SampleJobs.JpyRounding;

    public IStatusDotViewModel Dot { get; } = new StatusDotViewModel(Kind);
}

internal sealed class DesignSidebarViewModel : ISidebarViewModel
{
    public IDecisionsViewModel Decisions { get; } = new DesignDecisionsViewModel();

    public bool IsDecisionsOpen => false;

    public IReadOnlyList<IJobRowViewModel> NeedsYou { get; } =
    [
        new DesignJobRowViewModel(),
        new DesignJobRowViewModel(SampleJobs.FlakyCheckout, "Fix flaky CheckoutForm test", JobStatus.Running, "wants to run a command", StatusKind.NeedsYou, 1),
        new DesignJobRowViewModel(SampleJobs.SyncQueue, "Extract sync queue into a module", JobStatus.NeedsHelp, "held: stalled", StatusKind.Held, 0),
    ];

    public IReadOnlyList<IJobRowViewModel> Running { get; } =
    [
        new DesignJobRowViewModel(SampleJobs.JpyRounding, "Fix JPY rounding in invoice totals", JobStatus.Running, "2 of 4", StatusKind.Working, 0),
        new DesignJobRowViewModel(SampleJobs.LodashUpdate, "Update lodash to 4.17.21", JobStatus.Checking, "verifying", StatusKind.Checking, 0),
    ];

    public IReadOnlyList<IJobRowViewModel> ReadyForReview { get; } =
    [
        new DesignJobRowViewModel(SampleJobs.LoginRateLimit, "Rate-limit POST /login", JobStatus.AwaitingReview, "verified on attempt 2", StatusKind.ReadyForReview, 0),
    ];

    public IReadOnlyList<IJobRowViewModel> Done { get; } =
    [
        new DesignJobRowViewModel(SampleJobs.CheckoutSplit, "Split CheckoutPage into steps", JobStatus.Approved, "merged", StatusKind.Done, 0),
    ];

    public IJobRowViewModel? Selected => Running[0];

    public int PendingDecisions => 2;

    public bool HasPendingDecisions => true;

    public bool IsEmpty => false;

    public IRelayCommand<IJobRowViewModel> SelectCommand { get; } = new RelayCommand<IJobRowViewModel>(_ => { });

    public IRelayCommand ToggleDecisionsCommand { get; } = new RelayCommand(() => { });
}
