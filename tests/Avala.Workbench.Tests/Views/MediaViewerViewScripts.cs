using Avala.Agents.Contracts.Sessions;
using Avala.Components.UI.Pictures;
using Avala.Testing;
using Avala.Testing.UI;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class MediaViewerViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ClickingAnImageOpensTheViewerWhoseArrowsStepAndEscapeClosesAsync() =>
        ui.RunAsync(async () =>
        {
            using var folder = new TemporaryFolder();
            var decoder = new PictureCache();
            var first = await MediaAsync(folder, "login.png", decoder);
            var second = await MediaAsync(folder, "checkout.png", decoder);
            var viewer = new MediaViewerViewModel(decoder);
            first.OpenRequested += (_, _) => viewer.Show([first.Shown, second.Shown], first.Shown);
            var image = Screen.Show(first);
            var sheet = Screen.Show(viewer);

            image.Click("Frame");
            await SettledAsync(sheet, viewer);
            var opened = (sheet.Find<Popup>("Sheet").IsOpen, sheet.TextOf("SheetTitle"), sheet.TextOf("Position"), sheet.TextOf("Caption"), sheet.Find<Image>("SheetPicture").Source is not null);
            sheet.Find<Border>("SheetCard").Focus();
            sheet.Press(Key.Right);
            await SettledAsync(sheet, viewer);
            var stepped = (sheet.TextOf("SheetTitle"), sheet.TextOf("Position"));
            sheet.Press(Key.Left);
            await SettledAsync(sheet, viewer);
            var back = sheet.TextOf("SheetTitle");
            sheet.Press(Key.Escape);
            sheet.Settle();

            Assert.Equal((true, "login.png", "1 of 2", "The login.png screen", true), opened);
            Assert.Equal(("checkout.png", "2 of 2"), stepped);
            Assert.Equal("login.png", back);
            Assert.False(sheet.Find<Popup>("Sheet").IsOpen);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ASingleImageShowsNoStepsAndNoPositionAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignMediaViewerViewModel { HasMany = false });

            Assert.Equal((false, false, false, true), (view.Shows("Previous"), view.Shows("Next"), view.Shows("Position"), view.Shows("Close")));
        }, TestContext.Current.CancellationToken);

    private static async Task SettledAsync(ViewScript view, MediaViewerViewModel viewer)
    {
        view.Settle();
        await (viewer.StepCommand.ExecutionTask ?? Task.CompletedTask);
        await (viewer.FitCommand.ExecutionTask ?? Task.CompletedTask);
        view.Settle();
    }

    private static async Task<MediaViewModel> MediaAsync(TemporaryFolder folder, string name, PictureCache decoder)
    {
        var path = Path.Combine(folder.Path, name);
        await File.WriteAllBytesAsync(path, Pngs.Of(8, 6), TestContext.Current.CancellationToken);

        return new MediaViewModel(Avala.Jobs.Contracts.JobId.New(), new MediaEntry(name, new Attachment(name, "image/png", path), $"The {name} screen"), decoder, Tools.Actions(new FakeClipboard(), Tools.Noting()));
    }
}
