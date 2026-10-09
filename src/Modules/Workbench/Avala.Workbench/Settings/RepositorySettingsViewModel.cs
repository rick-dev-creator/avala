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

    string Notice { get; }

    IAsyncRelayCommand ReadCommand { get; }

    IAsyncRelayCommand<IRuleFileViewModel> EditCommand { get; }

    IAsyncRelayCommand<IRuleFileViewModel> EditHereCommand { get; }

    IRuleFileEditorViewModel Editor { get; }

    IAsyncRelayCommand<string> OpenCommand { get; }

    string Name { get; }

    string AutonomyNote { get; }

    IRuleFileViewModel? PermissionsFile { get; }

    IRuleFileViewModel? BudgetFile { get; }

    Task LoadAsync(CancellationToken cancellationToken);
}

[INotifyPropertyChanged]
internal sealed partial class RepositorySettingsViewModel : IRepositorySettingsViewModel
{
    private readonly RulesReader reader;
    private readonly SettingsFiles files;
    private readonly JobBoard board;
    private readonly RuleFileEditorViewModel editor;

    public RepositorySettingsViewModel(RulesReader reader, SettingsFiles files, JobBoard board, RuleFileEditorViewModel editor)
    {
        this.reader = reader;
        this.files = files;
        this.board = board;
        this.editor = editor;
        editor.Saved += (_, path) => Refreshing = RefreshAsync(path);
    }

    public IRuleFileEditorViewModel Editor => editor;

    public Task Refreshing { get; private set; } = Task.CompletedTask;

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
    [NotifyCanExecuteChangedFor(nameof(EditCommand), nameof(EditHereCommand))]
    public partial string Shown { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Autonomy { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string FormStrategy { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Notice { get; private set; } = string.Empty;

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
    private async Task EditAsync(IRuleFileViewModel? file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return;
        }

        var opened = await files.OpenInRepositoryAsync(Shown, file.Path, cancellationToken);
        Error = opened.Match(_ => string.Empty, error => SettingsPhrases.Opening(error, Path.Combine(Shown, file.Path)));
        Notice = opened.Match(done => done.Created ? SettingsPhrases.CreatedInRepository(file.Path) : string.Empty, _ => string.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanEditHere))]
    private async Task EditHereAsync(IRuleFileViewModel? file, CancellationToken cancellationToken)
    {
        if (file is not null)
        {
            Notice = string.Empty;
            await editor.OpenAsync(Shown, file.Path, cancellationToken);
        }
    }

    private async Task RefreshAsync(string saved)
    {
        await ReadCommand.ExecuteAsync(null);
        Notice = RuleFilePhrases.Saved(saved);
    }

    private bool CanEditHere(IRuleFileViewModel? file) => file is { CanEditHere: true } && Shown.Length > 0;

    private bool CanRead() => !string.IsNullOrWhiteSpace(Repository);

    private bool CanEdit(IRuleFileViewModel? file) => file is not null && Shown.Length > 0;

    private void Show(RulesOfRepository read)
    {
        Shown = read.Repository;
        Name = SettingsPhrases.Name(read.Repository);
        AutonomyNote = SettingsPhrases.Autonomy(read.Policy.Autonomy.ToString());
        Error = string.Empty;
        Notice = string.Empty;
        Autonomy = read.Policy.Autonomy.ToString();
        FormStrategy = SettingsPhrases.Strategy(read.Policy.Strategy);
        RuleFileViewModel[] declared =
        [
            new(RuleFiles.Permissions, SettingsPhrases.Status(read.Policy.File, read.Policy.Error), read.Policy.Origin, $"{Autonomy}, {FormStrategy.ToLowerInvariant()}, {Overview.OverviewPhrases.Count(read.Policy.Rules.Count, "rule")}") { CanEditHere = editor.CanEdit(RuleFiles.Permissions) },
            new(RuleFiles.Budget, SettingsPhrases.Status(read.Budget.File, read.Budget.Error), read.Budget.Origin, Overview.OverviewPhrases.Count(read.Budget.Connections.Count + 1, "scope")) { CanEditHere = editor.CanEdit(RuleFiles.Budget) },
            new(RuleFiles.Checks, read.Checks.File.ToString(), read.Checks.Origin, read.Checks.Checks.Count == 0 ? "no checks" : string.Join(", ", read.Checks.Checks.Select(check => check.Name))) { CanEditHere = editor.CanEdit(RuleFiles.Checks) },
            new(RuleFiles.Jobs, read.Jobs.File.ToString(), read.Jobs.Origin, Overview.OverviewPhrases.Count(read.Jobs.Sections.Count, "section")) { CanEditHere = editor.CanEdit(RuleFiles.Jobs) },
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
