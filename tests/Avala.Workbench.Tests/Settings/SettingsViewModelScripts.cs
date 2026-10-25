using Avala.Testing;
using Avala.Workbench.RepositoryChoice;
using Avala.Workbench.Settings;

namespace Avala.Workbench.Tests.Settings;

public sealed class SettingsViewModelScripts
{
    [Fact]
    public async Task ActivatingThePageLoadsTheMachineTheAppearanceThenTheRepository()
    {
        var calls = new List<string>();
        var page = new SettingsViewModel(new Loading("repository", calls), new Loading("machine", calls), new AppearanceLoading(calls), new DesignAboutViewModel());

        page.Activate();
        await page.Loading;

        Assert.Equal(("Settings", "machine,appearance,repository"), (page.Title, string.Join(",", calls)));
    }

    [Fact]
    public void ThePageHoldsTheRepositoryMachineAndAppearanceSettings()
    {
        var repository = new DesignRepositorySettingsViewModel();
        var machine = new DesignMachineSettingsViewModel();
        var appearance = new DesignAppearanceViewModel();

        ViewModelScript.Given(new SettingsViewModel(repository, machine, appearance, new DesignAboutViewModel()))
            .Then(page => Assert.Equal<(object, object, object)>((repository, machine, appearance), (page.Repository, page.Machine, page.Appearance)));
    }

    [Fact]
    public void ThePageOpensOnAccountsAndListsThisComputersSectionsInOrder()
    {
        var page = Page();

        Assert.Equal(("Accounts,Connections,Supervision,Worktrees,Notifications,Fleet,Appearance,About", "Accounts", true), (string.Join(",", page.Entries.Select(entry => entry.Title)), page.Heading.Title, page.Entries[0].IsSelected));
    }

    [Fact]
    public void ChoosingASectionShowsOnlyItAndMarksOnlyItsEntry()
    {
        var page = Page();

        page.ShowCommand.Execute(SettingsSection.Supervision);

        Assert.Equal(("Supervision", "Supervision", false, true), (page.Heading.Title, string.Join(",", page.Entries.Where(entry => entry.IsSelected).Select(entry => entry.Title)), page.ShowsAccounts, page.ShowsSupervision));
    }

    [Fact]
    public async Task OpeningARepositoryShowsItsRulesAndNamesItAsTheShownRepositoryAsync()
    {
        var page = Page();

        await page.OpenRepositoryCommand.ExecuteAsync("~/code/ledger-api");

        Assert.Equal((true, "~/code/ledger-api", false), (page.ShowsRepository, page.ShownRepository, page.Entries.Any(entry => entry.IsSelected)));
    }

    [Fact]
    public void NoRepositoryIsMarkedWhileASectionOfThisComputerIsShown() =>
        Assert.Equal(string.Empty, Page().ShownRepository);

    [Fact]
    public async Task ReadingAnotherRepositoryShowsTheRepositorySectionAsync()
    {
        var page = Page();

        await page.ReadRepositoryCommand.ExecuteAsync(null);

        Assert.Equal(SettingsSection.Repository, page.Section);
    }

    private static SettingsViewModel Page() =>
        new(new DesignRepositorySettingsViewModel(), new DesignMachineSettingsViewModel(), new DesignAppearanceViewModel(), new DesignAboutViewModel());

    private sealed class AppearanceLoading(List<string> calls) : IAppearanceViewModel
    {
        private readonly DesignAppearanceViewModel appearance = new();

        public string File => appearance.File;

        public bool FollowsSystem => appearance.FollowsSystem;

        public bool IsLight => appearance.IsLight;

        public bool IsDark => appearance.IsDark;

        public bool MotionFollowsSystem => appearance.MotionFollowsSystem;

        public bool IsMotionReduced => appearance.IsMotionReduced;

        public bool IsMotionFull => appearance.IsMotionFull;

        public bool IsReadingComfortable => appearance.IsReadingComfortable;

        public bool IsReadingWide => appearance.IsReadingWide;

        public bool SendsWithCommandEnter => appearance.SendsWithCommandEnter;

        public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand ToggleSendKeyCommand => appearance.ToggleSendKeyCommand;

