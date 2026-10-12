using System.Text;
using Avala.Agents.Contracts.Events;
using Avala.Sdk;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class FilePreviewViewModelScripts
{
    private static readonly byte[] Png = [137, 80, 78, 71, 13, 10, 26, 10];

    [Theory]
    [InlineData("orbit.png", true)]
    [InlineData("photos/moon.JPEG", true)]
    [InlineData("sky.webp", true)]
    [InlineData("orbits.svg", true)]
    [InlineData("sistema-solar.htm", true)]
    [InlineData("src/money.ts", false)]
    public void OnlyAWrittenImageSvgOrPageOffersAPreview(string path, bool previews) =>
        Assert.Equal(previews, new Worktree().Tool(Written(path)).ShowsPreview);

    [Fact]
    public void AFileStillBeingWrittenOffersNoPreview() =>
        Assert.False(new Worktree().Tool(Written("orbit.png") with { Outcome = Option<ItemOutcome>.None }).ShowsPreview);

    [Fact]
    public async Task ASmallWrittenImageShowsItsThumbnailUnderItsLineAsync()
    {
        var worktree = new Worktree();
        worktree.Files.Stored[Path.Combine(Worktree.Folder, "orbit.png")] = Png;
        var tool = worktree.Tool(Written("orbit.png"));

        await (tool.Preview.LoadCommand.ExecutionTask ?? Task.CompletedTask);
        await tool.Preview.RealizeCommand.ExecuteAsync(true);

        Assert.Equal((true, true, true), (tool.ShowsPreview, tool.Preview.HasThumbnail, tool.Preview.IsImage));
        Assert.Equal([FilePreviewViewModel.ThumbnailWidth], worktree.Pictures.Held);
        Assert.Same(worktree.Pictures.Decoded[^1], tool.Preview.Picture);
    }

    [Fact]
    public async Task AThumbnailReleasesItsPictureOnceItsRowIsNoLongerRealizedAsync()
    {
        var worktree = new Worktree();
        worktree.Files.Stored[Path.Combine(Worktree.Folder, "orbit.png")] = Png;
        var tool = worktree.Tool(Written("orbit.png"));
        await (tool.Preview.LoadCommand.ExecutionTask ?? Task.CompletedTask);
        await tool.Preview.RealizeCommand.ExecuteAsync(true);

        await tool.Preview.RealizeCommand.ExecuteAsync(false);

        Assert.Empty(worktree.Pictures.Held);
        Assert.True(tool.Preview.Picture.IsNothing);
    }

    [Fact]
    public async Task TheSheetShowsThePictureAtItsOwnWidthAndClosingItReturnsToTheThumbnailAsync()
    {
        var worktree = new Worktree();
        worktree.Files.Stored[Path.Combine(Worktree.Folder, "orbit.png")] = Png;
        var tool = worktree.Tool(Written("orbit.png"));
        await (tool.Preview.LoadCommand.ExecutionTask ?? Task.CompletedTask);
        await tool.Preview.RealizeCommand.ExecuteAsync(true);

        await tool.Preview.OpenCommand.ExecuteAsync(null);
        await tool.Preview.FitCommand.ExecuteAsync(1600);
        var opened = worktree.Pictures.Held.ToList();
        await tool.Preview.CloseCommand.ExecuteAsync(null);

        Assert.Equal([1600], opened);
        Assert.Equal([FilePreviewViewModel.ThumbnailWidth], worktree.Pictures.Held);
    }

    [Fact]
    public async Task ALargeImageHasNoThumbnailButStillOpensInTheSheetAsync()
    {
        var worktree = new Worktree();
        worktree.Files.Stored[Path.Combine(Worktree.Folder, "atlas.png")] = new byte[FilePreviewViewModel.ThumbnailLimit + 1];
        var tool = worktree.Tool(Written("atlas.png"));
        await (tool.Preview.LoadCommand.ExecutionTask ?? Task.CompletedTask);

        await tool.Preview.OpenCommand.ExecuteAsync(null);

        Assert.Equal((false, true), (tool.Preview.HasThumbnail, tool.Preview.IsOpen));
    }

    [Fact]
    public async Task AnSvgOpensInTheSheetDrawnAsASvgCanvasAndClosesAsync()
    {
        var worktree = new Worktree();
        worktree.Files.Stored[Path.Combine(Worktree.Folder, "orbits.svg")] = Encoding.UTF8.GetBytes("<svg/>");
        var tool = worktree.Tool(Written("orbits.svg"));

        await tool.Preview.OpenCommand.ExecuteAsync(null);
        var drawn = (tool.Preview.IsOpen, tool.Preview.Drawing.MediaType, tool.Preview.Drawing.Content);
        await tool.Preview.CloseCommand.ExecuteAsync(null);

        Assert.Equal((true, "image/svg+xml", "<svg/>"), drawn);
        Assert.False(tool.Preview.IsOpen);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task APageOpensInTheBrowserFromTheWorktreeOrOnceItIsGoneFromTheRepositoryAsync(bool gone)
    {
        var worktree = new Worktree { Gone = gone };
        worktree.Files.Stored[Path.Combine(Worktree.Folder, "sistema-solar.html")] = [60];
        worktree.Files.Stored[Path.Combine(Worktree.Repository, "sistema-solar.html")] = [60];
        var tool = worktree.Tool(Written("sistema-solar.html"));

        await tool.Preview.OpenCommand.ExecuteAsync(null);

        Assert.Equal([new Uri(Path.Combine(gone ? Worktree.Repository : Worktree.Folder, "sistema-solar.html"))], worktree.Opener.Opened);
        Assert.Equal((false, string.Empty, "Open in your browser"), (tool.Preview.IsOpen, tool.Preview.Notice, tool.Preview.Hint));
    }

    [Theory]
    [InlineData("sistema-solar.html")]
    [InlineData("orbit.png")]
    public async Task AFileNoLongerThereSaysSoInsteadOfOpeningAsync(string path)
    {
        var worktree = new Worktree();
        var tool = worktree.Tool(Written(path));

        await tool.Preview.OpenCommand.ExecuteAsync(null);

        Assert.Equal((false, $"{path} is no longer in the workspace or the repository."), (tool.Preview.IsOpen, tool.Preview.Notice));
        Assert.Empty(worktree.Opener.Opened);
    }

    private static ToolEntry Written(string path) =>
        new("t", ItemKind.FileEdit, $"Write {path}", string.Empty, ItemOutcome.Succeeded);
}
