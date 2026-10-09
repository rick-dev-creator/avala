using System.Collections.ObjectModel;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Machine;
using Avala.Workbench.Presenting;
using Avala.Workbench.RepositoryRules;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Settings;

[INotifyPropertyChanged]
internal sealed partial class RepositorySettingsViewModel(RulesReader reader, SettingsFiles files, JobBoard board)
{
    public ObservableCollection<string> Repositories { get; } = [];

    public ObservableCollection<RuleFileViewModel> Files { get; } = [];

    public ObservableCollection<RuleViewModel> Rules { get; } = [];

    public ObservableCollection<CapsViewModel> Caps { get; } = [];

    public ObservableCollection<CheckViewModel> Checks { get; } = [];

    public ObservableCollection<JobSectionViewModel> JobSections { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReadCommand))]
    public partial string Repository { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Shown { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Autonomy { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string FormStrategy { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        Repositories.ShowOnly(board.Jobs.Values.OrderByDescending(job => job.Summary.Submitted).Select(job => job.Summary.Repository).Distinct());

        if (string.IsNullOrWhiteSpace(Repository) && Repositories.Count > 0)
        {
            Repository = Repositories[0];
        }

        if (CanRead())
        {
            await ReadAsync(cancellationToken);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRead))]
    private async Task ReadAsync(CancellationToken cancellationToken) => Show(await reader.ReadAsync(Repository.Trim(), cancellationToken));

    [RelayCommand]
    private async Task EditAsync(RuleFileViewModel file, CancellationToken cancellationToken) =>
        Error = (await files.OpenInRepositoryAsync(Shown, file.Path, cancellationToken)).Match(_ => string.Empty, SettingsPhrases.Opening);

    private bool CanRead() => !string.IsNullOrWhiteSpace(Repository);

    private void Show(RulesOfRepository rules)
    {
        Shown = rules.Repository;
        Error = string.Empty;
        Autonomy = rules.Policy.Autonomy.ToString();
        FormStrategy = SettingsPhrases.Strategy(rules.Policy.Strategy);
        RuleFileViewModel[] declared =
        [
            new(RuleFiles.Permissions, SettingsPhrases.Status(rules.Policy.File, rules.Policy.Error), rules.Policy.Origin),
            new(RuleFiles.Budget, SettingsPhrases.Status(rules.Budget.File, rules.Budget.Error), rules.Budget.Origin),
            new(RuleFiles.Checks, rules.Checks.File.ToString(), rules.Checks.Origin),
            new(RuleFiles.Jobs, rules.Jobs.File.ToString(), rules.Jobs.Origin),
        ];
        CapsViewModel[] caps =
        [
            new("Every connection", Amounts.Caps(rules.Budget.Caps)),
            .. rules.Budget.Connections.Select(connection => new CapsViewModel(connection.Connection.Value, Amounts.Caps(connection.Caps))),
        ];
        Files.ShowOnly(declared);
        Rules.ShowOnly(rules.Policy.Rules.Select(rule => new RuleViewModel(rule)));
        Caps.ShowOnly(caps);
        Checks.ShowOnly(rules.Checks.Checks.Select(check => new CheckViewModel(check)));
        JobSections.ShowOnly(rules.Jobs.Sections.Select(section => new JobSectionViewModel(section.Name, section.Value)));
    }
}