        public string Error => appearance.Error;

        public Components.Privacy.IPersonalDetailsViewModel PersonalDetails => appearance.PersonalDetails;

        public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand<Sdk.Appearance.ThemeChoice> ChooseThemeCommand => appearance.ChooseThemeCommand;

        public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand<Sdk.Appearance.MotionChoice> ChooseMotionCommand => appearance.ChooseMotionCommand;

        public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand<Sdk.Appearance.ReadingChoice> ChooseReadingCommand => appearance.ChooseReadingCommand;

        public Task LoadAsync(CancellationToken cancellationToken)
        {
            calls.Add("appearance");

            return Task.CompletedTask;
        }
    }

    private sealed class Loading(string name, List<string> calls) : IRepositorySettingsViewModel, IMachineSettingsViewModel
    {
        private readonly DesignRepositorySettingsViewModel repository = new();
        private readonly DesignMachineSettingsViewModel machine = new();

        public IReadOnlyList<string> Repositories => repository.Repositories;

        public IRepositoryFieldViewModel RepositoryField => repository.RepositoryField;

        public IDefaultConnectionViewModel DefaultConnection => machine.DefaultConnection;

        public INotificationsViewModel Notifications => machine.Notifications;

        public IWorktreeSettingsViewModel Worktrees => machine.Worktrees;

        public IMachineAccountsViewModel Accounts => machine.Accounts;

        public IReadOnlyList<object> Harnesses => machine.Harnesses;

        public IReadOnlyList<object> Fleet => machine.Fleet;

        public IRuleFileEditorViewModel Editor => repository.Editor;

        public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand<IRuleFileViewModel> EditHereCommand => repository.EditHereCommand;

        public IReadOnlyList<IRuleFileViewModel> Files => repository.Files;

        public IReadOnlyList<IRuleViewModel> Rules => repository.Rules;

        public IReadOnlyList<ICapsViewModel> Caps => repository.Caps;

        public IReadOnlyList<ICheckViewModel> Checks => repository.Checks;

        public IReadOnlyList<IJobSectionViewModel> JobSections => repository.JobSections;

        public string Repository { get; set; } = string.Empty;

        public string Shown => repository.Shown;

        public string Autonomy => repository.Autonomy;

        public string FormStrategy => repository.FormStrategy;

        public string Error => string.Empty;

        public string Notice => string.Empty;

        public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand ReadCommand => repository.ReadCommand;

        public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand<IRuleFileViewModel> EditCommand => repository.EditCommand;

        public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand<string> OpenCommand => repository.OpenCommand;

        public string Name => repository.Name;

        public string AutonomyNote => repository.AutonomyNote;

        public IRuleFileViewModel? PermissionsFile => repository.PermissionsFile;

        public IRuleFileViewModel? BudgetFile => repository.BudgetFile;

        public IRuleFileViewModel? ChecksFile => repository.ChecksFile;

        public IRuleFileViewModel? JobsFile => repository.JobsFile;

        public RepositoryTab Tab => repository.Tab;

        public bool ShowsPermissions => repository.ShowsPermissions;

        public bool ShowsChecks => repository.ShowsChecks;

        public bool ShowsBudget => repository.ShowsBudget;

        public bool ShowsDelivery => repository.ShowsDelivery;

        public bool ShowsDocs => repository.ShowsDocs;

        public bool ShowsFiles => repository.ShowsFiles;

        public CommunityToolkit.Mvvm.Input.IRelayCommand<RepositoryTab> ShowTabCommand => repository.ShowTabCommand;

        public IDocsSettingViewModel Docs => repository.Docs;

        public bool IsSilenceChanged => false;

        public double SilenceMinutes { get; set; } = 10;

        public string Silence => machine.Silence;

        public string SupervisionFile => machine.SupervisionFile;

        public string SilenceDraft { get; set; } = string.Empty;

        public string Resources => machine.Resources;

        public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand SaveSilenceCommand => machine.SaveSilenceCommand;

        public Task LoadAsync(CancellationToken cancellationToken)
        {
            calls.Add(name);

            return Task.CompletedTask;
        }
    }
}
