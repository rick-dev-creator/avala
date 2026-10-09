using Avala.Testing;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class InterjectionViewModelScripts
{
    [Fact]
    public void AMessageSentWhileTheAgentWorkedSaysItJoinedTheTurn() =>
        ViewModelScript.Given(new InterjectionViewModel(new InterjectionEntry("interjection:1", "Keep the alias")))
            .Then(interjection => Assert.Equal(("Keep the alias", "You, while it worked · joined the turn", true), (interjection.Text, interjection.Note, interjection.IsShown)));
}
