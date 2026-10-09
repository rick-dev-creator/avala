using Avala.Agents.Contracts.Connections;
using Avala.Sdk;
using Avala.Workbench.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Navigation;

internal interface IFirstRunViewModel
{
    bool IsShown { get; }

    string Heading { get; }

    string Explanation { get; }

    IRelayCommand OpenSettingsCommand { get; }

    Task CheckAsync(CancellationToken cancellationToken);
}

[INotifyPropertyChanged]
internal sealed partial class FirstRunViewModel(IConnections connections, SettingsLink settings) : IFirstRunViewModel
{
    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    public partial string Heading { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Explanation { get; private set; } = string.Empty;

    [RelayCommand]
    private void OpenSettings() => settings.Open();

    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        var guide = FirstRunPhrases.Guide(await connections.CatalogAsync(cancellationToken));
        IsShown = guide.IsSome;
        Heading = guide.Match(found => found.Heading, () => string.Empty);
        Explanation = guide.Match(found => found.Explanation, () => string.Empty);
    }
}

internal sealed class SettingsLink(SettingsViewModel settings, JobFocus focus)
{
    public void Open() => focus.Show(settings);
}

internal static class FirstRunPhrases
{
    public static Option<(string Heading, string Explanation)> Guide(ConnectionCatalog catalog)
    {
        if (catalog.File == ConnectionFileStatus.Rejected)
        {
            return ("connections.json is rejected", "Avala cannot use the connections it declares, so no job can start on them. Settings shows why; fix the file there or open it.");
        }

        return catalog.Connections.Count == 0
            ? ("No connections yet", "A job runs on a harness, and Avala found none to run it on: no Claude Code login on this computer and no connection declared in connections.json.")
            : Option<(string, string)>.None;
    }
}
