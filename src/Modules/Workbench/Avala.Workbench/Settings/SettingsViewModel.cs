using System.ComponentModel;
using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Settings;

internal interface ISettingsViewModel
{
    string Title { get; }

    IRepositorySettingsViewModel Repository { get; }

    IMachineSettingsViewModel Machine { get; }

    IAppearanceViewModel Appearance { get; }

    IAboutViewModel About { get; }

    IReadOnlyList<ISettingsEntryViewModel> Entries { get; }

    SettingsSection Section { get; }

    SectionHeading Heading { get; }

    string ShownRepository { get; }

    bool ShowsAccounts { get; }

    bool ShowsConnections { get; }

    bool ShowsSupervision { get; }

    bool ShowsWorktrees { get; }

    bool ShowsNotifications { get; }

    bool ShowsAppearance { get; }

    bool ShowsAbout { get; }

    bool ShowsRepository { get; }

    IRelayCommand<SettingsSection> ShowCommand { get; }

    IAsyncRelayCommand<string> OpenRepositoryCommand { get; }

    IAsyncRelayCommand ReadRepositoryCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class SettingsViewModel : ISettingsViewModel, IPage, IActivatable
{
    private readonly IReadOnlyList<SettingsEntryViewModel> entries = Selected(SectionPhrases.Entries(), SettingsSection.Accounts);

    public SettingsViewModel(IRepositorySettingsViewModel repository, IMachineSettingsViewModel machine, IAppearanceViewModel appearance, IAboutViewModel about)
    {
        Repository = repository;
        Machine = machine;
        Appearance = appearance;
        About = about;

        if (repository is INotifyPropertyChanged observed)
        {
            observed.PropertyChanged += (_, changed) =>
            {
                if (changed.PropertyName == nameof(IRepositorySettingsViewModel.Shown))
                {
                    OnPropertyChanged(nameof(ShownRepository));
                }
            };
        }

        repository.ReadCommand.CanExecuteChanged += (_, _) => ReadRepositoryCommand.NotifyCanExecuteChanged();
    }

    public string Title => "Settings";

    public string Icon => "IconSettings";

    public IRepositorySettingsViewModel Repository { get; }

    public IMachineSettingsViewModel Machine { get; }

    public IAppearanceViewModel Appearance { get; }

    public IAboutViewModel About { get; }

    public IReadOnlyList<ISettingsEntryViewModel> Entries => entries;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(Heading),
        nameof(ShownRepository),
        nameof(ShowsAccounts),
        nameof(ShowsConnections),
        nameof(ShowsSupervision),
        nameof(ShowsWorktrees),
        nameof(ShowsNotifications),
        nameof(ShowsAppearance),
        nameof(ShowsAbout),
        nameof(ShowsRepository))]
    public partial SettingsSection Section { get; private set; } = SettingsSection.Accounts;

    public SectionHeading Heading => SectionPhrases.Heading(Section);

    public string ShownRepository => ShowsRepository ? Repository.Shown : string.Empty;

    public bool ShowsAccounts => Section == SettingsSection.Accounts;

    public bool ShowsConnections => Section == SettingsSection.Connections;

    public bool ShowsSupervision => Section == SettingsSection.Supervision;

    public bool ShowsWorktrees => Section == SettingsSection.Worktrees;

    public bool ShowsNotifications => Section == SettingsSection.Notifications;

    public bool ShowsAppearance => Section == SettingsSection.Appearance;

    public bool ShowsAbout => Section == SettingsSection.About;

    public bool ShowsRepository => Section == SettingsSection.Repository;

    public Task Loading { get; private set; } = Task.CompletedTask;

    public void Activate() => Loading = LoadAsync(CancellationToken.None);

    public void Deactivate()
    {
    }

    [RelayCommand]
    public void Show(SettingsSection section)
    {
        Section = section;

        foreach (var entry in entries)
        {
            entry.IsSelected = entry.Section == section;
        }
    }

    [RelayCommand]
    public async Task OpenRepositoryAsync(string repository, CancellationToken cancellationToken)
    {
        Show(SettingsSection.Repository);
        await Repository.OpenCommand.ExecuteAsync(repository);
    }

    [RelayCommand(CanExecute = nameof(CanReadRepository))]
    private async Task ReadRepositoryAsync(CancellationToken cancellationToken)
    {
        Show(SettingsSection.Repository);
        await Repository.ReadCommand.ExecuteAsync(null);
    }

    private bool CanReadRepository() => Repository.ReadCommand.CanExecute(null);

    private static IReadOnlyList<SettingsEntryViewModel> Selected(IReadOnlyList<SettingsEntryViewModel> listed, SettingsSection section)
    {
        foreach (var entry in listed)
        {
            entry.IsSelected = entry.Section == section;
        }

        return listed;
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        About.Refresh();
        await Machine.LoadAsync(cancellationToken);
        await Appearance.LoadAsync(cancellationToken);
        await Repository.LoadAsync(cancellationToken);
        await About.Telemetry.LoadAsync(cancellationToken);
    }
}
