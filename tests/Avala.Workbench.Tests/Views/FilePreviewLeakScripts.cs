using Avala.Components.UI.Pictures;
using Avala.Testing.UI;
using Avala.Workbench.Conversation;
using Avalonia.Controls;

namespace Avala.Workbench.Tests.Views;

[Collection(Leaks.Collection)]
public sealed class FilePreviewLeakScripts(HeadlessUi ui)
{
    [Fact]
    public Task ShowingAndClosingImagePreviewsThirtyTimesReturnsMemoryToItsBaselineAsync() =>
        ui.RunAsync(async () =>
        {
            var pictures = new PictureCache();
            var previews = new List<FilePreviewViewModel>();

            for (var shade = 0; shade < 10; shade++)
            {
                previews.Add(await LoadedAsync(pictures, $"orbit-{shade}.png", Pngs.Of(1600, 40, (byte)shade)));
            }

            var view = Screen.Show(new TextBlock());
            var held = 0L;

            var retention = await Leaks.ManagedAcrossAsync(async () =>
            {
                var shown = new StackPanel();
                shown.Children.AddRange(previews.Select(preview => new ContentControl { Content = preview }));
                view.Window.Content = shown;
                view.Settle();
                await Task.WhenAll(previews.Select(preview => preview.RealizeCommand.ExecutionTask ?? Task.CompletedTask));
                await previews[0].OpenCommand.ExecuteAsync(null);
                view.Settle();
                await (previews[0].FitCommand.ExecutionTask ?? Task.CompletedTask);
                await previews[0].CloseCommand.ExecuteAsync(null);
                view.Window.Content = null;
                view.Settle();
                held = held == 0 ? pictures.LiveBytes : held;
            });

            Assert.InRange(held, 1, pictures.Budget);
            Assert.Equal((0L, held), (pictures.LeasedBytes, pictures.LiveBytes));
            Assert.True(retention.IsBounded, retention.ToString());
        }, TestContext.Current.CancellationToken);

    private static async Task<FilePreviewViewModel> LoadedAsync(PictureCache pictures, string path, byte[] bytes)
    {
        var worktree = new Worktree();
        worktree.Files.Stored[Path.Combine(Worktree.Folder, path)] = bytes;
        var worked = worktree.Worked();
        var preview = new FilePreviewViewModel(worktree.Job, path, new FileViewing(worked, new Linking.Links(worktree.Opener, new CommunityToolkit.Mvvm.Messaging.StrongReferenceMessenger()), pictures));
        await preview.LoadCommand.ExecuteAsync(null);

        return preview;
    }
}
