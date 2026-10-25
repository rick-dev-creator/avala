using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Settings;

namespace Avala.Workbench.Tests.Settings;

public sealed class WorktreeSettingsViewModelScripts
{
    private readonly FakeResources resources = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheDefaultsShowAMomentPerRowAndNothingNeedsSavingAsync()
    {
        var shown = await ShownAsync();

        Assert.Equal(
            ["Archived", "Approved and on its base branch", "Approved, pull request open", "Approved, not delivered yet", "Discarded", "Failed", "Waiting for you, untouched"],
            shown.Moments.Select(rule => rule.Title));
        Assert.Equal(
            ["Free it at once", "Free it at once", "Free it at once", "Free it after 1 day", "Free it at once", "Free it at once", "Free it after 3 days"],
            shown.Moments.Select(rule => rule.Choice));
        Assert.Equal(["After 7 days", "After 30 days"], shown.Deletions.Select(rule => rule.Choice));
        Assert.Equal(["20 GB", "10%"], shown.Limits.Select(rule => rule.Choice));
        Assert.Equal(("resources.json: Absent", false), (shown.File, shown.IsChanged));
    }

    [Fact]
    public async Task ChoosingAnotherDelayOffersToSaveAsync()
    {
        var shown = await ShownAsync();

        ViewModelScript.Given(shown)
            .When(settings => settings.Moments[0].Choice = "Keep it")
            .ThenNotified(nameof(IWorktreeSettingsViewModel.IsChanged))
            .Then(settings => Assert.True(settings.SaveCommand.CanExecute(null)));
    }

    [Fact]
    public async Task SavingWritesThePolicyTheRowsChoseAsync()
    {
        var shown = await ShownAsync();
        shown.Moments[0].Choice = "Keep it";
        shown.Moments[5].Choice = "Free it after 7 days";
        shown.Deletions[0].Choice = "Never";

        await shown.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(resources.Policies);
        Assert.Equal((Option<TimeSpan>.None, Option<TimeSpan>.Some(TimeSpan.FromDays(7)), Option<TimeSpan>.None), (saved.Archived, saved.Failed, saved.DeleteDiscarded));
        Assert.Equal((Option<TimeSpan>.Some(TimeSpan.FromDays(1)), Option<TimeSpan>.Some(TimeSpan.FromDays(30))), (saved.Undelivered, saved.DeleteFailed));
        Assert.Equal((false, string.Empty), (shown.IsChanged, shown.Error));
    }

    [Fact]
    public async Task SavingWritesTheSpaceBudgetAndTheDiskFloorTheLimitsChoseAsync()
    {
        var shown = await ShownAsync();
        shown.Limits[0].Choice = "50 GB";
        shown.Limits[1].Choice = "No floor";

        await shown.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(resources.Policies);
        Assert.Equal((Option<long>.Some(50L << 30), Option<double>.None), (saved.SpaceBudget, saved.DiskFloor));
    }

    [Fact]
    public async Task ADelayTheFileGivesThatIsNotAmongTheChoicesIsOfferedAsItIsAsync()
    {
        resources.Settings = resources.Settings with { Worktrees = resources.Settings.Worktrees with { Archived = TimeSpan.FromHours(2.5) } };

        var shown = await ShownAsync();

        Assert.Equal("Free it after 2.5 hours", shown.Moments[0].Choice);
        Assert.Contains("Free it after 2.5 hours", shown.Moments[0].Choices);
    }

    [Fact]
    public async Task ARefusedSaveSaysWhyAndKeepsTheDraftAsync()
    {
        var shown = await ShownAsync();
        resources.Refusal = ResourceError.Unwritable;
        shown.Moments[0].Choice = "Keep it";

        await shown.SaveCommand.ExecuteAsync(null);

        Assert.Equal(("resources.json could not be written.", true), (shown.Error, shown.IsChanged));
    }

    private async Task<WorktreeSettingsViewModel> ShownAsync()
    {
        var shown = new WorktreeSettingsViewModel(resources, new InlineUiDispatcher());
        await shown.LoadAsync(Cancellation);

        return shown;
    }
}
