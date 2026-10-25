using Avala.Handoffs.Contracts;
using Avala.Handoffs.Policy;
using Avala.Handoffs.RuleFiles;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Handoffs.Tests.RuleFiles;

public sealed class LimitRulesReaderTests
{
    private const string Worktree = "/worktrees/fix-rounding";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task WithoutEitherFileAJobHoldsAtNinetyPercentAsync()
    {
        await using var data = new TemporaryFolder();

        var rules = await Reader(new CommittedFiles().Workspace(Worktree), data).OfWorktreeAsync(Worktree, Cancellation);

        Assert.Equal((OnLimit.Hold, LimitRules.DefaultThreshold, Option<HandoffError>.None), (rules.OnLimit, rules.Threshold, rules.Error));
    }

    [Fact]
    public async Task TheMachinesSettingsApplyWhenTheRepositoryDeclaresNoSectionAsync()
    {
        await using var data = await MachineAsync("""{ "onLimit": "handoff-any-harness", "threshold": 0.8 }""");

        var rules = await Reader(new CommittedFiles().With(Worktree, LimitRulesReader.JobFile, """{ "connection": "auto" }"""), data).OfWorktreeAsync(Worktree, Cancellation);

        Assert.Equal((OnLimit.HandOffAnyHarness, 0.8), (rules.OnLimit, rules.Threshold));
    }

    [Fact]
    public async Task TheRepositorysSectionWinsOverTheMachineAsAWholeAsync()
    {
        await using var data = await MachineAsync("""{ "onLimit": "handoff-any-harness", "threshold": 0.8 }""");

        var rules = await Reader(new CommittedFiles().With(Worktree, LimitRulesReader.JobFile, """{ "limits": { "onLimit": "hold" } }"""), data).OfWorktreeAsync(Worktree, Cancellation);

        Assert.Equal((OnLimit.Hold, LimitRules.DefaultThreshold), (rules.OnLimit, rules.Threshold));
    }

    [Fact]
    public async Task RejectedRulesHoldAndSayWhyAsync()
    {
        await using var data = await MachineAsync("""{ "onLimit": "move" }""");

        var machine = await Reader(new CommittedFiles().Workspace(Worktree), data).OfWorktreeAsync(Worktree, Cancellation);
        var unreadable = await Reader(new CommittedFiles().Failing(Worktree, WorkspaceFailure.UnknownWorkspace), data).OfWorktreeAsync(Worktree, Cancellation);

        Assert.Equal((OnLimit.Hold, Option<HandoffError>.Some(HandoffError.UnknownAction)), (machine.OnLimit, machine.Error));
        Assert.Equal((OnLimit.Hold, Option<HandoffError>.Some(HandoffError.Unreadable)), (unreadable.OnLimit, unreadable.Error));
    }

    [Fact]
    public async Task SavedMachineSettingsApplyAtOnceAndReadBackFromTheFileAsync()
    {
        await using var data = new TemporaryFolder();
        var wanted = new LimitSettings(OnLimit.HandOffAnyHarness, 0.85, 0.95, [new("claude-work"), new("claude-max")]) { Strategy = HandoffStrategy.InOrder, Resume = ResumeAtReset.Auto, WindDown = false };
        await using var reader = Reader(new CommittedFiles().Workspace(Worktree), data);
        await ((ILimitSettings)reader).MachineAsync(Cancellation);

        var saved = await reader.ChangeMachineAsync(wanted, Cancellation);
        var applied = await reader.OfWorktreeAsync(Worktree, Cancellation);
        await using var fresh = Reader(new CommittedFiles().Workspace(Worktree), data);
        var reread = await ((ILimitSettings)fresh).MachineAsync(Cancellation);

        Assert.True(saved.IsSuccess);
        Assert.Equal((OnLimit.HandOffAnyHarness, 0.85, HandoffStrategy.InOrder), (applied.OnLimit, applied.Threshold, applied.Strategy));
        Assert.Equal(LimitFileStatus.Applied, reread.Status);
        Assert.Equivalent(wanted, reread.Settings);
    }

    [Fact]
    public async Task AHardCapBelowTheThresholdIsRefusedAndNothingIsWrittenAsync()
    {
        await using var data = new TemporaryFolder();
        await using var reader = Reader(new CommittedFiles().Workspace(Worktree), data);

        var saved = await reader.ChangeMachineAsync(new LimitSettings(OnLimit.Hold, 0.9, 0.8, []), Cancellation);

        Assert.Equal(HandoffError.InvalidHardCap, saved.Match(_ => HandoffError.Unwritable, error => error));
        Assert.False(File.Exists(Path.Combine(data.Path, LimitRulesReader.MachineFile)));
    }

    [Fact]
    public async Task WithoutAMachineFileTheSettingsAreTheDefaultsAndSayTheFileIsAbsentAsync()
    {
        await using var data = new TemporaryFolder();
        await using var reader = Reader(new CommittedFiles().Workspace(Worktree), data);

        var machine = await ((ILimitSettings)reader).MachineAsync(Cancellation);

        Assert.Equal((LimitFileStatus.Absent, OnLimit.Hold, LimitRules.DefaultThreshold), (machine.Status, machine.Settings.OnLimit, machine.Settings.Threshold));
    }

    private static LimitRulesReader Reader(CommittedFiles files, TemporaryFolder data) => new(files, new AvalaPaths(data.Path));

    private static async Task<TemporaryFolder> MachineAsync(string content)
    {
        var data = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(data.Path, LimitRulesReader.MachineFile), content, Cancellation);

        return data;
    }
}
