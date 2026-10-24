using System.Text.Json;
using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Host.Tests;

public sealed class ReadableFolderTests(PublishedPlugins plugins)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnAutonomousJobReadsASiblingRepositoryInAReadableFolderWhileAReadOutsideItIsStillDeniedAsync()
    {
        await using var run = await SimulatedRun.PreparedAsync(plugins, (".avala/permissions.json", """{ "autonomy": "autonomous" }"""));
        var worktrees = JsonSerializer.Serialize(Path.Combine(run.DataFolder, "worktrees"));
        await File.WriteAllTextAsync(Path.Combine(run.DataFolder, "readable.json"), $$"""{ "folders": [{{worktrees}}] }""", Cancellation);

        Outcomes.Succeeds(await run.SubmitAsync(new JobRequest(string.Empty, "[simulate: sibling-reads] Learn from the sibling")));
        var decisions = await run.DecisionsAsync(2);

        Assert.Equal(
            [(PolicyAnswer.Allow, "reads-in-readable-folders"), (PolicyAnswer.Deny, "autonomous-denies-the-rest")],
            decisions.Select(decision => (decision.Answer, decision.Rule.Match(rule => rule.Name, () => string.Empty))));
    }

    [Fact]
    public async Task AnAutonomousJobAsksAPersonOnceForAFolderOutsideItsWorktreeAndThenEditsThereWithoutAskingAsync()
    {
        await using var run = await SimulatedRun.StartAsync(plugins, "request-folder", (".avala/permissions.json", """{ "autonomy": "autonomous" }"""));
        var asked = await run.DecisionAsync();
        var beside = Path.GetDirectoryName(run.Worktree)!;
        Outcomes.Succeeds(await run.Get<IPermissionAnswers>().AnswerAsync(asked.Session, new PermissionReply(asked.Item, PermissionAnswer.Allow) { Remember = Remember.EditsInThisFolder }, Cancellation));
        var edit = await run.DecisionAsync();

        Assert.Equal(JobStatus.AwaitingReview, await run.SettledAsync());
        Assert.Equal((beside, DecisionDelivery.LeftToHuman, Option<FolderAccess>.Some(FolderAccess.Edit)), (asked.Target, asked.Delivery, asked.Folder.Map(request => request.Access)));
        Assert.Equal((Path.Combine(beside, "contracts.txt"), PolicyAnswer.Allow, "edits in this folder for this job"), (edit.Target, edit.Answer, edit.Rule.Match(rule => rule.Name, () => string.Empty)));
        Assert.Equal("Orders v2\n", await File.ReadAllTextAsync(Path.Combine(beside, "contracts.txt"), Cancellation));
    }
}
