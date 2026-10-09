using Avala.Permissions.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Settings;

internal sealed record DesignRuleFileViewModel(string Path, string Status, string Commit, bool EditedInCheckout, string Summary) : IRuleFileViewModel
{
    public DesignRuleFileViewModel()
        : this(".avala/permissions.json", "Applied", "4be19c2", false, "Autonomous, recommended options, 6 rules")
    {
    }

    public bool IsRejected => Status.StartsWith("Rejected", StringComparison.Ordinal);

    public bool CanEditHere => Path != ".avala/jobs.json";
}

[INotifyPropertyChanged]
internal sealed partial class DesignRuleFileEditorViewModel : IRuleFileEditorViewModel
{
    public bool IsOpen { get; init; } = true;

    public string Path => ".avala/budget.json";

    [ObservableProperty]
    public partial string Content { get; set; } = "{\n  \"costPerJob\": { \"USD\": 5.00 },\n  \"holdAtLimit\": 0.9\n}\n";

    public string Note => RuleFilePhrases.Note(".avala/budget.json", exists: true);

    public string Error { get; init; } = string.Empty;

    public IAsyncRelayCommand SaveCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IRelayCommand CancelCommand { get; } = new RelayCommand(() => { });
}

internal sealed record DesignRuleViewModel(int Order, string Name, string Origin, string Kind, string Target, PolicyAnswer Answer) : IRuleViewModel
{
    public DesignRuleViewModel()
        : this(3, "tests", "Repository", "Command", "go test *, go vet *, golangci-lint *", PolicyAnswer.Allow)
    {
    }

    public RuleScope Scope => RuleScope.Anywhere;
}

internal sealed class DesignCapsViewModel(string scope, string caps, IReadOnlyList<SettingLine> lines) : ICapsViewModel
{
    public DesignCapsViewModel()
        : this("Every connection", "3 USD per job, 400,000 tokens per job, holds at 90% of a limit", [new("Cost per job", "3 USD"), new("Tokens per job", "400,000"), new("Hold when a usage window reaches", "90%")])
    {
    }

    public string Scope { get; } = scope;

    public string Caps { get; } = caps;

    public IReadOnlyList<SettingLine> Lines { get; } = lines;
}

internal sealed class DesignCheckViewModel(string name, string command, string timeout) : ICheckViewModel
{
    public DesignCheckViewModel()
        : this("test", "go test ./...", "300s")
    {
    }

    public string Name { get; } = name;

    public string Command { get; } = command;

    public string Timeout { get; } = timeout;
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

    public string Origin => IsDeclared ? "declared in connections.json" : "discovered on this machine";

    public bool IsDefault { get; } = isDefault;

    public bool IsDeclared { get; init; } = isDefault;

    public string Reference => string.Empty;

    public IRelayCommand EditCommand { get; } = new RelayCommand(() => { });

    public IRelayCommand RemoveCommand { get; } = new RelayCommand(() => { });
}

[INotifyPropertyChanged]
internal sealed partial class DesignConnectionEditorViewModel : IConnectionEditorViewModel
{
    public bool IsOpen { get; init; } = true;

    public string Title => "New connection";

    [ObservableProperty]
    public partial string Name { get; set; } = "claude-team";

    public IReadOnlyList<string> Providers { get; } = ["Claude Code · claude-code"];

    [ObservableProperty]
    public partial int Provider { get; set; }

    public IReadOnlyList<string> Sources { get; } = [ConnectionPhrases.OwnLogin, ConnectionPhrases.Source("login"), ConnectionPhrases.Source("apiKey")];

    [ObservableProperty]
    public partial int Source { get; set; } = 2;

    [ObservableProperty]
    public partial string Reference { get; set; } = "TEAM_API_KEY";

    public bool NeedsReference => true;

    public string ReferenceHint => ConnectionPhrases.ReferenceHint("apiKey");

    public string Error => string.Empty;

    public string Removing { get; init; } = string.Empty;

    public IRelayCommand NewCommand { get; } = new RelayCommand(() => { });

    public IAsyncRelayCommand SaveCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IRelayCommand CancelCommand { get; } = new RelayCommand(() => { });

    public IAsyncRelayCommand RemoveCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IRelayCommand KeepCommand { get; } = new RelayCommand(() => { });
}

[INotifyPropertyChanged]
internal sealed partial class DesignRepositorySettingsViewModel : IRepositorySettingsViewModel
{
    public IReadOnlyList<string> Repositories { get; } = ["~/code/ledger-api", "~/code/web-console", "~/code/billing-worker", "~/code/mobile-sync"];

    public IReadOnlyList<IRuleFileViewModel> Files { get; } =
    [
        new DesignRuleFileViewModel(),
        new DesignRuleFileViewModel(".avala/budget.json", "Applied", "4be19c2", false, "1 scope"),
        new DesignRuleFileViewModel(".avala/checks.json", "Applied", "4be19c2", true, "lint, vet, test, build"),
        new DesignRuleFileViewModel(".avala/jobs.json", "Absent", "no commit", false, "0 sections"),
    ];

