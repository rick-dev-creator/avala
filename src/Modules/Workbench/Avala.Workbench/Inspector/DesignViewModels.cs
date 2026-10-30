using Avala.Components.Privacy;
using Avala.Components.Meters;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Inspector;

internal sealed class DesignEvidenceSectionViewModel : IEvidenceSectionViewModel
{
    public bool IsLoaded => true;

    public string Fact => "4 of 4 checks passed";

    public string Summary => "Verified on attempt 2 of 2";

    public IReadOnlyList<string> Attempts { get; } =
    [
        "Attempt 1: failed · tests failed (exit 1, 41.2 s), lint passed (exit 0, 3.1 s)",
        "Attempt 2: passed · tests passed (exit 0, 39.8 s), lint passed (exit 0, 2.9 s)",
    ];

    public string Lacks => string.Empty;

    public IAsyncRelayCommand DeclareChecksCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask, () => false);

    public string ReportedTitle => string.Empty;

    public IReadOnlyList<string> Reported { get; } = [];
}

internal sealed class DesignUndeclaredEvidenceSectionViewModel : IEvidenceSectionViewModel
{
    public bool IsLoaded => true;

    public string Fact => "no checks declared";

    public string Summary => "No checks declared: nothing proves the work";

    public IReadOnlyList<string> Attempts { get; } = ["Attempt 1: no checks declared"];

    public string Lacks => Review.ReviewPhrases.MissingChecks;

    public IAsyncRelayCommand DeclareChecksCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public string ReportedTitle => Review.ReviewPhrases.ReportedTitle(afterEdit: true);

    public IReadOnlyList<string> Reported { get; } = ["dotnet build Avala.slnx · passed", "dotnet test --solution Avala.slnx · passed"];
}

internal sealed class DesignAuditSectionViewModel : IAuditSectionViewModel
{
    public bool IsLoaded => true;

    public string Fact => "1 assumption";

    public bool IsAttention => false;

    public string Summary => "6 allowed by rules · 1 answered by you · 1 assumption";

    public IReadOnlyList<DecisionRow> Decisions { get; } =
    [
        new(DecisionVerdict.Allowed, "Run", "npm install express-rate-limit", "allowed by you"),
        new(DecisionVerdict.Allowed, "Run", "npm test -- test/auth", "allowed by rule tests") { Times = 4 },
        new(DecisionVerdict.Allowed, "Edit", "src/auth/rateLimit.ts", "allowed by rule source"),
    ];

    public string More => string.Empty;

    public IRelayCommand ShowAllCommand { get; } = new RelayCommand(() => { });

    public IReadOnlyList<AssumptionRow> Assumptions { get; } = [new("How many failed logins before the limit?", "5 attempts per minute")];
}

internal sealed class DesignUsageSectionViewModel : IUsageSectionViewModel
{
    public DesignUsageSectionViewModel()
    {
        var meter = new MeterViewModel("Cost cap", Sdk.Option<double>.None);
        meter.Show(0.168);
        Meter = meter;
    }

    public bool IsLoaded => true;

    public string Fact => "0.84 USD of 5 USD";

    public bool IsAttention => false;

    public string Spent => "0.84 USD · 61,250 tokens";

    public IReadOnlyList<string> Caps { get; } = ["Cost cap 5 USD", "Holds at 90% of a limit"];

    public IReadOnlyList<string> Interventions { get; } = [];

    public string Carve => string.Empty;

    public IReadOnlyList<string> Raises { get; } = [];

    public bool CanRaise => false;

    public string Notice => string.Empty;

    public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand RaiseCommand { get; } = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => Task.CompletedTask);

    public IMeterViewModel? Meter { get; }
}

internal sealed class DesignAutonomySectionViewModel : IAutonomySectionViewModel
{
    public bool IsLoaded => true;

    public string Fact => "Supervised";

    public string Autonomy => "Supervised, as the repository declares";

    public string Connection => "claude-personal";

    public string Model => "claude-opus-5-5 · high effort";

    public string Reason => "Chosen by capacity: claude-personal had the most left";

    public IReadOnlyList<CapacityLine> Compared { get; } =
    [
        new("claude-work", "88% of 5h · holds at 90%", false, false),
        new("claude-personal", "31% of 5h · holds at 90%", true, false),
    ];

    public IReadOnlyList<HandoffLine> Handoffs { get; } =
    [
        new("Handed off from claude-work to claude-personal at 91% of the 5-hour window · 2026-10-10 10:42", "Spent on claude-work: 1.84 USD · 412,880 tokens"),
        new(string.Empty, "Spent on claude-personal: 0.31 USD · 61,200 tokens"),
    ];

    public string Waiting => string.Empty;
}

internal sealed class DesignWorktreeSectionViewModel : IWorktreeSectionViewModel
{
    public bool IsLoaded => true;

    public string Fact => "rate-limit-post-login";

    public string Branch => "avala/rate-limit-post-login";

    public string Base => "main at 4f2c9e1";

    public Personal Path => Personal.Path("/home/dana/.avala/worktrees/shop-api/rate-limit-post-login");

    public string Ports => "Ports 41000–41009";

    public bool IsScratch { get; init; }

    public string Repository { get; init; } = "~/.local/share/avala/scratch/2026-10-12-sketch-a-csv-parser-3f9a1c";

    public string FolderProblem => string.Empty;

    public bool CouldNotStart { get; init; }

    public string StartProblem { get; init; } = "The process cannot access the file '/home/dana/code/shop-api/.avala/jobs.json' because it is being used by another process.";

    public string RetryRefusal { get; init; } = string.Empty;

    public bool IsBranchCopied => false;

    public bool IsPathCopied { get; init; }

    public IAsyncRelayCommand CopyBranchCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand CopyPathCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand OpenFolderCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand RetryCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

internal sealed class DesignContestSectionViewModel : IContestSectionViewModel
{
    public bool IsLoaded => true;

    public bool IsContested => true;

    public string Fact => "candidate 3 of 3 · the winner";

    public string Status => "Decided: the winner waits for your review";

    public IReadOnlyList<string> Ranking { get; } =
    [
        "1. Candidate 3 · 0 exceptions · 1 attempt · 1 line changed · 0.009 USD · ahead of the next on a smaller diff · this job",
        "2. Candidate 2 · 0 exceptions · 1 attempt · 31 lines changed · 0.012 USD · the only verified run",
        "3. Candidate 1 · 0 exceptions · 1 attempt · 1 line changed · 0.015 USD · not verified",
    ];
}

internal sealed class DesignChecksRunningViewModel : IChecksRunningViewModel
{
    public bool IsShown { get; init; } = true;

    public string Text { get; init; } = "Checks running · 2 of 3";

    public IReadOnlyList<string> Lines { get; init; } = ["passed · build (ubuntu-latest)", "passed · build (macos-latest)", "pending · build (windows-latest)"];
}
