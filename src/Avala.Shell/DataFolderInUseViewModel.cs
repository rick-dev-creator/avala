using CommunityToolkit.Mvvm.Input;

namespace Avala.Shell;

internal interface IDataFolderInUseViewModel
{
    string Folder { get; }

    string Reason { get; }

    IRelayCommand QuitCommand { get; }
}

internal sealed partial class DataFolderInUseViewModel(string folder) : IDataFolderInUseViewModel
{
    public event EventHandler? QuitRequested;

    public string Folder { get; } = folder;

    public string Reason => DataFolderInUsePhrases.Reason;

    [RelayCommand]
    private void Quit() => QuitRequested?.Invoke(this, EventArgs.Empty);
}

internal sealed class DesignDataFolderInUseViewModel : IDataFolderInUseViewModel
{
    public string Folder => "/home/dana/.local/share/Avala";

    public string Reason => DataFolderInUsePhrases.Reason;

    public IRelayCommand QuitCommand { get; } = new RelayCommand(() => { });
}

internal static class DataFolderInUsePhrases
{
    public const string Reason =
        "Another Avala already runs on this data folder. Two of them would write the same databases and worktrees, so this one stops before opening anything. "
        + "Switch to the Avala that is open, or set AVALA_DATA_PATH to another folder to run a second one.";
}
