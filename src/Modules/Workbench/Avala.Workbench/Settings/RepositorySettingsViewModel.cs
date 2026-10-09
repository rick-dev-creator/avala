using System.Collections.ObjectModel;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Machine;
using Avala.Workbench.Presenting;
using Avala.Workbench.RepositoryRules;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Settings;

internal interface IRepositorySettingsViewModel
{
    IReadOnlyList<string> Repositories { get; }

    IReadOnlyList<IRuleFileViewModel> Files { get; }

    IReadOnlyList<IRuleViewModel> Rules { get; }

    IReadOnlyList<ICapsViewModel> Caps { get; }

    IReadOnlyList<ICheckViewModel> Checks { get; }

    IReadOnlyList<IJobSectionViewModel> JobSections { get; }

    string Repository { get; set; }

    string Shown { get; }

    string Autonomy { get; }

    string FormStrategy { get; }

    string Error { get; }

    IAsyncRelayCommand ReadCommand { get; }

    IAsyncRelayCommand<IRuleFileViewModel> EditCommand { get; }

    IAsyncRelayCommand<string> OpenCommand { get; }

    string Name { get; }

    string AutonomyNote { get; }

    IRuleFileViewModel? PermissionsFile { get; }

    IRuleFileViewModel? BudgetFile { get; }

    Task LoadAsync(CancellationToken cancellationToken);
}

[INotifyPropertyChanged]
internal sealed partial class RepositorySettingsViewModel(RulesReader reader, SettingsFiles files, JobBoard board) : IRepositorySettingsViewModel
{
    private readonly ObservableCollection<string> repositories = [];
    private readonly ObservableCollection<RuleFileViewModel> ruleFiles = [];
    private readonly ObservableCollection<RuleViewModel> rules = [];
    private readonly ObservableCollection<CapsViewModel> caps = [];
    private readonly ObservableCollection<CheckViewModel> checks = [];
    private readonly ObservableCollection<JobSectionViewModel> jobSections = [];

    public IReadOnlyList<string> Repositories => repositories;

    public IReadOnlyList<IRuleFileViewModel> Files => ruleFiles;

    public IReadOnlyList<IRuleViewModel> Rules => rules;

    public IReadOnlyList<ICapsViewModel> Caps => caps;

    public IReadOnlyList<ICheckViewModel> Checks => checks;

    public IReadOnlyList<IJobSectionViewModel> JobSections => jobSections;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReadCommand))]
    public partial string Repository { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    public partial string Shown { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Autonomy { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string FormStrategy { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string AutonomyNote { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IRuleFileViewModel? PermissionsFile { get; private set; }

    [ObservableProperty]
    public partial IRuleFileViewModel? BudgetFile { get; private set; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        repositories.ShowOnly(board.Jobs.Values.OrderByDescending(job => job.Summary.Submitted).Select(job => job.Summary.Repository).Distinct());

        if (string.IsNullOrWhiteSpace(Repository) && repositories.Count > 0)
        {
            Repository = repositories[0];
        }

        if (CanRead())
        {
            await ReadAsync(cancellationToken);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRead))]
    private async Task ReadAsync(CancellationToken cancellationToken) => Show(await reader.ReadAsync(Repository.Trim(), cancellationToken));

    [RelayCommand]
    private async Task OpenAsync(string? repository, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(repository))
        {
            Repository = repository;
            await ReadAsync(cancellationToken);
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task EditAsync(IRuleFileViewModel? file, CancellationToken cancellationToken) =>
        Error = file is null
            ? string.Empty
            : (await files.OpenInRepositoryAsync(Shown, file.Path, cancellationToken)).Match(_ => string.Empty, SettingsPhrases.Opening);

    private bool CanRead() => !string.IsNullOrWhiteSpace(Repository);

    private bool CanEdit(IRuleFileViewModel? file) => file is not null && Shown.Length > 0;

    private void Show(RulesOfRepository read)
    {
        Shown = read.Repository;
        Name = SettingsPhrases.Name(read.Repository);
        AutonomyNote = SettingsPhrases.Autonomy(read.Policy.Autonomy.ToString());
        Error = string.Empty;
        Autonomy = read.Policy.Autonomy.ToString();
        FormStrategy = SettingsPhrases.Strategy(read.Policy.Strategy);
        RuleFileViewModel[] declared =
        [
            new(RuleFiles.Permissions, SettingsPhrases.Status(read.Policy.File, read.Policy.Error), read.Policy.Origin, $"{Autonomy}, {FormStrategy.ToLowerInvariant()}, {Overview.OverviewPhrases.Count(read.Policy.Rules.Count, "rule")}"),
            new(RuleFiles.Budget, SettingsPhrases.Status(read.Budget.File, read.Budget.Error), read.Budget.Origin, Overview.OverviewPhrases.Count(read.Budget.Connections.Count + 1, "scope")),
            new(RuleFiles.Checks, read.Checks.File.ToString(), read.Checks.Origin, read.Checks.Checks.Count == 0 ? "no checks" : string.Join(", ", read.Checks.Checks.Select(check => check.Name))),
            new(RuleFiles.Jobs, read.Jobs.File.ToString(), read.Jobs.Origin, Overview.OverviewPhrases.Count(read.Jobs.Sections.Count, "section")),
        ];
        CapsViewModel[] capped =
        [
            new("Every connection", Amounts.Caps(read.Budget.Caps), SettingsPhrases.Lines(read.Budget.Caps)),
            .. read.Budget.Connections.Select(connection => new CapsViewModel(connection.Connection.Value, Amounts.Caps(connection.Caps), SettingsPhrases.Lines(connection.Caps))),
        ];
        ruleFiles.ShowOnly(declared);
        rules.ShowOnly(read.Policy.Rules.Select((rule, index) => new RuleViewModel(rule, index + 1)));
        PermissionsFile = declared[0];
        BudgetFile = declared[1];
        caps.ShowOnly(capped);
        checks.ShowOnly(read.Checks.Checks.Select(check => new CheckViewModel(check)));
        jobSections.ShowOnly(read.Jobs.Sections.Select(section => new JobSectionViewModel(section.Name, section.Value)));
    }
}
