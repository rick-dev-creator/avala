using Avala.Testing;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class RestartViewModelScripts
{
    [Fact]
    public void TheRestartNoteSaysWhyWhatCameBeforeIsSummarized() =>
        ViewModelScript.Given(new RestartViewModel())
            .When(restart => restart.Update(new RestartEntry(EntryKeys.Restart)))
            .Then(restart => Assert.Equal(("Avala restarted. The agent's work before this point is summarized by its attempts above.", true), (restart.Note, restart.IsShown)));
}
