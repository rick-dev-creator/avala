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

    public bool IsChild { get; init; }

    public bool IsScratch { get; init; }

    public bool ShowsElapsed { get; init; }

    public DateTimeOffset Since { get; init; } = DateTimeOffset.UtcNow.AddSeconds(-83);

    public IStatusDotViewModel Dot { get; } = new StatusDotViewModel(Kind);

    public DateTimeOffset Submitted => DateTimeOffset.UnixEpoch;

    public IReadOnlyList<ISidebarRow> Children { get; init; } = [];
}

internal sealed record DesignCandidateRowViewModel(JobId Job, int Ordinal, string Fact, StatusKind Kind) : ICandidateRowViewModel
{
    public DesignCandidateRowViewModel()
        : this(SampleJobs.LodashUpdate, 2, "verified", StatusKind.ReadyForReview)
    {
    }

    public string Title => $"Candidate {Ordinal}";

    public int PendingDecisions => 0;

    public bool HasPendingDecisions => false;

    public bool IsSelected { get; init; }

    public bool IsChild => true;

    public DateTimeOffset Submitted => DateTimeOffset.UnixEpoch;

    public IStatusDotViewModel Dot { get; } = new StatusDotViewModel(Kind);

    public IReadOnlyList<ISidebarRow> Children => [];
}

internal sealed record DesignContestRowViewModel(JobId Job, string Title, string Fact, StatusKind Kind) : IContestRowViewModel
{
    public DesignContestRowViewModel()
        : this(SampleJobs.JpyRounding, "Fix JPY rounding in invoice totals", "3 candidates · 2 passed", StatusKind.ReadyForReview)
    {
    }

    public int PendingDecisions => 0;

    public bool HasPendingDecisions => false;

    public bool IsSelected => false;

    public bool IsChild => false;

    public DateTimeOffset Submitted => DateTimeOffset.UnixEpoch;

    public IStatusDotViewModel Dot { get; } = new StatusDotViewModel(Kind);

    public bool IsExpanded { get; init; }

    public IReadOnlyList<ISidebarRow> Children { get; init; } = [];

    public IRelayCommand ToggleCommand { get; init; } = new RelayCommand(() => { });
}

internal sealed class DesignSidebarViewModel : ISidebarViewModel
{
    public IReadOnlyList<ISidebarEntry> NeedsYou { get; } =
    [
        new DesignJobRowViewModel(),
        new DesignJobRowViewModel(SampleJobs.FlakyCheckout, "Fix flaky CheckoutForm test", JobStatus.Running, "wants to run a command", StatusKind.NeedsYou, 1),
        new DesignJobRowViewModel(SampleJobs.SyncQueue, "Extract sync queue into a module", JobStatus.NeedsHelp, "held: stalled", StatusKind.Held, 0),
    ];

    public IReadOnlyList<ISidebarEntry> Running { get; } =
    [
        new DesignJobRowViewModel(SampleJobs.JpyRounding, "Fix JPY rounding in invoice totals", JobStatus.Running, "2 of 4", StatusKind.Working, 0) { ShowsElapsed = true },
        new DesignJobRowViewModel(SampleJobs.InvoicePdf, "Migrate payments to stripe-go v79", JobStatus.Running, "3 sub-agents", StatusKind.Working, 0)
        {
            Children =
            [
                new DesignJobRowViewModel(SampleJobs.FlakyCheckout, "Update call sites in internal/payments", JobStatus.Running, "9 of 14 files", StatusKind.Working, 0) { IsChild = true },
                new DesignJobRowViewModel(SampleJobs.SyncQueue, "Rewrite webhook signature tests", JobStatus.Running, "waiting for its parent", StatusKind.Held, 0) { IsChild = true },
                new DesignJobRowViewModel(SampleJobs.LoginRateLimit, "Regenerate the API reference", JobStatus.AwaitingReview, "verified", StatusKind.ReadyForReview, 0) { IsChild = true },
            ],
        },
        new DesignJobRowViewModel(SampleJobs.LodashUpdate, "Update zod to 3.23", JobStatus.Checking, "verifying", StatusKind.Checking, 0),
    ];

    public IReadOnlyList<ISidebarEntry> Waiting { get; } =
    [
        new DesignJobRowViewModel(SampleJobs.CheckoutSplit, "Fix the calculator on the first account", JobStatus.NeedsHelp, "resumes at 15:51", StatusKind.Held, 0),
    ];

    public IReadOnlyList<ISidebarEntry> ReadyForReview { get; } =
    [
        new DesignContestRowViewModel()
        {
            IsExpanded = true,
            Children =
            [
                new DesignCandidateRowViewModel(SampleJobs.InvoicePdf, 1, "needs help: retries ran out", StatusKind.NeedsYou),
                new DesignCandidateRowViewModel(SampleJobs.LodashUpdate, 2, "verified", StatusKind.ReadyForReview),
                new DesignCandidateRowViewModel(SampleJobs.SyncQueue, 3, "verified", StatusKind.ReadyForReview),
            ],
        },
        new DesignJobRowViewModel(SampleJobs.LoginRateLimit, "Rate-limit POST /login", JobStatus.AwaitingReview, "verified on attempt 2", StatusKind.ReadyForReview, 0),
        new DesignJobRowViewModel(SampleJobs.SyncQueue, "Sketch a CSV parser for bank exports", JobStatus.AwaitingReview, "no checks", StatusKind.ReadyForReview, 0) { IsScratch = true },
    ];

    public IReadOnlyList<ISidebarEntry> Done { get; } =
    [
        new DesignJobRowViewModel(SampleJobs.CheckoutSplit, "Paginate audit log export", JobStatus.Approved, "merged", StatusKind.Done, 0),
    ];

    public ISidebarRow? Selected => Running[0];

    public bool IsEmpty => false;

    public IRelayCommand<object> SelectCommand { get; } = new RelayCommand<object>(_ => { });
}

internal sealed class DesignToolbarViewModel : IToolbarViewModel
{
    public IDecisionsViewModel Decisions { get; } = new DesignDecisionsViewModel();

    public bool IsDecisionsOpen => false;

    public int PendingDecisions => 3;

    public bool HasPendingDecisions => true;

    public IRelayCommand ToggleDecisionsCommand { get; } = new RelayCommand(() => { });

    public IRelayCommand CloseDecisionsCommand { get; } = new RelayCommand(() => { });

    public IRelayCommand NewJobCommand { get; } = new RelayCommand(() => { });
}
