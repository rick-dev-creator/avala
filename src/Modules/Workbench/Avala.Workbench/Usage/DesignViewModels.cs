using Avala.Jobs.Contracts;
using Avala.Workbench.Overview;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Usage;

internal sealed record DesignLimitViewModel(string Window, string Label, double Used, string UsedText, string Resets, double Hold, bool ReachesHold) : ILimitViewModel
{
    public DesignLimitViewModel()
        : this("5h", "5-hour window", 0.88, "88%", "resets 2026-10-09 16:20", 0.9, false)
    {
    }

    public string HoldAt => IsNear ? $"Jobs on this connection hold at the {Amounts.Percent(Hold)} threshold." : string.Empty;

    public bool HasHold => Hold > 0;

    public bool IsNear => HasHold && Used >= Hold - 0.1;
}

internal sealed class DesignConnectionMeterViewModel(string name, string cost, string tokens, IReadOnlyList<ILimitViewModel> limits) : IConnectionMeterViewModel
{
    public DesignConnectionMeterViewModel()
        : this("claude-work", "9.86 USD", "1,412,880 tokens", [new DesignLimitViewModel(), new DesignLimitViewModel("7d", "7-day window", 0.43, "43%", "resets 2026-10-13 09:00", 0.9, false)])
    {
    }

    public string Name { get; } = name;

    public string Provider => "Claude Code";

    public string Cost { get; } = cost;

    public string Tokens { get; } = tokens;

    public string Unpriced => string.Empty;

    public string Caps => "3 USD per job, holds at 90% of a limit";

    public IReadOnlyList<ILimitViewModel> Limits { get; } = limits;
}

internal sealed record DesignUsageWindowViewModel(string Label, string Cost, long Input, long Output, long CacheRead, long CacheWrite, long Reasoning, string Unpriced) : IUsageWindowViewModel
{
    public DesignUsageWindowViewModel()
        : this("Today", "4.0540 USD", 310_400, 58_120, 1_204_300, 92_000, 21_700, string.Empty)
    {
    }

    public string Tokens => Amounts.Tokens(Input + Output + CacheRead + CacheWrite + Reasoning);

    public string Turns => "37 turns finished, 2 interrupted, 0 failed";
}

internal sealed record DesignUsageDayViewModel(string Day, string Tokens, string Cost, double Share, bool IsToday) : IUsageDayViewModel
{
    public DesignUsageDayViewModel()
        : this("Sat 10 Oct · today", "620,400 tokens", "4.05 USD", 0.88, true)
    {
    }

    public string Types => "input 310,400 · output 58,120 · cache read 1,204,300 · cache write 92,000 · reasoning 21,700";
}

internal sealed class DesignUsageRangeViewModel : IUsageRangeViewModel
{
    public bool ShowsToday => false;

    public bool ShowsWeek => true;

    public bool ShowsMonth => false;

    public string Caption => "Since Sun 4 Oct, in this computer's time zone · newest first";

    public IUsageWindowViewModel? Total { get; } =
        new DesignUsageWindowViewModel("Last 7 days", "14.07 USD", 588_000, 157_000, 1_020_000, 78_000, 118_000, "2 usage reports had no cost");

    public IReadOnlyList<IUsageDayViewModel> Days { get; } =
    [
        new DesignUsageDayViewModel(),
        new DesignUsageDayViewModel("Fri 9 Oct", "702,300 tokens", "4.92 USD", 1, false),
        new DesignUsageDayViewModel("Thu 8 Oct", "301,140 tokens", "2.11 USD", 0.43, false),
        new DesignUsageDayViewModel("Wed 7 Oct", "0 tokens", "no cost", 0, false),
        new DesignUsageDayViewModel("Tue 6 Oct", "333,100 tokens", "2.96 USD", 0.47, false),
        new DesignUsageDayViewModel("Mon 5 Oct", "0 tokens", "no cost", 0, false),
        new DesignUsageDayViewModel("Sun 4 Oct", "4,060 tokens", "0.03 USD", 0.01, false),
    ];

