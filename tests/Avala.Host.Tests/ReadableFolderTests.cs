using System.Text.Json;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
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
}
