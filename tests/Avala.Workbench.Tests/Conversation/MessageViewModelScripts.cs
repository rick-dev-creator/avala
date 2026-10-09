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
        ViewModelScript.Given(new MessageViewModel(new MessageEntry("m", "Totals now round", Option<ItemOutcome>.None)))
            .Then(message => Assert.Equal(("Totals now round", true), (message.Text, message.IsStreaming)))
            .When(message => message.Update(new MessageEntry("m", "Totals now round to whole yen.", ItemOutcome.Succeeded)))
            .ThenNotified(nameof(MessageViewModel.Text), nameof(MessageViewModel.IsStreaming))
            .Then(message => Assert.Equal(("Totals now round to whole yen.", false), (message.Text, message.IsStreaming)));

    [Fact]
    public void AnEntryOfAnotherKindChangesNothing() =>
        ViewModelScript.Given(new MessageViewModel(new MessageEntry("m", "Hello", Option<ItemOutcome>.None)))
            .When(message => message.Update(new RestartEntry("m")))
            .Then(message => Assert.Equal(("Hello", true, true), (message.Text, message.IsStreaming, message.IsShown)));
}
