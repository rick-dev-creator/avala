using System.Collections.ObjectModel;
using System.Globalization;
using Avala.Agents.Contracts.Connections;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Workbench.Accounts;
using Avala.Workbench.Machine;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Settings;

internal interface IMachineSettingsViewModel
{
    IDefaultConnectionViewModel DefaultConnection { get; }

    INotificationsViewModel Notifications { get; }

    IMachineAccountsViewModel Accounts { get; }

    string Silence { get; }

    string SupervisionFile { get; }

    string SilenceDraft { get; set; }

    bool IsSilenceChanged { get; }

    double SilenceMinutes { get; set; }

    string Resources { get; }

    IAsyncRelayCommand SaveSilenceCommand { get; }

    Task LoadAsync(CancellationToken cancellationToken);
}

internal sealed record MachineSections(IDefaultConnectionViewModel DefaultConnection, INotificationsViewModel Notifications, ISourceControlViewModel SourceControl);

internal static class SilenceDial
{
    public const double Fewest = 1;

    public const double Most = 60;

    public static double Minutes(string draft, string saved) =>
        Seconds(draft) is { } seconds ? Math.Clamp(seconds / 60, Fewest, Most)
        : Seconds(saved.TrimEnd('s')) is { } kept ? Math.Clamp(kept / 60, Fewest, Most)
        : Fewest;

    public static string Draft(string draft, string saved, double minutes) =>
        !double.IsFinite(minutes) || Math.Abs(minutes - Minutes(draft, saved)) < 1e-6
            ? draft
            : (Math.Round(Math.Clamp(minutes, Fewest, Most)) * 60).ToString("0", CultureInfo.InvariantCulture);

    private static double? Seconds(string text) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && double.IsFinite(seconds) && seconds > 0 ? seconds : null;
}

[INotifyPropertyChanged]
internal sealed partial class MachineSettingsViewModel : IMachineSettingsViewModel
{
    private readonly MachineSettings settings;
    private readonly MachineAccountsViewModel accounts;

    public MachineSettingsViewModel(MachineSettings settings, MachineSections sections, ConnectionEditorViewModel editor)
    {
        this.settings = settings;
        DefaultConnection = sections.DefaultConnection;
        Notifications = sections.Notifications;
        accounts = new MachineAccountsViewModel(editor, sections.SourceControl, OpenConnectionsCommand);
        DefaultConnection.Changed += (_, catalog) => ShowConnections(catalog);
        editor.Edited += (_, edited) =>
        {
            ShowConnections(edited.Catalog);
            DefaultConnection.Show(edited.Catalog);
            Notice = edited.Notice;
        };
    }

    public IDefaultConnectionViewModel DefaultConnection { get; }

    public INotificationsViewModel Notifications { get; }

    public IMachineAccountsViewModel Accounts => accounts;

    public IConnectionEditorViewModel Editor => accounts.Editing;

    public IReadOnlyList<IMachineConnectionViewModel> Connections => accounts.Connections;

    public string ConnectionsFile
    {
        get => accounts.ConnectionsFile;
        private set => accounts.ConnectionsFile = value;
    }

    public ISourceControlViewModel SourceControl => accounts.SourceControl;

    public string ForgesFile
    {
        get => accounts.ForgesFile;
        private set => accounts.ForgesFile = value;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSilenceChanged), nameof(SilenceMinutes))]
    public partial string Silence { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string SupervisionFile { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSilenceCommand))]
    [NotifyPropertyChangedFor(nameof(IsSilenceChanged), nameof(SilenceMinutes))]
    public partial string SilenceDraft { get; set; } = string.Empty;

    public double SilenceMinutes
    {
        get => SilenceDial.Minutes(SilenceDraft, Silence);
        set => SilenceDraft = SilenceDial.Draft(SilenceDraft, Silence, value);
    }

    public bool IsSilenceChanged => Silence.Length > 0 && SilenceDraft.Trim() + "s" != Silence;

    [ObservableProperty]
    public partial string Resources { get; private set; } = string.Empty;

    public string Error
    {
        get => accounts.Error;
        private set => accounts.Error = value;
    }

    public string Notice
    {
        get => accounts.Notice;
        private set => accounts.Notice = value;
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        Show(await settings.ReadAsync(cancellationToken));
        await Notifications.LoadAsync(cancellationToken);
        await SourceControl.LoadAsync(cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanSaveSilence))]
    private async Task SaveSilenceAsync(CancellationToken cancellationToken)
    {
        if (!double.TryParse(SilenceDraft.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || !double.IsFinite(seconds))
        {
            Error = SettingsPhrases.NotSeconds;
            return;
        }

        var changed = await settings.ChangeSilenceAsync(TimeSpan.FromSeconds(Math.Clamp(seconds, -1, 1e9)), cancellationToken);
        Error = changed.Match(_ => string.Empty, SettingsPhrases.Silence);

        if (changed.TryGetValue(out var applied, out _))
        {
            ShowSupervision(applied);
        }
    }

    [RelayCommand]
    private async Task OpenConnectionsAsync(CancellationToken cancellationToken)
    {
        var opening = await accounts.Editing.OpenFileAsync(cancellationToken);
        (Error, Notice) = (opening.Error, opening.Notice);
        Show(await settings.ReadAsync(cancellationToken));
    }

    private bool CanSaveSilence() => !string.IsNullOrWhiteSpace(SilenceDraft);

    private void Show(MachineState state)
    {
        ShowConnections(state.Connections);
        DefaultConnection.Show(state.Connections);
        ShowSupervision(state.Supervision);
        Resources = Describe(state.Resources);
        ForgesFile = state.Forges.Match(ForgeSettingsPhrases.File, () => string.Empty);
    }

    private void ShowConnections(ConnectionCatalog catalog)
    {
        ConnectionsFile = SettingsPhrases.Status(catalog.File, catalog.Error);
        accounts.Editing.Show(catalog);
        accounts.Rows.ShowOnly(catalog.Connections.Select(connection =>
            new MachineConnectionViewModel(
                connection,
                catalog.DefaultMode == DefaultMode.Fixed && catalog.Default == Option<ConnectionName>.Some(connection.Name),
                new RelayCommand(() => accounts.Editing.Edit(connection)),
                new RelayCommand(() => accounts.Editing.AskToRemove(connection.Name)))));
    }

    private void ShowSupervision(SupervisionSettings supervision)
    {
        Silence = $"{SettingsPhrases.Seconds(supervision.Silence)}s";
        SilenceDraft = SettingsPhrases.Seconds(supervision.Silence);
        SupervisionFile = SettingsPhrases.Status(supervision.File, supervision.Error);
    }

    private static string Describe(ResourceSettings resources) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{SettingsPhrases.Status(resources.File, resources.Error)}: samples every {Amounts.Seconds(resources.Sampling)}s, orphans {resources.Orphans}, ports {resources.Ports.First}-{resources.Ports.Last} by {resources.Ports.PerWorktree}");
}
