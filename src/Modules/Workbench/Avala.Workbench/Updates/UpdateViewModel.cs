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

    IAsyncRelayCommand CheckCommand { get; }

    IAsyncRelayCommand OpenReleaseCommand { get; }

    void Refresh();
}

[INotifyPropertyChanged]
internal sealed partial class UpdateViewModel(AvalaBuild build, IUpdates updates, ILinkOpener links) : IUpdateViewModel
{
    private const int ShortCommit = 12;

    public string Version => build.Version;

    public string Commit => build.Commit.Match(commit => commit.Length > ShortCommit ? commit[..ShortCommit] : commit, () => AvalaBuild.Unknown);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAvailable))]
    [NotifyCanExecuteChangedFor(nameof(OpenReleaseCommand))]
    public partial UpdateState State { get; private set; } = updates.Latest;

    [ObservableProperty]
    public partial string Status { get; private set; } = UpdatePhrases.Status(updates.Latest);

    public bool IsAvailable => State.Update.IsSome;

    public void Refresh() => Show(updates.Latest);

    [RelayCommand]
    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        Show(State with { Status = UpdateStatus.Checking });
        Show(await updates.CheckAsync(cancellationToken));
    }

    [RelayCommand(CanExecute = nameof(IsAvailable))]
    private async Task OpenReleaseAsync(CancellationToken cancellationToken)
    {
        if (State.Update.Match<AvailableUpdate?>(update => update, () => null) is { } update)
        {
            Status = (await links.OpenAsync(update.Release, cancellationToken)).Match(
                _ => UpdatePhrases.Status(State),
                _ => UpdatePhrases.Unopened(update.Release));
        }
    }

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
        UpdateStatus.UpToDate => "Avala is up to date.",
        UpdateStatus.Checking => "Checking for updates…",
        UpdateStatus.Unreachable => "GitHub could not be reached to check for updates.",
        UpdateStatus.Off => "Checking at startup is off on this machine.",
        _ => "Not checked yet.",
    };

    public static string Available(string version) => $"Version {version} is available";

    public static string Unopened(Uri release) => $"The browser could not be opened. The release is at {release}.";
}
