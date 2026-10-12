using Avala.Components.Privacy;
using Avala.Sdk;
using Avala.Workbench.Machine;
using Avala.Workbench.Telemetry;
using Avala.Workbench.Updates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Settings;

internal interface IAboutViewModel
{
    string Version { get; }

    string Commit { get; }

    Personal LogFolder { get; }

    PersonalPhrase Note { get; }

    IUpdateViewModel Update { get; }

    ITelemetryViewModel Telemetry { get; }

    IAsyncRelayCommand OpenLogFolderCommand { get; }

    IAsyncRelayCommand OpenRepositoryCommand { get; }

    IAsyncRelayCommand OpenLicenseCommand { get; }

    void Refresh();
}

[INotifyPropertyChanged]
internal sealed partial class AboutViewModel(IUpdateViewModel update, ITelemetryViewModel telemetry, SettingsFiles files, ILinkOpener links) : IAboutViewModel
{
    public string Version => update.Version;

    public string Commit => update.Commit;

    public IUpdateViewModel Update => update;

    public ITelemetryViewModel Telemetry => telemetry;

    public Personal LogFolder => Personal.Path(files.Logs);

    public void Refresh() => update.Refresh();

    [ObservableProperty]
    public partial PersonalPhrase Note { get; private set; } = PersonalPhrase.Empty;

    [RelayCommand]
    private async Task OpenLogFolderAsync(CancellationToken cancellationToken) =>
        Note = (await files.OpenLogsAsync(cancellationToken)).Match(_ => PersonalPhrase.Empty, error => AboutPhrases.Folder(error, files.Logs));

    [RelayCommand]
    private Task OpenRepositoryAsync(CancellationToken cancellationToken) => OpenAsync(AvalaBuild.Repository, cancellationToken);

    [RelayCommand]
    private Task OpenLicenseAsync(CancellationToken cancellationToken) => OpenAsync(AvalaBuild.License, cancellationToken);

    private async Task OpenAsync(Uri link, CancellationToken cancellationToken) =>
        Note = (await links.OpenAsync(link, cancellationToken)).Match(_ => PersonalPhrase.Empty, _ => new PersonalPhrase(AboutPhrases.Link(link)));
}

internal static class AboutPhrases
{
    public static PersonalPhrase Folder(FileOpenError error, string folder) => error switch
    {
        FileOpenError.Uncreatable => new("The log folder could not be created at ", Personal.Path(folder), "."),
        FileOpenError.Refused => new("The system refused to open the log folder. It is ", Personal.Path(folder), "."),
        _ => new("Nothing on this computer opens folders from Avala. The log folder is ", Personal.Path(folder), "."),
    };

    public static string Link(Uri link) => $"The browser could not be opened. The address is {link}.";
}
