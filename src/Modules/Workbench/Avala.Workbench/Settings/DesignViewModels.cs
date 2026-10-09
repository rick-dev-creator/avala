using Avala.Permissions.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Settings;

internal sealed class DesignRuleFileViewModel(string path, string status, string commit, bool editedInCheckout) : IRuleFileViewModel
{
    public DesignRuleFileViewModel()
        : this(".avala/permissions.json", "Applied", "4f2c9e1", true)
    {
    }

    public string Path { get; } = path;

    public string Status { get; } = status;

    public string Commit { get; } = commit;

    public bool EditedInCheckout { get; } = editedInCheckout;
}

internal sealed record DesignRuleViewModel(string Name, string Origin, string Kind, string Target, PolicyAnswer Answer) : IRuleViewModel
{
    public DesignRuleViewModel()
        : this("tests", "Repository", "Command", "npm test*", PolicyAnswer.Allow)
    {
    }

    public RuleScope Scope => RuleScope.Anywhere;
}

internal sealed class DesignCapsViewModel(string scope, string caps) : ICapsViewModel
{
    public DesignCapsViewModel()
        : this("Every connection", "5 USD per job, holds at 90% of a limit")
    {
    }

    public string Scope { get; } = scope;

    public string Caps { get; } = caps;
}

internal sealed class DesignCheckViewModel : ICheckViewModel
{
    public string Name => "tests";

    public string Command => "npm test";

    public string Timeout => "300s";
}

internal sealed class DesignJobSectionViewModel : IJobSectionViewModel
{
    public string Name => "approval";

    public string Value => "merge";
}

internal sealed class DesignMachineConnectionViewModel(string name, string source, bool isDefault) : IMachineConnectionViewModel
{
    public DesignMachineConnectionViewModel()
        : this("claude-work", "keychain: claude-work", true)
    {
    }

    public string Name { get; } = name;

    public string Provider => "claude-code";

    public string Source { get; } = source;

    public bool IsDefault { get; } = isDefault;
}

[INotifyPropertyChanged]
internal sealed partial class DesignRepositorySettingsViewModel : IRepositorySettingsViewModel
{
    public IReadOnlyList<string> Repositories { get; } = ["~/code/shop-api", "~/code/shop-web"];

    public IReadOnlyList<IRuleFileViewModel> Files { get; } =
    [
        new DesignRuleFileViewModel(),
        new DesignRuleFileViewModel(".avala/budget.json", "Applied", "4f2c9e1", false),
        new DesignRuleFileViewModel(".avala/checks.json", "Declared", "4f2c9e1", false),
        new DesignRuleFileViewModel(".avala/jobs.json", "Absent", "no commit", false),
    ];

    public IReadOnlyList<IRuleViewModel> Rules { get; } =
    [
        new DesignRuleViewModel(),
        new DesignRuleViewModel("migrations", "Repository", "Command", "dotnet ef database update", PolicyAnswer.Ask),
        new DesignRuleViewModel("outside the worktree", "Built-in", "FileEdit", "anything", PolicyAnswer.Deny),
    ];

    public IReadOnlyList<ICapsViewModel> Caps { get; } =
    [
        new DesignCapsViewModel(),
        new DesignCapsViewModel("claude-personal", "2 USD per job"),
    ];

    public IReadOnlyList<ICheckViewModel> Checks { get; } = [new DesignCheckViewModel()];

    public IReadOnlyList<IJobSectionViewModel> JobSections { get; } = [];

    [ObservableProperty]
    public partial string Repository { get; set; } = "~/code/shop-api";

    public string Shown => "~/code/shop-api";

    public string Autonomy => "Supervised";

    public string FormStrategy => "Recommended options";

    public string Error => string.Empty;

    public IAsyncRelayCommand ReadCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand<IRuleFileViewModel> EditCommand { get; } = new AsyncRelayCommand<IRuleFileViewModel>(_ => Task.CompletedTask);

    public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

[INotifyPropertyChanged]
internal sealed partial class DesignMachineSettingsViewModel : IMachineSettingsViewModel
{
    public IReadOnlyList<IMachineConnectionViewModel> Connections { get; } =
    [
        new DesignMachineConnectionViewModel(),
        new DesignMachineConnectionViewModel("claude-personal", "the provider's own login", false),
    ];

    public string ConnectionsFile => "Applied";

    public string Silence => "600s";

    public string SupervisionFile => "Applied";

    [ObservableProperty]
    public partial string SilenceDraft { get; set; } = "600";

    public string Resources => "Applied: samples every 5s, orphans Kill, ports 41000-41999 by 10";

    public string Error => string.Empty;

    public IAsyncRelayCommand SaveSilenceCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand OpenConnectionsCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class DesignSettingsViewModel : ISettingsViewModel
{
    public string Title => "Settings";

    public IRepositorySettingsViewModel Repository { get; } = new DesignRepositorySettingsViewModel();

    public IMachineSettingsViewModel Machine { get; } = new DesignMachineSettingsViewModel();
}
