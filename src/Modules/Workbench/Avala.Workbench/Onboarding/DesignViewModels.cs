using Avala.Components.Privacy;
using Avala.Workbench.Accounts;
using Avala.Workbench.Telemetry;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Onboarding;

internal sealed class DesignOnboardingViewModel : IOnboardingViewModel
{
    public bool IsShown { get; init; } = true;

    public IOnboardingStep Step { get; init; } = new DesignWelcomeStepViewModel();

    public IReadOnlyList<OnboardingDot> Dots { get; init; } = [new(true), new(false), new(false), new(false)];

    public bool IsLast { get; init; }

    public string ContinueLabel { get; init; } = OnboardingPhrases.Continue;

    public ICodeStepViewModel Code { get; init; } = new DesignCodeStepViewModel();

    public IRelayCommand ContinueCommand { get; init; } = new RelayCommand(() => { });

    public IRelayCommand SkipCommand { get; init; } = new RelayCommand(() => { });
}

internal sealed class DesignWelcomeStepViewModel : IWelcomeStepViewModel
{
    public string Title => OnboardingPhrases.WelcomeTitle;

    public string Text => OnboardingPhrases.WelcomeText;

    public string Detail => OnboardingPhrases.WelcomeDetail;

    public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class DesignAgentRowViewModel : IAgentRowViewModel
{
    public string Name { get; init; } = "claude-work";

    public string Monogram => AccountPhrases.Monogram(Name);

    public string Provider { get; init; } = "Claude Code";

    public AgentState State { get; init; }

    public string Status => AgentPhrases.Status(State, Sdk.Option<Agents.Contracts.Connections.ConnectionError>.None);
}

internal sealed class DesignInstallRowViewModel : IInstallRowViewModel
{
    public string Provider { get; init; } = "Claude Code";

    public string Command { get; init; } = "npm install -g @anthropic-ai/claude-code";

    public string SignIn => AgentPhrases.SignIn("claude");

    public bool IsCopied => false;

    public IAsyncRelayCommand CopyCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

internal sealed class DesignAgentsStepViewModel : IAgentsStepViewModel
{
    public IReadOnlyList<IAgentRowViewModel> Agents { get; init; } =
    [
        new DesignAgentRowViewModel(),
        new DesignAgentRowViewModel { Name = "claude-personal", State = AgentState.AtLimit },
    ];

    public IReadOnlyList<IInstallRowViewModel> Installs { get; init; } = [new DesignInstallRowViewModel()];

    public bool HasAgents => Agents.Count > 0;

    public IAsyncRelayCommand CheckAgainCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class DesignCodeStepViewModel : ICodeStepViewModel
{
    public Personal Folder { get; init; } = Personal.Path("/home/sam/Projects");

    public ISourceControlViewModel Accounts { get; init; } = new DesignSourceControlViewModel();

    public IAsyncRelayCommand ChangeFolderCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class DesignPrivacyStepViewModel : IPrivacyStepViewModel
{
    public IPersonalDetailsViewModel PersonalDetails { get; init; } = new DesignPersonalDetailsViewModel();

    public ITelemetryViewModel Statistics { get; init; } = new DesignTelemetryViewModel();

    public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task ConcludeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
