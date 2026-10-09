using System.Collections.ObjectModel;
using System.Globalization;
using Avala.Agents.Contracts.Connections;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Supervision.Contracts;
using Avala.Workbench.Machine;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Settings;

internal interface IMachineSettingsViewModel
{
    IReadOnlyList<IMachineConnectionViewModel> Connections { get; }

    string ConnectionsFile { get; }

    string Silence { get; }

    string SupervisionFile { get; }

    string SilenceDraft { get; set; }

    string Resources { get; }

    string Error { get; }

    IAsyncRelayCommand SaveSilenceCommand { get; }

    IAsyncRelayCommand OpenConnectionsCommand { get; }

    Task LoadAsync(CancellationToken cancellationToken);
}

[INotifyPropertyChanged]
internal sealed partial class MachineSettingsViewModel(MachineSettings settings, SettingsFiles files) : IMachineSettingsViewModel
{
    private readonly ObservableCollection<MachineConnectionViewModel> connections = [];

    public IReadOnlyList<IMachineConnectionViewModel> Connections => connections;

    [ObservableProperty]
    public partial string ConnectionsFile { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Silence { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string SupervisionFile { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSilenceCommand))]
    public partial string SilenceDraft { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Resources { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    public async Task LoadAsync(CancellationToken cancellationToken) => Show(await settings.ReadAsync(cancellationToken));

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
    private async Task OpenConnectionsAsync(CancellationToken cancellationToken) =>
        Error = (await files.OpenInDataFolderAsync(SettingsFiles.Connections, cancellationToken)).Match(_ => string.Empty, SettingsPhrases.Opening);

    private bool CanSaveSilence() => !string.IsNullOrWhiteSpace(SilenceDraft);

    private void Show(MachineState state)
    {
        ConnectionsFile = SettingsPhrases.Status(state.Connections.File, state.Connections.Error);
        connections.ShowOnly(state.Connections.Connections.Select(connection =>
            new MachineConnectionViewModel(connection, state.Connections.Default == Option<ConnectionName>.Some(connection.Name))));
        ShowSupervision(state.Supervision);
        Resources = Describe(state.Resources);
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
