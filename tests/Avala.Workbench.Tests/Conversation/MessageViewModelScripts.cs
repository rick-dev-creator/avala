using Avala.Agents.Contracts.Events;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class MessageViewModelScripts
{
    [Fact]
    public void AMessageStreamsUntilItsOutcomeArrives() =>
        ViewModelScript.Given(new MessageViewModel(new MessageEntry("m", "Totals now round", Option<ItemOutcome>.None), FakeLinks.Opening))
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
        var message = new MessageViewModel(new MessageEntry("m", "See the link", ItemOutcome.Succeeded), new Avala.Workbench.Linking.Links(opener));

        await message.OpenLinkCommand.ExecuteAsync(link);

        Assert.Equal(opened, opener.Opened.Count == 1);
        Assert.Equal(opened, message.LinkNotice.Length == 0);
    }

    [Theory]
    [InlineData(FileOpenError.Unavailable, "No browser is available to open https://go.dev.")]
    [InlineData(FileOpenError.Refused, "The platform refused to open https://go.dev.")]
    public async Task ALinkThePlatformCannotOpenSaysSo(FileOpenError refusal, string notice)
    {
        var message = new MessageViewModel(new MessageEntry("m", "See the link", ItemOutcome.Succeeded), new Avala.Workbench.Linking.Links(new FakeLinks { Refusal = refusal }));

        await message.OpenLinkCommand.ExecuteAsync("https://go.dev");

        Assert.Equal(notice, message.LinkNotice);
    }

    [Fact]
    public void AnEntryOfAnotherKindChangesNothing() =>
        ViewModelScript.Given(new MessageViewModel(new MessageEntry("m", "Hello", Option<ItemOutcome>.None), FakeLinks.Opening))
            .When(message => message.Update(new RestartEntry("m", Kept: true)))
            .Then(message => Assert.Equal(("Hello", true, true), (message.Text, message.IsStreaming, message.IsShown)));
}
