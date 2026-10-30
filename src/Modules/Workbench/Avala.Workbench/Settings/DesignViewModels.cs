using Avala.Components.Privacy;
using Avala.Workbench.Accounts;
using Avala.Permissions.Contracts;
using Avala.Sdk.Appearance;
using Avala.Workbench.ModelChoices;
using Avala.Workbench.RepositoryChoice;
using Avala.Workbench.Telemetry;
using Avala.Workbench.Updates;
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

internal sealed class DesignSettingsEntryViewModel : ISettingsEntryViewModel
{
    public SettingsSection Section { get; init; } = SettingsSection.Accounts;

    public string Title { get; init; } = "Accounts";

    public string Icon { get; init; } = "IconPerson";

    public bool IsSelected { get; init; } = true;
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

    public string Monogram => AccountPhrases.Monogram(Name);

    public string Provider => "claude-code";

    public string Source { get; } = source;

    public string Origin => IsDeclared ? "declared in connections.json" : "discovered on this machine";

    public bool IsDefault { get; } = isDefault;

    public bool IsDeclared { get; init; } = isDefault;

    public string Reference => string.Empty;

    public string Problem { get; init; } = string.Empty;

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

    public IModelPickerViewModel Models { get; } = new DesignModelPickerViewModel
    {
        Models = [ModelPhrases.HarnessDefault, "opus", "sonnet", "haiku"],
        Efforts = [ModelPhrases.HarnessDefault, "low", "medium", "high", "xhigh", "max"],
        Note = ModelPhrases.ConnectionNote,
    };

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
    public IDocsSettingViewModel Docs { get; } = new DesignDocsSettingViewModel();

    public IReadOnlyList<string> Repositories { get; } = ["~/code/ledger-api", "~/code/web-console", "~/code/billing-worker", "~/code/mobile-sync"];

    public IRepositoryFieldViewModel RepositoryField { get; } = new DesignRepositoryFieldViewModel
    {
        Repository = "~/code/ledger-api",
        Repositories = ["~/code/ledger-api", "~/code/web-console", "~/code/billing-worker", "~/code/mobile-sync"],
        Problem = string.Empty,
        Offer = string.Empty,
        Creation = new DesignNewRepositoryViewModel { IsOpen = false },
    };

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

    public IReadOnlyList<ICheckViewModel> Checks { get; init; } =
    [
        new DesignCheckViewModel("lint", "golangci-lint run", "120s"),
        new DesignCheckViewModel("vet", "go vet ./...", "120s"),
        new DesignCheckViewModel(),
        new DesignCheckViewModel("build", "go build ./...", "300s"),
    ];

    public IReadOnlyList<IJobSectionViewModel> JobSections { get; init; } = [];

    [ObservableProperty]
    public partial string Repository { get; set; } = "~/code/ledger-api";

    public string Shown { get; init; } = "~/code/ledger-api";

    public string Name => "ledger-api";

    public string Autonomy => "Autonomous";

    public string AutonomyNote => SettingsPhrases.Autonomy(Autonomy);

    public string FormStrategy => "Recommended options";

    public string Error => string.Empty;

    public string Notice { get; init; } = string.Empty;

    public IRuleFileViewModel? PermissionsFile => Files[0];

    public IRuleFileViewModel? BudgetFile => Files[1];

    public IRuleFileViewModel? ChecksFile => Files.Count > 2 ? Files[2] : null;

    public IRuleFileViewModel? JobsFile => Files.Count > 3 ? Files[3] : null;

    public RepositoryTab Tab { get; init; } = RepositoryTab.Permissions;

    public bool ShowsPermissions => Tab == RepositoryTab.Permissions;

    public bool ShowsChecks => Tab == RepositoryTab.Checks;

    public bool ShowsBudget => Tab == RepositoryTab.Budget;

    public bool ShowsDelivery => Tab == RepositoryTab.Delivery;

    public bool ShowsDocs => Tab == RepositoryTab.Docs;

    public bool ShowsFiles => Tab == RepositoryTab.Files;

    public IRelayCommand<RepositoryTab> ShowTabCommand { get; } = new RelayCommand<RepositoryTab>(_ => { });

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

    public INotificationsViewModel Notifications { get; } = new DesignNotificationsViewModel();

    public IWorktreeSettingsViewModel Worktrees { get; } = new DesignWorktreeSettingsViewModel();

    public IReadOnlyList<object> Harnesses { get; init; } = [];

    public IReadOnlyList<object> Fleet { get; init; } = [];

    public IMachineAccountsViewModel Accounts { get; init; } = new DesignMachineAccountsViewModel();

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

