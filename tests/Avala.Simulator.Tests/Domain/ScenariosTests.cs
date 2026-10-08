using Avala.Simulator.Domain;

namespace Avala.Simulator.Tests.Domain;

public sealed class ScenariosTests
{
    [Theory]
    [InlineData("[simulate: fix-after-feedback] Add a calculator", "fix-after-feedback")]
    [InlineData("Add a canvas [simulate:canvas]", "canvas")]
    [InlineData("[Simulate: Permission] Migrate the database", "permission")]
    [InlineData("Add GitHub login", "reply")]
    [InlineData("[simulate: unknown] Add GitHub login", "reply")]
    public void TheTagOfTheFirstMessageChoosesTheScenario(string message, string expected) =>
        Assert.Equal(expected, Scenarios.Choose(message).Name);

    [Fact]
    public void EveryScenarioThatReachesItsEndReportsUsageWithCostAndALimit()
    {
        var scripts = Scenarios.All
            .Where(scenario => scenario != Scenarios.Hang)
            .SelectMany(scenario => scenario.Turns.Select(script => (scenario.Name, Script: script)));

        Assert.All(scripts, turn =>
        {
            Assert.Contains(turn.Script, step => step is ReportUsage { Cost.Amount: > 0 });
            Assert.Contains(turn.Script, step => step is ReportLimit);
        });
    }
}
