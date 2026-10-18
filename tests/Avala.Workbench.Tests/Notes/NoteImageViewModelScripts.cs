using Avala.Workbench.Notes;

namespace Avala.Workbench.Tests.Notes;

public sealed class NoteImageViewModelScripts
{
    [Fact]
    public async Task AThumbnailDecodesItsImageWhenShownAndReleasesItWhenScrolledAwayAsync()
    {
        var file = Path.Combine(Directory.CreateTempSubdirectory().FullName, "login.png");
        await File.WriteAllBytesAsync(file, [1, 2, 3], TestContext.Current.CancellationToken);
        var pictures = new FakePictures();
        using var image = new NoteImageViewModel(file, pictures);

        await image.ShowCommand.ExecuteAsync(true);
        var held = pictures.Held.ToList();
        await image.ShowCommand.ExecuteAsync(false);

        Assert.Equal("login.png", image.Name);
        Assert.Equal([NoteImageViewModel.ThumbnailWidth], held);
        Assert.Empty(pictures.Held);
    }
}
