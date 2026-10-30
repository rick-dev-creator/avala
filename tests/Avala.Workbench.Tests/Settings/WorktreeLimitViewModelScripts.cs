using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Settings;

namespace Avala.Workbench.Tests.Settings;

public sealed class WorktreeLimitViewModelScripts
{
    [Fact]
    public void ChoosingAnotherLimitChangesWhatTheRowSavesAndSaysSo()
    {
        var changed = 0;
        var limit = new WorktreeLimitViewModel(new WorktreeLimit(WorktreePhrases.BudgetTitle, WorktreePhrases.BudgetNote, WorktreePhrases.BudgetChoices(20d), 20d), () => changed++);

        ViewModelScript.Given(limit)
            .When(row => row.Choice = "No limit")
            .ThenNotified(nameof(IWorktreeLimitViewModel.Choice))
            .Then(row => Assert.Equal((Option<double>.None, 1), (row.Value, changed)));
    }
}
