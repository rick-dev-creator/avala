using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Settings;

namespace Avala.Workbench.Tests.Settings;

public sealed class WorktreeRuleViewModelScripts
{
    [Fact]
    public void ChoosingAnotherDelayChangesWhatTheRowSavesAndSaysSo()
    {
        var changed = 0;
        var rule = new WorktreeRuleViewModel(new WorktreeRule("Failed", "The job failed.", WorktreePhrases.FreeChoices(TimeSpan.Zero), TimeSpan.Zero), () => changed++);

        ViewModelScript.Given(rule)
            .When(row => row.Choice = "Free it after 3 days")
            .ThenNotified(nameof(IWorktreeRuleViewModel.Choice))
            .Then(row => Assert.Equal((Option<TimeSpan>.Some(TimeSpan.FromDays(3)), 1), (row.After, changed)));
    }

    [Fact]
    public void KeepingSavesNoDelay()
    {
        var rule = new WorktreeRuleViewModel(new WorktreeRule("Failed", "The job failed.", WorktreePhrases.FreeChoices(TimeSpan.Zero), TimeSpan.Zero), () => { });

        rule.Choice = "Keep it";

        Assert.Equal(Option<TimeSpan>.None, rule.After);
    }
}
