using Avala.Jobs.Contracts;
using Avala.Workbench.Presenting;
using Avala.Workspaces.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Review;

internal sealed class DesignHunkViewModel : IHunkViewModel
{
    public string Header => "@@ -12,6 +12,14 @@ export async function login(request: Request)";

    public IReadOnlyList<string> Lines { get; } =
    [
        "   const { email, password } = await request.json();",
        "+  const attempt = limiter.consume(clientAddress(request));",
        "+  if (!attempt.allowed) {",
        "+    return tooManyRequests(attempt.retryAfter);",
        "+  }",
        "   const user = await users.findByEmail(email);",
    ];
}

internal sealed class DesignChangedFileViewModel(string path, ChangeKind kind, string counts, bool isExpanded) : IChangedFileViewModel
{
    public DesignChangedFileViewModel()
        : this("src/auth/login.ts", ChangeKind.Modified, "+24 -3", true)
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

internal sealed class DesignReviewExceptionViewModel(string title, string detail) : IReviewExceptionViewModel
{
    public DesignReviewExceptionViewModel()
        : this("Attempt 1 failed tests (exit 1)", "FAIL test/auth/login.test.ts\n  ✕ answers 429 after five failed attempts (14 ms)\n    Expected: 429\n    Received: 401")
    {
    }

    public string Title { get; } = title;

    public string Detail { get; } = detail;
}

[INotifyPropertyChanged]
internal sealed partial class DesignReviewViewModel : IReviewViewModel
{
    public JobId Job => SampleJobs.LoginRateLimit;

    public JobStatus Status => JobStatus.AwaitingReview;

    public bool IsLoaded => true;

    public string Verdict => "Verified on attempt 2 of 2";

    public bool HasExceptions => true;

    public IReadOnlyList<IReviewExceptionViewModel> Exceptions { get; } =
    [
        new DesignReviewExceptionViewModel(),
        new DesignReviewExceptionViewModel("Assumed 5 attempts per minute for \"How many failed logins before the limit?\"", "The policy took the recommended option."),
    ];

    public string Quiet => "6 other decisions were allowed by rules · USD 0.84 · 61,250 tokens";

    public string Changes => "3 files changed";

    public IReadOnlyList<IChangedFileViewModel> Files { get; } =
    [
        new DesignChangedFileViewModel(),
        new DesignChangedFileViewModel("src/auth/rateLimit.ts", ChangeKind.Added, "+58 -0", false),
        new DesignChangedFileViewModel("test/auth/login.test.ts", ChangeKind.Modified, "+41 -2", false),
    ];

    [ObservableProperty]
    public partial string Feedback { get; set; } = string.Empty;

    public string Outcome => string.Empty;

    public IReadOnlyList<string> Conflicts { get; } = [];

    public bool ConfirmingDiscard => false;

    public IAsyncRelayCommand ApproveCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand SendBackCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask, () => false);

    public IRelayCommand RequestDiscardCommand { get; } = new RelayCommand(() => { });

    public IRelayCommand CancelDiscardCommand { get; } = new RelayCommand(() => { }, () => false);

    public IAsyncRelayCommand ConfirmDiscardCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask, () => false);
}