    public IReadOnlyList<IRuleViewModel> Rules { get; } =
    [
        new DesignRuleViewModel(1, "Anything outside the worktree", "Built-in", "any", "anything", PolicyAnswer.Deny),
        new DesignRuleViewModel(2, "Ask before editing applied migrations", "Repository", "FileEdit", "migrations/applied/**", PolicyAnswer.Ask),
        new DesignRuleViewModel(),
        new DesignRuleViewModel(4, "No pushes", "Repository", "Command", "git push *", PolicyAnswer.Deny),
        new DesignRuleViewModel(5, "Docs", "Repository", "Web", "pkg.go.dev", PolicyAnswer.Allow),
        new DesignRuleViewModel(6, "Anything else inside the worktree", "Built-in", "any", "anything", PolicyAnswer.Allow),
    ];

    public IReadOnlyList<ICapsViewModel> Caps { get; } = [new DesignCapsViewModel()];

    public IReadOnlyList<ICheckViewModel> Checks { get; } =
    [
        new DesignCheckViewModel("lint", "golangci-lint run", "120s"),
        new DesignCheckViewModel("vet", "go vet ./...", "120s"),
        new DesignCheckViewModel(),
        new DesignCheckViewModel("build", "go build ./...", "300s"),
    ];

    public IReadOnlyList<IJobSectionViewModel> JobSections { get; } = [];

    [ObservableProperty]
    public partial string Repository { get; set; } = "~/code/ledger-api";

    public string Shown => "~/code/ledger-api";

    public string Name => "ledger-api";

    public string Autonomy => "Autonomous";

    public string AutonomyNote => SettingsPhrases.Autonomy(Autonomy);

    public string FormStrategy => "Recommended options";

    public string Error => string.Empty;

    public string Notice { get; init; } = string.Empty;

    public IRuleFileViewModel? PermissionsFile => Files[0];

    public IRuleFileViewModel? BudgetFile => Files[1];

    public IAsyncRelayCommand ReadCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand<IRuleFileViewModel> EditCommand { get; } = new AsyncRelayCommand<IRuleFileViewModel>(_ => Task.CompletedTask);

    public IAsyncRelayCommand<IRuleFileViewModel> EditHereCommand { get; } = new AsyncRelayCommand<IRuleFileViewModel>(_ => Task.CompletedTask);

    public IRuleFileEditorViewModel Editor { get; init; } = new DesignRuleFileEditorViewModel { IsOpen = false };

    public IAsyncRelayCommand<string> OpenCommand { get; } = new AsyncRelayCommand<string>(_ => Task.CompletedTask);

    public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

[INotifyPropertyChanged]
internal sealed partial class DesignMachineSettingsViewModel : IMachineSettingsViewModel
{
    public IDefaultConnectionViewModel DefaultConnection { get; } = new DesignDefaultConnectionViewModel();

    public IConnectionEditorViewModel Editor { get; init; } = new DesignConnectionEditorViewModel { IsOpen = false };

    public IReadOnlyList<IMachineConnectionViewModel> Connections { get; } =
    [
        new DesignMachineConnectionViewModel("claude-work", "keychain: claude-work", false) { IsDeclared = true },
        new DesignMachineConnectionViewModel("claude-personal", "the provider's own login", false),
    ];

    public string ConnectionsFile => "Applied";

    public string Silence => "600s";

    public string SupervisionFile => "Applied";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSilenceChanged), nameof(SilenceMinutes))]
    public partial string SilenceDraft { get; set; } = "600";

    public bool IsSilenceChanged => SilenceDraft != "600";

    public double SilenceMinutes
    {
        get => SilenceDial.Minutes(SilenceDraft, Silence);
        set => SilenceDraft = SilenceDial.Draft(SilenceDraft, Silence, value);
    }

    public string Resources => "Applied: samples every 5s, orphans Kill, ports 41000-41999 by 10";

    public string Error => string.Empty;

    public string Notice { get; init; } = string.Empty;

    public IAsyncRelayCommand SaveSilenceCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand OpenConnectionsCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

[INotifyPropertyChanged]
internal sealed partial class DesignDefaultConnectionViewModel : IDefaultConnectionViewModel
{
    public event EventHandler<Agents.Contracts.Connections.ConnectionCatalog>? Changed
    {
        add { }
        remove { }
    }

    public IReadOnlyList<string> Choices { get; } = [DefaultPhrases.Auto, "claude-work", "claude-personal"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChanged), nameof(IsRecommended))]
    public partial string Draft { get; set; } = DefaultPhrases.Auto;

    public string Saved => DefaultPhrases.Auto;

    public bool IsChanged => Draft != Saved;

    public bool IsRecommended => Draft == DefaultPhrases.Auto;

    public string Explanation => "Recommended. A job that names no connection, in a repository that names none, runs on the connection with the most capacity left.";

    public string Error => string.Empty;

    public IAsyncRelayCommand SaveCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public void Show(Agents.Contracts.Connections.ConnectionCatalog catalog)
    {
    }
}

internal sealed class DesignSettingsViewModel : ISettingsViewModel
{
    public string Title => "Settings";

    public IRepositorySettingsViewModel Repository { get; } = new DesignRepositorySettingsViewModel();

    public IMachineSettingsViewModel Machine { get; } = new DesignMachineSettingsViewModel();
}
