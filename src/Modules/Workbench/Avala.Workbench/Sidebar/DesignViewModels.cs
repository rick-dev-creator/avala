using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Workbench.Board;
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

    public bool IsSelected { get; init; } = Job == SampleJobs.JpyRounding;

    public bool IsChild { get; init; }

    public JobGroup Group { get; init; } = JobGroup.Running;

    public bool CanMove => !IsChild;

    public IReadOnlyList<MoveTarget> MoveTargets { get; init; } =
    [
        MoveTarget.To("payments-api", _ => Task.CompletedTask),
        MoveTarget.To("Side projects", _ => Task.CompletedTask),
        MoveTarget.ToNew(_ => Task.CompletedTask),
    ];

    public bool ShowsElapsed { get; init; }

    public DateTimeOffset Since { get; init; } = DateTimeOffset.UtcNow.AddSeconds(-83);

    public bool IsOnPhone { get; init; }

    public bool HasDraft { get; init; }

    public IStatusDotViewModel Dot { get; } = new StatusDotViewModel(Kind);

    public DateTimeOffset Submitted => DateTimeOffset.UnixEpoch;

    public IReadOnlyList<ISidebarRow> Children { get; init; } = [];

    public IReadOnlyList<JobId> Shelved => [Job];

    public bool CanArchive { get; init; }

    public IAsyncRelayCommand ArchiveCommand { get; init; } = Shelving.Idle;

    public string Refusal { get; init; } = string.Empty;

    public bool HasRefusal => Refusal.Length > 0;
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

    public bool CanArchive => false;

    public IAsyncRelayCommand ArchiveCommand => Shelving.Idle;

    public string Refusal => string.Empty;

    public bool HasRefusal => false;
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

    public JobGroup Group => JobGroup.ReadyForReview;

    public IReadOnlyList<ISidebarRow> Children { get; init; } = [];

    public IRelayCommand ToggleCommand { get; init; } = new RelayCommand(() => { });

    public IReadOnlyList<JobId> Shelved => [Job];

    public bool CanArchive => true;

    public IAsyncRelayCommand ArchiveCommand => Shelving.Idle;

    public string Refusal => string.Empty;

    public bool HasRefusal => false;
}

internal sealed class DesignProjectGroupViewModel : IProjectGroupViewModel
{
    public string Key { get; init; } = "design";

    public IProjectDocsViewModel Docs { get; init; } = new DesignProjectDocsViewModel();

    public string Name { get; init; } = "acme-dashboard";

    public bool IsChats { get; init; }

    public bool IsProject { get; init; } = true;

    public int OpenCount => Open.Count;

    public bool HasRows => Open.Count + Done.Count > 0;

    public bool NeedsYou { get; init; }

    public bool IsExpanded { get; init; } = true;

    public IReadOnlyList<ISidebarEntry> Open { get; init; } = [];

    public IReadOnlyList<ISidebarEntry> Done { get; init; } = [];

    public bool HasDone => Done.Count > 0;

    public string DoneLabel => ProjectPhrases.Done(Done.Count);

    public bool IsDoneShown { get; init; }

    public bool IsRenaming { get; init; }

    public int Renames => 0;

    public string Draft { get; set; } = "acme-dashboard";

    public string Problem { get; init; } = string.Empty;

    public bool HasProblem => Problem.Length > 0;

    public bool CanDelete => IsProject && !HasRows;

    public bool HasRepository => IsProject;

    public IRelayCommand<object> SelectCommand { get; init; } = new RelayCommand<object>(_ => { });

    public IAsyncRelayCommand ToggleCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IRelayCommand ToggleDoneCommand { get; init; } = new RelayCommand(() => { });

    public IRelayCommand StartRenameCommand { get; init; } = new RelayCommand(() => { });

    public IAsyncRelayCommand CommitRenameCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IRelayCommand CancelRenameCommand { get; init; } = new RelayCommand(() => { });

    public IAsyncRelayCommand DeleteCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand ArchiveDoneCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IRelayCommand OpenNotesCommand { get; init; } = new RelayCommand(() => { });
}

