using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Settings;

internal interface IAboutViewModel
{
    string Version { get; }

    string Commit { get; }

    string LogFolder { get; }

    string Note { get; }

    IAsyncRelayCommand OpenLogFolderCommand { get; }

    IAsyncRelayCommand OpenRepositoryCommand { get; }

    IAsyncRelayCommand OpenLicenseCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class AboutViewModel(AvalaBuild build, AvalaPaths paths, IFileOpener files, ILinkOpener links) : IAboutViewModel
{
    private const int ShortCommit = 12;

    public string Version => build.Version;

    public string Commit => build.Commit.Match(commit => commit.Length > ShortCommit ? commit[..ShortCommit] : commit, () => AvalaBuild.Unknown);

    public string LogFolder => paths.Logs;

    [ObservableProperty]
    public partial string Note { get; private set; } = string.Empty;

    [RelayCommand]
    private async Task OpenLogFolderAsync(CancellationToken cancellationToken) =>
        Note = (await files.OpenFolderAsync(paths.Logs, cancellationToken)).Match(_ => string.Empty, error => AboutPhrases.Folder(error, paths.Logs));

    [RelayCommand]
    private Task OpenRepositoryAsync(CancellationToken cancellationToken) => OpenAsync(AvalaBuild.Repository, cancellationToken);

    [RelayCommand]
    private Task OpenLicenseAsync(CancellationToken cancellationToken) => OpenAsync(AvalaBuild.License, cancellationToken);

    private async Task OpenAsync(Uri link, CancellationToken cancellationToken) =>
        Note = (await links.OpenAsync(link, cancellationToken)).Match(_ => string.Empty, _ => AboutPhrases.Link(link));
}

internal static class AboutPhrases
{
    public static string Folder(FileOpenError error, string folder) => error switch
    {
        FileOpenError.Uncreatable => $"The log folder could not be created at {folder}.",
        FileOpenError.Refused => $"The system refused to open the log folder. It is {folder}.",
        _ => $"Nothing on this computer opens folders from Avala. The log folder is {folder}.",
    };

    public static string Link(Uri link) => $"The browser could not be opened. The address is {link}.";
}
