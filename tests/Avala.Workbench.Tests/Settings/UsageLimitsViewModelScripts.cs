using Avala.Agents.Contracts.Connections;
using Avala.Handoffs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Settings;

namespace Avala.Workbench.Tests.Settings;

public sealed class UsageLimitsViewModelScripts
{
    private readonly FakeLimitSettings limits = new();

    [Fact]
    public async Task WithoutALimitsFileTheDefaultsShowAndNothingNeedsSavingAsync()
    {
        var shown = await ShownAsync();

        Assert.Equal(("Hold the job", 90d, 98d, false, false), (shown.Action, shown.Threshold, shown.HardCap, shown.HandsOff, shown.IsChanged));
        Assert.Equal("limits.json: not created yet, so these defaults apply", shown.File);
        Assert.Equal(["claude-work", "claude-max"], shown.Connections.Select(row => row.Name));
        Assert.StartsWith("At 90% a job winds down, leaving its work in a valid state, and holds, and never passes 98%.", shown.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChoosingAHandoffRevealsTheRelayAndOffersToSaveAsync()
    {
        var shown = await ShownAsync();

        ViewModelScript.Given(shown)
            .When(limits => limits.Action = "Hand off to any harness")
            .ThenNotified(nameof(IUsageLimitsViewModel.HandsOff), nameof(IUsageLimitsViewModel.IsChanged))
            .Then(limits => Assert.Equal((true, true), (limits.HandsOff, limits.SaveCommand.CanExecute(null))));
    }

    [Fact]
    public async Task RaisingThePreparePointAboveTheHardCapRaisesTheCapWithItAsync()
    {
        var shown = await ShownAsync();

        shown.Threshold = 99;
        shown.HardCap = 95;

        Assert.Equal((99d, 99d, "99%"), (shown.Threshold, shown.HardCap, shown.HardCapLabel));
    }

    [Fact]
    public async Task SavingWritesTheChosenConnectionsInTheirOrderAsync()
    {
        var shown = await ShownAsync();
        shown.Action = "Hand off to any harness";
        shown.Strategy = "In order";
        shown.Threshold = 85;
        shown.ResumesByItself = true;
        shown.Connections[0].IsIncluded = true;
        shown.Connections[1].IsIncluded = true;
        shown.Connections[1].MoveUpCommand.Execute(null);

        await shown.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(limits.Saved);
        Assert.Equal((OnLimit.HandOffAnyHarness, 0.85, HandoffStrategy.InOrder, ResumeAtReset.Auto), (saved.OnLimit, saved.Threshold, saved.Strategy, saved.Resume));
        Assert.Equal(["claude-max", "claude-work"], saved.Connections.Select(connection => connection.Value));
        Assert.Equal((false, "limits.json: applied", string.Empty), (shown.IsChanged, shown.File, shown.Error));
    }

    [Fact]
    public async Task ARefusedSaveSaysWhyAndKeepsTheDraftAsync()
    {
        var shown = await ShownAsync();
        limits.Refusal = HandoffError.Unwritable;
        shown.WindsDown = false;

        await shown.SaveCommand.ExecuteAsync(null);

        Assert.Equal(("limits.json could not be written.", true), (shown.Error, shown.IsChanged));
    }

    [Fact]
    public async Task ARejectedFileSaysJobsHoldUntilItIsSavedAgainAsync()
    {
        limits.File = new LimitFile(new LimitSettings(OnLimit.Hold, 0.9, 0.98, []), LimitFileStatus.Rejected) { Problem = HandoffError.UnknownAction };

        var shown = await ShownAsync();

        Assert.Equal("limits.json is rejected (UnknownAction), so jobs hold at 90% until it is saved again", shown.File);
    }

    private async Task<UsageLimitsViewModel> ShownAsync()
    {
        var shown = new UsageLimitsViewModel(limits, new FakeConnections("claude-work", "claude-max"), new InlineUiDispatcher());
        shown.Activate();
        await shown.Loading;

        return shown;
    }
}