    public IAsyncRelayCommand SaveSilenceCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class DesignMachineAccountsViewModel : IMachineAccountsViewModel
{
    public IConnectionEditorViewModel Editor { get; init; } = new DesignConnectionEditorViewModel { IsOpen = false };

    public IReadOnlyList<IMachineConnectionViewModel> Connections { get; } =
    [
        new DesignMachineConnectionViewModel("claude-work", "keychain: claude-work", false) { IsDeclared = true },
        new DesignMachineConnectionViewModel("claude-personal", "the provider's own login", false),
    ];

    public string ConnectionsFile => "Applied";

    public ISourceControlViewModel SourceControl { get; init; } = new DesignSourceControlViewModel();

    public IPersonalDetailsViewModel PersonalDetails { get; init; } = new DesignPersonalDetailsViewModel();

    public string ForgesFile => "forges.json · checks every 60s · GitHub, Gitea, Forgejo installed";

    public string Error => string.Empty;

    public string Notice { get; init; } = string.Empty;

    public IAsyncRelayCommand OpenConnectionsCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
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

    public IMachineSettingsViewModel Machine { get; init; } = new DesignMachineSettingsViewModel();

    public IAppearanceViewModel Appearance { get; } = new DesignAppearanceViewModel();

    public IAboutViewModel About { get; } = new DesignAboutViewModel();

    public IReadOnlyList<ISettingsEntryViewModel> Entries => [.. SectionPhrases.Entries().Select(entry => new SettingsEntryViewModel(entry.Section, entry.Title, entry.Icon) { IsSelected = entry.Section == Section })];

    public SettingsSection Section { get; init; } = SettingsSection.Accounts;

    public SectionHeading Heading => SectionPhrases.Heading(Section);

    public string ShownRepository => ShowsRepository ? Repository.Shown : string.Empty;

    public bool ShowsAccounts => Section == SettingsSection.Accounts;

    public bool ShowsConnections => Section == SettingsSection.Connections;

    public bool ShowsSupervision => Section == SettingsSection.Supervision;

    public bool ShowsWorktrees => Section == SettingsSection.Worktrees;

    public bool ShowsNotifications => Section == SettingsSection.Notifications;

    public bool ShowsFleet => Section == SettingsSection.Fleet;

    public bool ShowsAppearance => Section == SettingsSection.Appearance;

    public bool ShowsAbout => Section == SettingsSection.About;

    public bool ShowsRepository => Section == SettingsSection.Repository;

    public IRelayCommand<SettingsSection> ShowCommand { get; } = new RelayCommand<SettingsSection>(_ => { });

    public IAsyncRelayCommand<string> OpenRepositoryCommand { get; } = new AsyncRelayCommand<string>(_ => Task.CompletedTask);

    public IAsyncRelayCommand ReadRepositoryCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

internal sealed class DesignAboutViewModel : IAboutViewModel
{
    public string Version => "0.9.0-beta.1";

    public string Commit => "5c371e6a1b2d";

    public Personal LogFolder => Personal.Path("/home/dana/.local/share/Avala/logs");

    public PersonalPhrase Note { get; init; } = PersonalPhrase.Empty;

    public IUpdateViewModel Update { get; init; } = new DesignUpdateViewModel();

    public ITelemetryViewModel Telemetry { get; init; } = new DesignTelemetryViewModel();

    public IAsyncRelayCommand OpenLogFolderCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand OpenRepositoryCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand OpenLicenseCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IRelayCommand ShowTourCommand { get; } = new RelayCommand(() => { });

    public void Refresh()
    {
    }
}

internal sealed class DesignAppearanceViewModel : IAppearanceViewModel
{
    public string File => "Applied";

    public bool FollowsSystem => true;

    public bool IsLight => false;

    public bool IsDark => false;

    public bool MotionFollowsSystem => true;

    public bool IsMotionReduced => false;

    public bool IsMotionFull => false;

    public bool IsReadingComfortable => true;

    public bool IsReadingWide => false;

    public bool SendsWithCommandEnter => false;

    public string Error { get; init; } = string.Empty;

    public IPersonalDetailsViewModel PersonalDetails { get; } = new DesignPersonalDetailsViewModel();

    public IAsyncRelayCommand<ThemeChoice> ChooseThemeCommand { get; } = new AsyncRelayCommand<ThemeChoice>(_ => Task.CompletedTask);

    public IAsyncRelayCommand<MotionChoice> ChooseMotionCommand { get; } = new AsyncRelayCommand<MotionChoice>(_ => Task.CompletedTask);

    public IAsyncRelayCommand<ReadingChoice> ChooseReadingCommand { get; } = new AsyncRelayCommand<ReadingChoice>(_ => Task.CompletedTask);

    public IAsyncRelayCommand ToggleSendKeyCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class DesignDocsSettingViewModel : IDocsSettingViewModel
{
    public bool IsShown => true;

    public bool IsOn { get; init; } = true;

    public string Folder => "docs/features";

    public string Writer => "research · haiku · low";

    public string Note => DocsPhrases.Uncommitted(on: true);

    public string Error => string.Empty;

    public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand SwitchCommand { get; } = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => Task.CompletedTask);

    public bool IsUncommitted { get; init; } = true;

    public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand CommitCommand { get; } = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => Task.CompletedTask);

    public string Site => "DocFX, laid out like Microsoft Learn, with the C4 player.";

    public bool CanCreateSite => true;

    public bool IsSitePreparing { get; init; }

    public CommunityToolkit.Mvvm.Input.IRelayCommand OpenSiteJobCommand { get; } = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { });

    public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand CreateSiteCommand { get; } = new CommunityToolkit.Mvvm.Input.AsyncRelayCommand(() => Task.CompletedTask);
}
