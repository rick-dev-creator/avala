using Avala.Jobs.Contracts;
using Avala.Workbench.Presenting;
using Avala.Workspaces.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Review;

internal sealed class DesignHunkViewModel : IHunkViewModel
{
    public string Header => "@@ -18,7 +18,10 @@ func Routes(r chi.Router, h *Handlers)";

    public IReadOnlyList<HunkLine> Lines { get; } =
    [
        new("  r.Get(\"/health\", h.Health)", DiffLineKind.Context),
        new("- r.Post(\"/login\", h.Login)", DiffLineKind.Removed),
        new("+ r.With(ratelimit.PerIPAndAccount(5, time.Minute)).", DiffLineKind.Added),
        new("+   Post(\"/login\", h.Login)", DiffLineKind.Added),
        new("  r.Post(\"/logout\", h.Logout)", DiffLineKind.Context),
    ];
}

internal sealed class DesignChangedFileViewModel(string path, ChangeKind kind, string counts, bool isExpanded) : IChangedFileViewModel
{
    public DesignChangedFileViewModel()
        : this("internal/http/routes.go", ChangeKind.Modified, "+4 −1", true)
    {
    }

    public string Path { get; } = path;

    public ChangeKind Kind { get; } = kind;

    public string Counts { get; } = counts;

    public IReadOnlyList<IHunkViewModel> Hunks { get; } = isExpanded ? [new DesignHunkViewModel()] : [];

    public bool IsExpanded { get; } = isExpanded;

    public string Error => string.Empty;

    public IAsyncRelayCommand ShowHunksCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

[INotifyPropertyChanged]
internal sealed partial class DesignReviewExceptionViewModel : IReviewExceptionViewModel
{
    public string Title { get; init; } = "Attempt 1 failed";

    public string Fact { get; init; } = "test · exit 1";

    public string Detail { get; init; } = string.Empty;

    public string Output { get; init; } = "--- FAIL: TestLogin_RateLimit (0.04s)\n    ratelimit_test.go:52: attempt 6: want 429, got 200";

    public ExceptionTone Tone { get; init; } = ExceptionTone.Failure;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;
}

[INotifyPropertyChanged]
internal sealed partial class DesignReviewViewModel : IReviewViewModel
{
    public JobId Job => SampleJobs.LoginRateLimit;

    public JobStatus Status { get; init; } = JobStatus.AwaitingReview;

    public string Heading { get; init; } = "Ready for review";

    public string Title => "Rate-limit POST /login";

    public string Facts => "ledger-api · claude-work · Autonomous · 2 sessions";

    public bool IsLoaded => true;

    public string Verdict => "Verified on attempt 2 of 2";

    public string Proof => "lint, vet, test and build all passed in the worktree after the last turn.";

    public bool IsVerified => true;

    public bool HasExceptions => true;

    public IReadOnlyList<IReviewExceptionViewModel> Exceptions { get; } =
    [
        new DesignReviewExceptionViewModel(),
        new DesignReviewExceptionViewModel
        {
            Title = "Denied: run curl https://ipinfo.io/json",
            Fact = "default policy",
            Detail = "Denied by the default policy.",
            Output = "curl https://ipinfo.io/json",
            Tone = ExceptionTone.Neutral,
            IsExpanded = false,
        },
        new DesignReviewExceptionViewModel
        {
            Title = "Assumed Both for \"Limit by IP, by account, or both?\"",
            Fact = "recommended option",
            Detail = "The policy took the recommended option.",
            Output = string.Empty,
            Tone = ExceptionTone.Neutral,
            IsExpanded = false,
        },
        new DesignReviewExceptionViewModel
        {
            Title = "The agent edited a rule file",
            Fact = ".avala/checks.json",
            Detail = "Its rules apply from the base commit, not from this edit.",
            Output = string.Empty,
            Tone = ExceptionTone.Attention,
            IsExpanded = false,
        },
    ];

    public string Quiet => "11 other decisions were allowed by rules · USD 1.12 · 186,240 tokens";

    public string Changes => "5 files changed";

    public string Totals => "+130 −2";

    public IReadOnlyList<IChangedFileViewModel> Files { get; } =
    [
        new DesignChangedFileViewModel("internal/http/ratelimit.go", ChangeKind.Added, "+71", false),
        new DesignChangedFileViewModel("internal/http/ratelimit_test.go", ChangeKind.Added, "+48", false),
        new DesignChangedFileViewModel(),
        new DesignChangedFileViewModel("config/defaults.yaml", ChangeKind.Modified, "+6", false),
        new DesignChangedFileViewModel(".avala/checks.json", ChangeKind.Modified, "+1 −1", false),
    ];

    [ObservableProperty]
    public partial string Feedback { get; set; } = string.Empty;

    public string Queued { get; init; } = string.Empty;

    public IAsyncRelayCommand SendBackQueuedCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public string Outcome { get; init; } = string.Empty;

    public bool IsClosed => Outcome.Length > 0;

    public string Refusal { get; init; } = string.Empty;

    public IReadOnlyList<string> Conflicts { get; init; } = [];

    public bool ConfirmingDiscard { get; init; }

    public IAsyncRelayCommand ApproveCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand SendBackCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask, () => false);

    public IRelayCommand RequestDiscardCommand { get; } = new RelayCommand(() => { });

    public IRelayCommand CancelDiscardCommand { get; } = new RelayCommand(() => { });

    public IAsyncRelayCommand ConfirmDiscardCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IRelayCommand CloseCommand { get; } = new RelayCommand(() => { });
}
