using Avala.Components.UI.Pictures;
using Avala.Testing.UI;
using Avala.Workbench.Conversation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class FilePreviewViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AnImagesThumbnailOpensItInASheetThatEscapeClosesAsync() =>
        ui.RunAsync(async () =>
        {
            var preview = await LoadedAsync("orbit.png", Pngs.Of(4, 4));
            var view = Screen.Show(preview);
            await (preview.RealizeCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();
            var thumbnail = (view.Shows("Thumbnail"), view.Find<Image>("Picture").Source is not null, view.Find<Popup>("Sheet").IsOpen);

            view.Click("Thumbnail");
            await (preview.OpenCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();
            await (preview.FitCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();
            var opened = (view.Find<Popup>("Sheet").IsOpen, view.Find<Image>("SheetPicture").Source is not null);
            view.Find<Border>("SheetCard").Focus();
            view.Press(Key.Escape);
            view.Settle();

            Assert.Equal((true, true, false), thumbnail);
            Assert.Equal((true, true), opened);
            Assert.False(view.Find<Popup>("Sheet").IsOpen);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AFileThatCouldNotBeReadSaysSoWithoutAThumbnailAsync() =>
        ui.RunAsync(async () =>
        {
            var preview = await LoadedAsync("orbit.png", []);

            var view = Screen.Show(preview);

            Assert.Equal((false, true), (view.Shows("Thumbnail"), view.Shows("Notice")));
            Assert.Equal("orbit.png is no longer in the workspace or the repository.", view.TextOf("Notice"));
        }, TestContext.Current.CancellationToken);

    private static async Task<FilePreviewViewModel> LoadedAsync(string path, byte[] bytes)
    {
        var worktree = new Worktree();

        if (bytes.Length > 0)
        {
            worktree.Files.Stored[Path.Combine(Worktree.Folder, path)] = bytes;
        }

        var worked = worktree.Worked();
        var preview = new FilePreviewViewModel(worktree.Job, path, new FileViewing(worked, new Linking.Links(worktree.Opener, new CommunityToolkit.Mvvm.Messaging.StrongReferenceMessenger()), new PictureCache()));
        await preview.LoadCommand.ExecuteAsync(null);

        return preview;
    }
}
