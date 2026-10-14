using CommunityToolkit.Mvvm.Input;

namespace Avala.ClaudeCode.Settings;

internal sealed class DesignClaudeCodeLoginViewModel : IClaudeCodeLoginViewModel
{
    public string Name { get; init; } = "claude-work";

    public string Where { get; init; } = "~/.claude-work";

    public bool IsSmall => false;

    public bool IsStandard { get; init; } = true;

    public bool IsLarge => false;

    public bool IsFull => false;

    public string Custom { get; init; } = string.Empty;

    public bool HasCustom => Custom.Length > 0;

    public bool AutoCompact { get; init; } = true;

    public bool IdleCompaction { get; init; } = true;

    public string Summary { get; init; } = "Summarizes the conversation at about 217k tokens, so each message reads at most that much";

    public string Problem { get; init; } = string.Empty;

    public bool HasProblem => Problem.Length > 0;

    public IAsyncRelayCommand<CompactChoice> ChooseCommand { get; init; } = new AsyncRelayCommand<CompactChoice>(_ => Task.CompletedTask);

    public IAsyncRelayCommand RetryCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand ToggleAutoCompactCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand ToggleIdleCompactionCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

internal sealed class DesignClaudeCodeSettingsViewModel : IClaudeCodeSettingsViewModel
{
    public IReadOnlyList<IClaudeCodeLoginViewModel> Logins { get; init; } =
    [
        new DesignClaudeCodeLoginViewModel(),
        new DesignClaudeCodeLoginViewModel { Name = "claude-personal", Where = "~/.claude", IsStandard = false, Custom = "400k in connections.json", Summary = "Summarizes the conversation at about 367k tokens, so each message reads at most that much" },
    ];

    public bool HasLogins => Logins.Count > 0;
}
