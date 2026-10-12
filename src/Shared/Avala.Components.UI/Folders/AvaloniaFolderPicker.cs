using Avala.Sdk;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace Avala.Components.UI.Folders;

public sealed class AvaloniaFolderPicker : IFolderPicker
{
    public async ValueTask<Option<string>> PickAsync(string title, string near, CancellationToken cancellationToken)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window })
        {
            return Option<string>.None;
        }

        return await await Dispatcher.UIThread.InvokeAsync(
            () => PickInAsync(window.StorageProvider, title, near),
            DispatcherPriority.Normal,
            cancellationToken);
    }

    private static async Task<Option<string>> PickInAsync(IStorageProvider storage, string title, string near)
    {
        if (!storage.CanPickFolder)
        {
            return Option<string>.None;
        }

        var start = await storage.TryGetFolderFromPathAsync(StartingFolders.Near(near, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        var picked = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false, SuggestedStartLocation = start });

        return picked.Count > 0 ? picked[0].TryGetLocalPath().ToOption() : Option<string>.None;
    }
}
