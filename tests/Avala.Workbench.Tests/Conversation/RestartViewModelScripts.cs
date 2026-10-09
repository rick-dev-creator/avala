using Avala.Testing;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class RestartViewModelScripts
{
    [Theory]
    [InlineData(true, "Avala restarted. Everything above happened before the restart.")]
    [InlineData(false, "Avala restarted. This job ran before conversations were kept, so its work before this point is summarized by its attempts above.")]
    public void TheRestartNoteSaysWhetherTheConversationAboveIsWhatHappenedOrOnlyItsAttempts(bool kept, string note) =>
        ViewModelScript.Given(new RestartViewModel(new RestartEntry(EntryKeys.Restart, kept)))
            .Then(restart => Assert.Equal((note, true), (restart.Note, restart.IsShown)));
}
