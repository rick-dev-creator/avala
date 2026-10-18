using System.Collections.Concurrent;
using System.Text.Json;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Contracts;
using Avala.Canvas.Showing;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Canvas.Tests.Showing;

public sealed class MediaDeskTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void TheShowMediaToolIsExecutedByAvalaAndAsksForAPathAndAnOptionalCaption()
    {
        using var schema = JsonDocument.Parse(ShowMediaTool.Definition.InputSchema);

        Assert.Equal((ToolSurface.Executed, "show_media"), (ShowMediaTool.Definition.Surface, ShowMediaTool.Definition.Name));
        Assert.Equal(["path"], schema.RootElement.GetProperty("required").EnumerateArray().Select(field => field.GetString()));
        Assert.True(schema.RootElement.GetProperty("properties").TryGetProperty("caption", out _));
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("absolute")]
    public async Task AnImageTheAgentShowsIsKeptByAvalaAndShownWithItsCaptionAsync(string given)
    {
        await using var desk = new Desk();
        var image = await desk.WriteAsync(Path.Combine("screens", "settings.png"));

        await desk.ShowAsync(Input(given == "relative" ? Path.Combine("screens", "settings.png") : image, "The settings page at 1280 px"));

        var shown = Assert.Single(desk.Bus.Published.OfType<MediaShown>());
        Assert.Equal(
            (desk.Session, desk.Item, "settings.png", "image/png", "The settings page at 1280 px"),
            (shown.Session, shown.Item, shown.Media.Name, shown.Media.MediaType, shown.Caption));
        Assert.Equal(image, Assert.Single(desk.Attachments.Kept));
        var returned = Assert.Single(desk.Agents.Results);
        Assert.Equal((desk.Session, desk.Item, false), (returned.Session, returned.Result.Item, returned.Result.IsError));
        Assert.StartsWith("Shown", returned.Result.Content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing", "was not found")]
    [InlineData("not an image", "is not an image")]
    [InlineData("too large", "larger than")]
    [InlineData("no path", "needs a path")]
    [InlineData("not json", "needs a path")]
    public async Task MediaAvalaCannotShowIsRefusedWithTheReasonAndNothingIsShownAsync(string situation, string reason)
    {
        await using var desk = new Desk(tooLarge: situation == "too large");
        await desk.WriteAsync("notes.txt");
        await desk.WriteAsync("big.png");

        await desk.ShowAsync(situation switch
        {
            "missing" => Input("gone.png", string.Empty),
            "not an image" => Input("notes.txt", string.Empty),
            "too large" => Input("big.png", string.Empty),
            "no path" => """{ "caption": "Here" }""",
            _ => "show it",
        });

        Assert.Empty(desk.Bus.Published.OfType<MediaShown>());
        var returned = Assert.Single(desk.Agents.Results).Result;
        Assert.True(returned.IsError);
        Assert.Contains(reason, returned.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AToolCallOfAnotherToolIsLeftToItsOwnDeskAsync()
    {
        await using var desk = new Desk();
        await desk.WriteAsync("settings.png");

        await desk.ShowAsync(Input("settings.png", string.Empty), tool: "propose_follow_up");

        Assert.Empty(desk.Agents.Results);
        Assert.Empty(desk.Bus.Published);
    }

    private static string Input(string path, string caption) =>
        JsonSerializer.Serialize(new { path, caption });

    private sealed class Desk(bool tooLarge = false) : IAsyncDisposable
    {
        private readonly TemporaryFolder worktree = new();

        public SessionId Session { get; } = SessionId.New();

        public ItemId Item { get; } = new("show");

        public RecordingBus Bus { get; } = new();

        public ReturningAgents Agents { get; } = new();

        public KeepingAttachments Attachments { get; } = new(tooLarge);

        public async Task<string> WriteAsync(string relative)
        {
            var path = Path.Combine(worktree.Path, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, [0x89, 0x50, 0x4E, 0x47], Cancellation);

            return path;
        }

        public async Task ShowAsync(string input, string tool = ShowMediaTool.Name)
        {
            var desk = new MediaDesk(Attachments, Agents, Bus);
            await desk.HandleAsync(new SessionOpened(Session, new ProviderInfo("simulator", "Simulator"), worktree.Path, new ConnectionName("work")), Cancellation);
            await desk.HandleAsync(new AgentActivity(new ToolCalled(Session, TurnId.New(), Item, tool, input)), Cancellation);
        }

        public ValueTask DisposeAsync() => worktree.DisposeAsync();
    }
}
