using Avala.Simulator.Scenarios;

namespace Avala.Simulator.Tests.Scenarios;

public sealed class ScenarioCatalogTests
{
    [Theory]
    [InlineData("[simulate: fix-after-feedback] Add a calculator", "fix-after-feedback")]
    [InlineData("Add a canvas [simulate:canvas]", "canvas")]
    [InlineData("[Simulate: Permission] Migrate the database", "permission")]
    [InlineData("Add GitHub login", "reply")]
    [InlineData("[simulate: unknown] Add GitHub login", "reply")]
    [InlineData("[simulate: contest] Fix the calculator", "contest")]
    [InlineData("[replay: edit-allowed] Greet the team", "replay:edit-allowed")]
    [InlineData("Greet the team [Replay as recorded: edit-allowed]", "replay as recorded:edit-allowed")]
    [InlineData("Write version 1 of Refund webhooks to .avala/docs/version.md. The job asked: [simulate: plan-approval] Add webhooks", "document")]
    public void TheTagOfTheFirstMessageChoosesTheScenario(string message, string expected) =>
        Assert.Equal(expected, ScenarioCatalog.NameIn(message));

    [Fact]
    public void EveryScenarioThatReachesItsEndReportsUsageWithCostAndALimit()
    {
        var scripts = ScenarioCatalog.All
            .Where(scenario => scenario != ScenarioCatalog.Hang)
            .SelectMany(scenario => scenario.Turns.Select(script => (scenario.Name, Script: script)))
            .Where(turn => !turn.Script.OfType<IgnoreInterrupt>().Any());

        Assert.All(scripts, turn =>
        {
            Assert.Contains(turn.Script, step => step is ReportUsage { Cost.Amount: > 0 });
            Assert.Contains(turn.Script, step => step is ReportLimit or ReportLimitResetting or RunOutOfWindow or IfTold { Step: ReportLimit or ReportLimitResetting });
        });
    }
}
