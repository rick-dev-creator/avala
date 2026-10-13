using Avala.Sdk;
using Avala.Sdk.Updates;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Updates;

internal interface IUpdateViewModel
{
    string Version { get; }

    string Commit { get; }

    string Status { get; }

    bool IsAvailable { get; }

    string Action { get; }

    IAsyncRelayCommand CheckCommand { get; }

    IAsyncRelayCommand UpdateCommand { get; }

    void Refresh();
}

[INotifyPropertyChanged]
internal sealed partial class UpdateViewModel(AvalaBuild build, IUpdates updates, ILinkOpener links) : IUpdateViewModel
{
    private const int ShortCommit = 12;

    public string Version => build.Version;

    public string Commit => build.Commit.Match(commit => commit.Length > ShortCommit ? commit[..ShortCommit] : commit, () => AvalaBuild.Unknown);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAvailable), nameof(Action))]
    [NotifyCanExecuteChangedFor(nameof(UpdateCommand))]
    public partial UpdateState State { get; private set; } = updates.Latest;

    [ObservableProperty]
    public partial string Status { get; private set; } = UpdatePhrases.Status(updates.Latest);

    public bool IsAvailable => State.Update.IsSome;

    public string Action => UpdatePhrases.Action(State);

    public void Refresh() => Show(updates.Latest);

    [RelayCommand]
    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        Show(State with { Status = UpdateStatus.Checking });
        Show(await updates.CheckAsync(cancellationToken));
    }

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task UpdateAsync(CancellationToken cancellationToken)
    {
        if (State.CanRestart)
        {
            Show(await updates.RestartAsync(cancellationToken));
        }
        else if (State.CanDownload)
        {
            Show(State with { Status = UpdateStatus.Downloading });
            Show(await updates.DownloadAsync(cancellationToken));
        }
        else if (State.Update.Match<AvailableUpdate?>(update => update, () => null) is { } update)
        {
            Status = (await links.OpenAsync(update.Release, cancellationToken)).Match(
                _ => UpdatePhrases.Status(State),
                _ => UpdatePhrases.Unopened(update.Release));
        }
    }

    private bool CanUpdate() => IsAvailable && State.Status != UpdateStatus.Downloading;

    private void Show(UpdateState state)
    {
        State = state;
        Status = UpdatePhrases.Status(state);
    }
}

internal static class UpdatePhrases
{
    public static string Status(UpdateState state) => state.Status switch
    {
        UpdateStatus.Available => state.Update.Match(update => $"{Available(update.Version)}.", () => "A newer version is available."),
        UpdateStatus.Downloading => state.Update.Match(update => $"{Downloading(update.Version)}.", () => "Downloading the update."),
        UpdateStatus.Downloaded => state.Update.Match(update => $"Version {update.Version} is downloaded. Restart Avala to update.", () => "Restart Avala to update."),
        UpdateStatus.DownloadFailed => state.Update.Match(update => $"Version {update.Version} could not be downloaded. Try again.", () => "The update could not be downloaded."),
        UpdateStatus.UpToDate => "Avala is up to date.",
        UpdateStatus.Checking => "Checking for updates…",
        UpdateStatus.Unreachable => "GitHub could not be reached to check for updates.",
        UpdateStatus.Off => "Checking at startup is off on this machine.",
        _ => "Not checked yet.",
    };

    public static string Notice(UpdateState state) => state.Update.Match(
        update => state.Status switch
        {
            UpdateStatus.Downloading => $"{Downloading(update.Version)}…",
            UpdateStatus.Downloaded => RestartToUpdate,
            _ => Available(update.Version),
        },
        () => string.Empty);

    public static string Hint(UpdateState state) =>
        state.CanRestart ? "Restart Avala and update"
        : state.Status == UpdateStatus.Downloading ? "Downloading the update"
        : state.CanDownload ? "Download the update"
        : "Open the release page";

    public static string Action(UpdateState state) => state.CanRestart ? RestartToUpdate : "Download";

    public const string RestartToUpdate = "Restart to update";

    public const string DownloadFailed = "The download failed. Click to try again.";

    public static string Available(string version) => $"Version {version} is available";

    public static string Unopened(Uri release) => $"The browser could not be opened. The release is at {release}.";

    private static string Downloading(string version) => $"Downloading version {version}";
}
