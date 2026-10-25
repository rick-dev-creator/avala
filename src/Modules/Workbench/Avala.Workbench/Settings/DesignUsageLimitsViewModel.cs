using Avala.Handoffs.Contracts;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Settings;

internal sealed class DesignUsageLimitsViewModel : IUsageLimitsViewModel
{
    public IReadOnlyList<string> Actions => LimitPhrases.Actions;

    public string Action { get; set; } = LimitPhrases.ActionName(OnLimit.HandOffAnyHarness);

    public bool HandsOff { get; init; } = true;

    public double Threshold { get; set; } = 90;

    public double HardCap { get; set; } = 97;

    public string ThresholdLabel => LimitPhrases.Percent(Threshold);

    public string HardCapLabel => LimitPhrases.Percent(HardCap);

    public IReadOnlyList<string> Strategies => LimitPhrases.Strategies;

    public string Strategy { get; set; } = LimitPhrases.StrategyName(HandoffStrategy.MostHeadroom);

    public string StrategyExplanation => LimitPhrases.Explain(HandoffStrategy.MostHeadroom);

    public bool ResumesByItself { get; set; } = true;

    public bool WindsDown { get; set; } = true;

    public bool CompactsOnResume { get; set; } = true;

    public IReadOnlyList<ILimitConnectionViewModel> Connections { get; init; } =
    [
        new DesignLimitConnectionViewModel(),
        new DesignLimitConnectionViewModel { Name = "claude-team", Position = "2", CanMoveUp = true },
        new DesignLimitConnectionViewModel { Name = "claude-max", Position = "3", CanMoveUp = true },
        new DesignLimitConnectionViewModel { Name = "codex-personal", Position = "4", CanMoveUp = true, IsIncluded = false },
    ];

    public string ConnectionsHint => LimitPhrases.OnlyThese(3);

    public string Summary { get; init; } = LimitPhrases.Summary(new LimitSettings(OnLimit.HandOffAnyHarness, 0.9, 0.97, []) { Resume = ResumeAtReset.Auto });

    public string File { get; init; } = "limits.json: applied";

    public string Error { get; init; } = string.Empty;

    public bool IsChanged { get; init; }

    public IAsyncRelayCommand SaveCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

internal sealed class DesignLimitConnectionViewModel : ILimitConnectionViewModel
{
    public string Name { get; init; } = "claude-work";

    public bool IsIncluded { get; set; } = true;

    public string Position { get; init; } = "1";

    public bool CanMoveUp { get; init; }

    public IRelayCommand MoveUpCommand { get; } = new RelayCommand(() => { });
}
