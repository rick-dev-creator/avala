using Avala.Agents.Contracts.Events;
using Avala.Components.Code;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Conversation;
using Avala.Workbench.Linking;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class MessageViewModelScripts
{
    [Fact]
    public void AMessageStreamsUntilItsOutcomeArrives() =>
        ViewModelScript.Given(new MessageViewModel(new MessageEntry("m", "Totals now round", Option<ItemOutcome>.None), new FakeClipboard(), Tools.Reply()))
            .Then(message => Assert.Equal(("Totals now round", true), (message.Text, message.IsStreaming)))
            .When(message => message.Update(new MessageEntry("m", "Totals now round to whole yen.", ItemOutcome.Succeeded)))
            .ThenNotified(nameof(MessageViewModel.Text), nameof(MessageViewModel.IsStreaming))
            .Then(message => Assert.Equal(("Totals now round to whole yen.", false), (message.Text, message.IsStreaming)));

    [Theory]
    [InlineData("https://go.dev/ref/spec", true)]
    [InlineData("http://localhost:8080/health", true)]
    [InlineData("https://user:secret@example.com", false)]
    [InlineData("ftp://example.com/file", false)]
    [InlineData("mailto:team@example.com", false)]
    [InlineData("/etc/passwd", false)]
    public async Task OnlyWebLinksReachTheOpener(string link, bool opened)
    {
        var opener = new FakeLinks();
        var message = new MessageViewModel(new MessageEntry("m", "See the link", ItemOutcome.Succeeded), new FakeClipboard(), Tools.Reply(opener));

        await message.OpenLinkCommand.ExecuteAsync(link);

        Assert.Equal(opened, opener.Opened.Count == 1);
        Assert.Equal(opened, message.LinkNotice.Length == 0);
    }

    [Theory]
    [InlineData(FileOpenError.Unavailable, "No browser is available to open https://go.dev.")]
    [InlineData(FileOpenError.Refused, "The platform refused to open https://go.dev.")]
    public async Task ALinkThePlatformCannotOpenSaysSo(FileOpenError refusal, string notice)
    {
        var message = new MessageViewModel(new MessageEntry("m", "See the link", ItemOutcome.Succeeded), new FakeClipboard(), Tools.Reply(new FakeLinks { Refusal = refusal }));

        await message.OpenLinkCommand.ExecuteAsync("https://go.dev");

        Assert.Equal(notice, message.LinkNotice);
    }

    [Fact]
    public async Task CopyPutsTheWholeReplyOnTheClipboardAndConfirmsIt()
    {
        var clipboard = new FakeClipboard();
        var message = new MessageViewModel(new MessageEntry("m", "Totals now **round** to whole yen.", ItemOutcome.Succeeded), clipboard, Tools.Reply());

        await message.CopyCommand.ExecuteAsync(null);

        Assert.Equal(["Totals now **round** to whole yen."], clipboard.Copied);
        Assert.True(message.IsCopied);
    }

    [Fact]
    public async Task ACopyThePlatformRefusesConfirmsNothing()
    {
        var message = new MessageViewModel(new MessageEntry("m", "Totals now round", ItemOutcome.Succeeded), new FakeClipboard { Refuses = true }, Tools.Reply());

        await message.CopyCommand.ExecuteAsync(null);

        Assert.False(message.IsCopied);
    }

    [Fact]
    public async Task SavingACodeBlockOffersItUnderANameWithItsLanguagesExtensionAsync()
    {
        var saver = new FakeFileSaver();
        var message = new MessageViewModel(new MessageEntry("m", "Here", ItemOutcome.Succeeded), new FakeClipboard(), Tools.Reply(code: Code(saver)));

        await message.CodeCommand.ExecuteAsync(new CodeRequest(CodeAction.Save, "var x = 1;", ".cs"));

        Assert.Equal([("code.cs", "var x = 1;")], saver.Saved);
        Assert.Equal(string.Empty, message.LinkNotice);
    }

    [Fact]
    public async Task OpeningACodeBlockInTheEditorWritesItToAvalasCodeFolderAndOpensThatFileAsync()
    {
        var opener = new FakeOpener();
        var message = new MessageViewModel(new MessageEntry("m", "Here", ItemOutcome.Succeeded), new FakeClipboard(), Tools.Reply(code: Code(opener: opener)));

        await message.CodeCommand.ExecuteAsync(new CodeRequest(CodeAction.Open, "print(1)", ".py"));

        var opened = Assert.Single(opener.Opened);
        Assert.Equal((Path.Combine(Paths.Data, "code"), ".py", "print(1)"), (Path.GetDirectoryName(opened), Path.GetExtension(opened), opener.Created[opened]));
    }

    [Theory]
    [InlineData(FileSaveError.Cancelled, "")]
    [InlineData(FileSaveError.Unwritable, "The code could not be written to a file.")]
    public async Task ASaveThatDoesNotHappenSaysWhyUnlessThePersonCancelledAsync(FileSaveError refusal, string notice)
    {
        var message = new MessageViewModel(new MessageEntry("m", "Here", ItemOutcome.Succeeded), new FakeClipboard(), Tools.Reply(code: Code(new FakeFileSaver { Refusal = refusal })));

        await message.CodeCommand.ExecuteAsync(new CodeRequest(CodeAction.Save, "x", ".txt"));

        Assert.Equal(notice, message.LinkNotice);
    }

    [Fact]
    public async Task ACodeBlockNoAppOpensSaysSoAsync()
    {
        var message = new MessageViewModel(new MessageEntry("m", "Here", ItemOutcome.Succeeded), new FakeClipboard(), Tools.Reply(code: Code(opener: new FakeOpener { Refusal = FileOpenError.Refused })));

        await message.CodeCommand.ExecuteAsync(new CodeRequest(CodeAction.Open, "x", ".zig"));

        Assert.Equal("No app on this computer opens this kind of file.", message.LinkNotice);
    }

    [Fact]
    public void AnEntryOfAnotherKindChangesNothing() =>
        ViewModelScript.Given(new MessageViewModel(new MessageEntry("m", "Hello", Option<ItemOutcome>.None), new FakeClipboard(), Tools.Reply()))
            .When(message => message.Update(new RestartEntry("m", Kept: true)))
            .Then(message => Assert.Equal(("Hello", true, true), (message.Text, message.IsStreaming, message.IsShown)));

    private static readonly AvalaPaths Paths = new("/home/ana/.local/share/Avala");

    private static CodeFiles Code(FakeFileSaver? saver = null, FakeOpener? opener = null) => new(saver ?? new FakeFileSaver(), opener ?? new FakeOpener(), Paths);
}
