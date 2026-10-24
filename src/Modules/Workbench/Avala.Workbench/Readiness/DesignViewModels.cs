using Avala.Components.Privacy;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Readiness;

internal sealed class DesignReadinessAccountViewModel : IReadinessAccountViewModel
{
    public Personal Login { get; init; } = Personal.Login("octocat");

    public string Tool { get; init; } = "gh";

    public IAsyncRelayCommand ConnectCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

internal sealed class DesignReadinessItemViewModel : IReadinessItemViewModel
{
    public ReadinessNeed Need { get; init; } = ReadinessNeed.Forge;

    public PersonalPhrase Text { get; init; } = new("Connect github.com as ", Personal.Login("octocat"), $" through gh {ReadinessPhrases.Purpose}");

    public string Action { get; init; } = ReadinessPhrases.Connect;

    public string Command { get; init; } = string.Empty;

    public bool IsDone { get; init; }

    public bool IsWorking { get; init; }

    public string Error { get; init; } = string.Empty;

    public IReadOnlyList<IReadinessAccountViewModel> Accounts { get; init; } = [];

    public IAsyncRelayCommand ActCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

internal sealed class DesignReadinessCardViewModel : IReadinessCardViewModel
{
    public bool IsShown { get; init; } = true;

    public IReadOnlyList<IReadinessItemViewModel> Items { get; init; } =
    [
        new DesignReadinessItemViewModel(),
        new DesignReadinessItemViewModel { Need = ReadinessNeed.Rules, Text = new PersonalPhrase(ReadinessPhrases.RulesUncommitted), Action = ReadinessPhrases.Commit },
        new DesignReadinessItemViewModel { Need = ReadinessNeed.Checks, Text = new PersonalPhrase("Checks declared in .avala/checks.json"), Action = string.Empty, IsDone = true },
    ];
}

internal sealed class DesignAgentPullRequestNoteViewModel : IAgentPullRequestNoteViewModel
{
    public bool IsShown { get; init; } = true;

    public string Text { get; init; } = "Pull request #123 was opened by the agent. Connect github.com in Settings to follow its checks.";

    public IRelayCommand OpenSettingsCommand { get; } = new RelayCommand(() => { });
}