internal sealed class DesignSidebarViewModel : ISidebarViewModel
{
    public IReadOnlyList<ISidebarEntry> NeedsYou { get; } =
    [
        new DesignJobRowViewModel(SampleJobs.FlakyCheckout, "Fix flaky CheckoutForm test", JobStatus.Running, "wants to run a command", StatusKind.NeedsYou, 1) { Group = JobGroup.NeedsYou },
        new DesignJobRowViewModel(SampleJobs.SyncQueue, "Extract sync queue into a module", JobStatus.NeedsHelp, "held: stalled", StatusKind.Held, 0) { Group = JobGroup.NeedsYou },
    ];

    public IReadOnlyList<IProjectGroupViewModel> Projects { get; } =
    [
        new DesignProjectGroupViewModel
        {
            Open =
            [
                new DesignJobRowViewModel(SampleJobs.JpyRounding, "Fix JPY rounding in invoice totals", JobStatus.Running, "2 of 4", StatusKind.Working, 0) { ShowsElapsed = true },
                new DesignJobRowViewModel(SampleJobs.InvoicePdf, "Migrate payments to stripe-go v79", JobStatus.Running, "3 sub-agents", StatusKind.Working, 0)
                {
                    Children =
                    [
                        new DesignJobRowViewModel(SampleJobs.FlakyCheckout, "Update call sites in internal/payments", JobStatus.Running, "9 of 14 files", StatusKind.Working, 0) { IsChild = true },
                        new DesignJobRowViewModel(SampleJobs.LoginRateLimit, "Regenerate the API reference", JobStatus.AwaitingReview, "verified", StatusKind.ReadyForReview, 0) { IsChild = true },
                    ],
                },
                new DesignContestRowViewModel(),
                new DesignJobRowViewModel(SampleJobs.LoginRateLimit, "Rate-limit POST /login", JobStatus.AwaitingReview, "verified on attempt 2", StatusKind.ReadyForReview, 0) { Group = JobGroup.ReadyForReview },
            ],
            Done =
            [
                new DesignJobRowViewModel(SampleJobs.CheckoutSplit, "Paginate audit log export", JobStatus.Approved, "merged", StatusKind.Done, 0) { Group = JobGroup.Done },
            ],
        },
        new DesignProjectGroupViewModel { Key = "payments", Name = "payments-api", IsExpanded = false, NeedsYou = true, Open = [new DesignJobRowViewModel(SampleJobs.CheckoutSplit, "Fix the calculator", JobStatus.NeedsHelp, "resumes at 15:51", StatusKind.Held, 0) { Group = JobGroup.Waiting }] },
        new DesignProjectGroupViewModel { Key = "side", Name = "Side projects", IsRenaming = true, Draft = "Side projects" },
    ];

    public IProjectGroupViewModel Chats { get; } = new DesignProjectGroupViewModel
    {
        Key = "chats",
        Name = "Chats",
        IsChats = true,
        IsProject = false,
        Open = [new DesignJobRowViewModel(SampleJobs.SyncQueue, "Sketch a CSV parser for bank exports", JobStatus.AwaitingReview, "no checks", StatusKind.ReadyForReview, 0) { Group = JobGroup.ReadyForReview }],
    };

    public ISidebarRow? Selected => Projects[0].Open[0];

    public bool IsEmpty => false;

    public IRelayCommand<object> SelectCommand { get; } = new RelayCommand<object>(_ => { });

    public IAsyncRelayCommand CreateProjectCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
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

internal sealed class DesignProjectDocsViewModel : IProjectDocsViewModel
{
    public bool IsShown { get; init; } = true;

    public int Count => Features.Count;

    public bool IsOpen { get; init; } = true;

    public IReadOnlyList<IFeatureRowViewModel> Features { get; init; } =
    [
        new DesignFeatureRowViewModel("Payments on stripe-go v79", "3 versions"),
        new DesignFeatureRowViewModel("Refund webhooks", "2 versions · not merged"),
    ];

    public bool IsOn { get; init; } = true;

    public bool IsUncommitted { get; init; }

    public string Note { get; init; } = string.Empty;

    public string Error { get; init; } = string.Empty;

    public IRelayCommand ToggleCommand { get; } = new RelayCommand(() => { });

    public IAsyncRelayCommand SwitchCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand CommitCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

internal sealed record DesignFeatureRowViewModel(string Title, string Fact) : IFeatureRowViewModel
{
    public DesignFeatureRowViewModel()
        : this("Payments on stripe-go v79", "3 versions")
    {
    }

    public string Tip { get; init; } = string.Empty;

    public IRelayCommand OpenCommand { get; } = new RelayCommand(() => { });
}
