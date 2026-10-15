using Avala.Testing.UI;
using Avala.Workbench.Conversation;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class AttachmentTrayViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task TheTrayShowsEachAttachmentAndWhyOneWasRefusedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignAttachmentTrayViewModel { Problem = "This connection does not take archive.zip." });

            Assert.Equal(2, view.Find<ItemsControl>("Chips").ItemCount);
            Assert.Equal("This connection does not take archive.zip.", view.TextOf("Problem"));
        }, Cancellation);

    [Fact]
    public Task TheComposerOffersAttachOnlyWhereTheConnectionTakesAttachmentsAndShowsTheTrayAboveTheDraftAsync() =>
        ui.RunAsync(() =>
        {
            var attached = 0;
            var offered = Screen.Show(new DesignComposerViewModel { Attachments = new DesignAttachmentTrayViewModel { AttachCommand = new AsyncRelayCommand(() => { attached++; return Task.CompletedTask; }) } });
            var refused = Screen.Show(new DesignComposerViewModel { Attachments = new DesignAttachmentTrayViewModel { CanAttach = false, Chips = [] } });

            offered.Click("Attach");

            Assert.Equal((true, true, 1), (offered.Shows("Attach"), offered.Shows("Attachments"), attached));
            Assert.Equal((false, false), (refused.Shows("Attach"), refused.Shows("Attachments")));
            Assert.True(offered.Find("Attachments").TranslatePoint(default, offered.Window)?.Y < offered.Find("Draft").TranslatePoint(default, offered.Window)?.Y);
        }, Cancellation);

    [Fact]
    public Task APromptShowsTheAttachmentsItWasSentWithAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignPromptViewModel("Why does this fail?") { Attachments = [new DesignAttachmentChipViewModel { CanRemove = false }] });

            Assert.Equal(1, view.Find<ItemsControl>("Attachments").ItemCount);
            Assert.False(view.Shows("Remove"));
        }, Cancellation);
}

public sealed class AttachmentChipViewScripts(HeadlessUi ui)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public Task AChipShowsItsNameAndKindAndRemovesItselfAsync() =>
        ui.RunAsync(() =>
        {
            var removed = 0;
            var view = Screen.Show(new DesignAttachmentChipViewModel { Name = "build.log", IsImage = false, RemoveCommand = new RelayCommand(() => removed++) });

            view.Click("Remove");

            Assert.Equal(("build.log", "LOG", false, 1), (view.TextOf("AttachmentName"), view.TextOf("Kind"), view.Shows("Picture"), removed));
        }, Cancellation);
}
