using Avala.Jobs.Contracts;
using Avala.Workbench.Presenting;

namespace Avala.Workbench.Usage;

internal sealed class DesignLimitViewModel(string window, double used, string usedText, string resets, string holdAt, bool reachesHold) : ILimitViewModel
{
    public DesignLimitViewModel()
        : this("5h", 0.88, "88% used", "resets 2026-10-09 16:20", "jobs are held at 90%", false)
    {
    }

    public string Window { get; } = window;

    public double Used { get; } = used;

    public string UsedText { get; } = usedText;

    public string Resets { get; } = resets;

    public string HoldAt { get; } = holdAt;

    public bool ReachesHold { get; } = reachesHold;
}

internal sealed class DesignConnectionMeterViewModel(string name, string cost, string tokens, ILimitViewModel limit) : IConnectionMeterViewModel
{
    public DesignConnectionMeterViewModel()
        : this("claude-work", "3.2140 USD", "412,880 tokens", new DesignLimitViewModel())
    {
    }

    public string Name { get; } = name;

    public string Provider => "Claude Code";

    public string Cost { get; } = cost;

    public string Tokens { get; } = tokens;

    public string Unpriced => string.Empty;

    public string Caps => "5 USD per job, holds at 90% of a limit";

    public IReadOnlyList<ILimitViewModel> Limits { get; } = [limit];
}

internal sealed class DesignUsageWindowViewModel : IUsageWindowViewModel
{
    public string Label => "Today";

    public string Cost => "4.0540 USD";

    public long Input => 310_400;

    public long Output => 58_120;

    public long CacheRead => 1_204_300;

    public long CacheWrite => 92_000;

    public long Reasoning => 21_700;

    public string Tokens => "1,686,520 tokens";

    public string Unpriced => string.Empty;

    public string Turns => "37 turns finished, 2 interrupted, 0 failed";
}

internal sealed class DesignJobMeterViewModel(JobId job, string title, JobStatus status, string cost, string tokens, int interventions) : IJobMeterViewModel
{
    public DesignJobMeterViewModel()
        : this(SampleJobs.LoginRateLimit, "Rate-limit POST /login", JobStatus.AwaitingReview, "0.84 USD", "61,250 tokens", 0)
    {
    }

    public JobId Job { get; } = job;

    public string Title { get; } = title;

    public JobStatus Status { get; } = status;

    public string Cost { get; } = cost;

    public string Tokens { get; } = tokens;

    public string Unpriced => string.Empty;

    public string Caps => "5 USD per job";

    public string Carve => string.Empty;

    public int Interventions { get; } = interventions;
}

internal sealed class DesignInterventionViewModel : IInterventionViewModel
{
    public JobId Job => SampleJobs.SyncQueue;

    public string Title => "Extract sync queue into a module";

    public string At => "2026-10-09 14:52";

    public HoldReason Reason => HoldReason.Stalled;

    public string Detail => "silent for 600s, window 600s";
}

internal sealed class DesignUsageViewModel : IUsageViewModel
{
    public string Title => "Usage";

    public IReadOnlyList<IConnectionMeterViewModel> Connections { get; } =
    [
        new DesignConnectionMeterViewModel(),
        new DesignConnectionMeterViewModel("claude-personal", "0.8400 USD", "61,250 tokens", new DesignLimitViewModel("5h", 0.31, "31% used", "resets 2026-10-09 17:05", "jobs are held at 90%", false)),
    ];

    public IReadOnlyList<IUsageWindowViewModel> Windows { get; } = [new DesignUsageWindowViewModel()];

    public IReadOnlyList<IJobMeterViewModel> Jobs { get; } =
    [
        new DesignJobMeterViewModel(),
        new DesignJobMeterViewModel(SampleJobs.SyncQueue, "Extract sync queue into a module", JobStatus.NeedsHelp, "1.9200 USD", "240,410 tokens", 1),
    ];

    public IReadOnlyList<IInterventionViewModel> Interventions { get; } = [new DesignInterventionViewModel()];
}