    public IRelayCommand ShowTodayCommand { get; } = new RelayCommand(() => { });

    public IRelayCommand ShowWeekCommand { get; } = new RelayCommand(() => { });

    public IRelayCommand ShowMonthCommand { get; } = new RelayCommand(() => { });
}

internal sealed record DesignJobMeterViewModel(JobId Job, string Title, JobStatus Status, string Connection, string Cost, double Used, string Cap, int Interventions) : IJobMeterViewModel
{
    public DesignJobMeterViewModel()
        : this(SampleJobs.LoginRateLimit, "Rate-limit POST /login", JobStatus.AwaitingReview, "claude-work", "1.12 USD", 0.37, "3 USD", 0)
    {
    }

    public string Tokens => "61,250 tokens";

    public string Unpriced => string.Empty;

    public string Caps => $"{Cap} per job";

    public string Carve => string.Empty;

    public bool HasCap => Cap.Length > 0;

    public bool IsNearCap => HasCap && Used >= 0.9;
}

internal sealed record DesignInterventionViewModel(JobId Job, string Title, string At, HoldReason Reason, string Detail) : IInterventionViewModel
{
    public DesignInterventionViewModel()
        : this(SampleJobs.SyncQueue, "Extract sync queue into a module", "2026-10-09 14:52", HoldReason.Stalled, "silent for 600s, window 600s")
    {
    }
}

internal sealed class DesignUsageViewModel : IUsageViewModel
{
    public string Title => "Usage";

    public string Scope => Scopes.Usage;

    public IReadOnlyList<IConnectionMeterViewModel> Connections { get; } =
    [
        new DesignConnectionMeterViewModel(),
        new DesignConnectionMeterViewModel(
            "claude-personal",
            "4.21 USD",
            "548,120 tokens",
            [
                new DesignLimitViewModel("5h", "5-hour window", 0.31, "31%", "resets 2026-10-09 17:05", 0.9, false),
                new DesignLimitViewModel("7d", "7-day window", 0.22, "22%", "resets 2026-10-15 21:00", 0.9, false),
            ]),
    ];

    public IUsageRangeViewModel Range { get; } = new DesignUsageRangeViewModel();

    public IReadOnlyList<IJobMeterViewModel> Jobs { get; } =
    [
        new DesignJobMeterViewModel(),
        new DesignJobMeterViewModel(SampleAgents.ZodUpdate, "Update zod to 3.23", JobStatus.Checking, "claude-work", "0.71 USD", 0.24, "3 USD", 0),
        new DesignJobMeterViewModel(SampleJobs.SyncQueue, "Extract sync queue into a module", JobStatus.NeedsHelp, "claude-personal", "0.66 USD", 0.33, "2 USD", 1),
        new DesignJobMeterViewModel(SampleJobs.JpyRounding, "Fix JPY rounding in invoice totals", JobStatus.Running, "claude-work", "0.41 USD", 0.14, "3 USD", 0),
        new DesignJobMeterViewModel(SampleJobs.FlakyCheckout, "Fix flaky CheckoutForm test", JobStatus.NeedsHelp, "claude-personal", "0.29 USD", 0.1, "3 USD", 0),
        new DesignJobMeterViewModel(SampleJobs.InvoicePdf, "Add invoice PDF endpoint", JobStatus.NeedsHelp, "claude-work", "0.18 USD", 0.06, "3 USD", 0),
    ];

    public IReadOnlyList<IInterventionViewModel> Interventions { get; } =
    [
        new DesignInterventionViewModel(),
        new DesignInterventionViewModel(SampleJobs.LoginRateLimit, "Paginate audit log export", "2026-10-08 18:12", HoldReason.BudgetExceeded, "cost job: 3.04 against 3"),
    ];
}
