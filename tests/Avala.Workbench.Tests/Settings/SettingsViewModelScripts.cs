using Avala.Testing;
using Avala.Workbench.Settings;

namespace Avala.Workbench.Tests.Settings;

public sealed class SettingsViewModelScripts
{
    [Fact]
    public async Task ActivatingThePageLoadsTheMachineThenTheRepository()
    {
        var calls = new List<string>();
        var page = new SettingsViewModel(new Loading("repository", calls), new Loading("machine", calls));

        page.Activate();
        await page.Loading;

        Assert.Equal(("Settings", "machine,repository"), (page.Title, string.Join(",", calls)));
    }

    [Fact]
    public void ThePageHoldsTheRepositoryAndMachineSettings()
    {
        var repository = new DesignRepositorySettingsViewModel();
        var machine = new DesignMachineSettingsViewModel();

        ViewModelScript.Given(new SettingsViewModel(repository, machine))
            .Then(page => Assert.Equal((repository, machine), (page.Repository, page.Machine)));
    }

    private sealed class Loading(string name, List<string> calls) : IRepositorySettingsViewModel, IMachineSettingsViewModel
    {
        private readonly DesignRepositorySettingsViewModel repository = new();
        private readonly DesignMachineSettingsViewModel machine = new();

        public IReadOnlyList<string> Repositories => repository.Repositories;

        public IDefaultConnectionViewModel DefaultConnection => machine.DefaultConnection;

        IConnectionEditorViewModel IMachineSettingsViewModel.Editor => machine.Editor;

        IRuleFileEditorViewModel IRepositorySettingsViewModel.Editor => repository.Editor;

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

        public bool IsSilenceChanged => false;

        public double SilenceMinutes { get; set; } = 10;

        public IReadOnlyList<IMachineConnectionViewModel> Connections => machine.Connections;

        public string ConnectionsFile => machine.ConnectionsFile;

        public string Silence => machine.Silence;

        public string SupervisionFile => machine.SupervisionFile;

        public string SilenceDraft { get; set; } = string.Empty;

        public string Resources => machine.Resources;

        public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand SaveSilenceCommand => machine.SaveSilenceCommand;

        public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand OpenConnectionsCommand => machine.OpenConnectionsCommand;

        public Task LoadAsync(CancellationToken cancellationToken)
        {
            calls.Add(name);

            return Task.CompletedTask;
        }
    }
}
