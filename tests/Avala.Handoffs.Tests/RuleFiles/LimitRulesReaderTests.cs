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

    private static LimitRulesReader Reader(CommittedFiles files, TemporaryFolder data) => new(files, new AvalaPaths(data.Path));

    private static async Task<TemporaryFolder> MachineAsync(string content)
    {
        var data = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(data.Path, LimitRulesReader.MachineFile), content, Cancellation);

        return data;
    }
}
