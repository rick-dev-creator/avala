using Avala.Autopilot.Contracts;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Triggers;

internal sealed record DesignLoopItemViewModel(string Repository, string Status, bool NeedsAttention, string Summary, IReadOnlyList<string> Iterations)
    : ILoopItemViewModel
{
    public DesignLoopItemViewModel()
        : this(
            "~/code/ledger-api",
            "Running · 3 of 20 iterations done",
            false,
            "2 approved on their own · 1 waiting for you · spent 0.42 USD",
            [
                "#3 · docs-sweep · waiting for review: verification not passed",
                "#2 · update-deps · approved on its own",
                "#1 · fix-flaky-test · approved on its own",
            ])
    {
    }

    public LoopId Id { get; } = new(Guid.Parse("01a12000-0000-7000-8000-000000000001"));

    public bool CanPause => Status.StartsWith("Running", StringComparison.Ordinal) || Status.StartsWith("Waiting", StringComparison.Ordinal);

    public bool CanResume => Status.StartsWith("Paused", StringComparison.Ordinal);

    public bool CanStop => !Status.StartsWith("Ended", StringComparison.Ordinal);
}

internal sealed class DesignLoopsViewModel : ILoopsViewModel
{
    public IReadOnlyList<ILoopItemViewModel> Loops { get; init; } =
    [
        new DesignLoopItemViewModel(),
        new DesignLoopItemViewModel(
            "~/code/web-console",
            "Paused until Mon 12 Oct 07:00 · usage window 5h",
            false,
            "1 approved on their own · 0 waiting for you · spent 0.18 USD",
            ["#1 · triage-issue-42 · approved on its own"]),
        new DesignLoopItemViewModel(
            "~/code/billing",
            "Ended · breaker tripped: the same failure, 2 of 2",
            true,
            "0 approved on their own · 2 waiting for you · spent 0.31 USD",
            ["#2 · retry-payments · needs help", "#1 · retry-payments-docs · needs help"]),
    ];

    public string Error => string.Empty;

    public IAsyncRelayCommand<ILoopItemViewModel> PauseCommand { get; } = new AsyncRelayCommand<ILoopItemViewModel>(_ => Task.CompletedTask);

    public IAsyncRelayCommand<ILoopItemViewModel> ResumeCommand { get; } = new AsyncRelayCommand<ILoopItemViewModel>(_ => Task.CompletedTask);

    public IAsyncRelayCommand<ILoopItemViewModel> StopCommand { get; } = new AsyncRelayCommand<ILoopItemViewModel>(_ => Task.CompletedTask);

    public void Show(IReadOnlyList<LoopDigest> loops, TimeZoneInfo zone)
    {
    }
}
