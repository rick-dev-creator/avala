using Avala.Components.UI.Copying;
using Avala.Testing.UI;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

namespace Avala.Components.UI.Tests;

public sealed class ClipboardScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task ALinkIsCopiedAsItsAddressAsync() =>
        ui.RunAsync(async () =>
        {
            var view = ViewScript.Show(new Border(), new object());

            var copied = await new AvaloniaClipboard(() => view.Window).CopyLinkAsync(new Uri("https://example.com/pull/7"), Cancellation);

            Assert.True(copied);
            Assert.Equal("https://example.com/pull/7", await view.Window.Clipboard!.TryGetTextAsync());
        }, Cancellation);

    [Fact]
    public Task AnImageIsCopiedAsItsPictureAndItsFileAsync() =>
        ui.RunAsync(async () =>
        {
            var view = ViewScript.Show(new Border(), new object());
            var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png");
            await File.WriteAllBytesAsync(path, Pngs.Of(3, 2), Cancellation);

            try
            {
                var clipboard = view.Window.Clipboard!;
                var copied = await new AvaloniaClipboard(() => view.Window).CopyImageAsync(path, Cancellation);
                using var picture = await clipboard.TryGetBitmapAsync();
                var file = await clipboard.TryGetFileAsync();

                Assert.True(copied);
                Assert.NotNull(picture);
                Assert.Equal(path, file?.TryGetLocalPath());
            }
            finally
            {
                File.Delete(path);
            }
        }, Cancellation);

    [Fact]
    public Task FilesAreCopiedTogetherAsync() =>
        ui.RunAsync(async () =>
        {
            var view = ViewScript.Show(new Border(), new object());
            var folder = Directory.CreateTempSubdirectory().FullName;
            string[] paths = [Path.Combine(folder, "notes.md"), Path.Combine(folder, "plan.md")];

            try
            {
                foreach (var path in paths)
                {
                    await File.WriteAllTextAsync(path, "# Notes", Cancellation);
                }

                var clipboard = view.Window.Clipboard!;
                var copied = await new AvaloniaClipboard(() => view.Window).CopyFilesAsync(paths, Cancellation);
                var files = await clipboard.TryGetFilesAsync() ?? [];

                Assert.True(copied);
                Assert.Equal(paths, files.Select(file => file.TryGetLocalPath()));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }, Cancellation);

    [Fact]
    public Task AMissingImageIsNotCopiedAsync() =>
        ui.RunAsync(async () =>
        {
            var view = ViewScript.Show(new Border(), new object());

            Assert.False(await new AvaloniaClipboard(() => view.Window).CopyImageAsync(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.png"), Cancellation));
        }, Cancellation);
}
