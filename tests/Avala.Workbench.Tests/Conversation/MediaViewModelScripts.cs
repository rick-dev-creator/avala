using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Testing;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class MediaViewModelScripts
{
    [Fact]
    public async Task AShownImageDecodesItsPictureOnlyWhileItIsOnScreenAsync()
    {
        using var folder = new TemporaryFolder();
        var path = Path.Combine(folder.Path, "screen.png");
        await File.WriteAllBytesAsync(path, [137, 80, 78, 71], TestContext.Current.CancellationToken);
        var pictures = new FakePictures();
        var media = new MediaViewModel(JobId.New(), new MediaEntry("media", new Attachment("screen.png", "image/png", path), "The checkout page"), pictures, Tools.Actions(new FakeClipboard(), Tools.Noting()));

        await media.ShowCommand.ExecuteAsync(true);
        var shown = pictures.Held.ToList();
        await media.ShowCommand.ExecuteAsync(false);

        Assert.Equal([MediaViewModel.PictureWidth], shown);
        Assert.Empty(pictures.Held);
        Assert.Equal(("screen.png", "The checkout page", true), (media.Name, media.Caption, media.HasCaption));
    }

    [Fact]
    public async Task AShownImageWhoseCopyIsGoneShowsNoPictureAsync()
    {
        var pictures = new FakePictures();
        var media = new MediaViewModel(JobId.New(), new MediaEntry("media", new Attachment("screen.png", "image/png", "/nowhere/screen.png"), string.Empty), pictures, Tools.Actions(new FakeClipboard(), Tools.Noting()));

        await media.ShowCommand.ExecuteAsync(true);

        Assert.Equal((true, false), (media.Picture.IsNothing, media.HasCaption));
        Assert.Empty(pictures.Decoded);
    }
}
