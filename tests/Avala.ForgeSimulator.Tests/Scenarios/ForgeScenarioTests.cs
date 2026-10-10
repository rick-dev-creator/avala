using Avala.ForgeSimulator.Scenarios;
using Avala.Forges.Contracts;

namespace Avala.ForgeSimulator.Tests.Scenarios;

public sealed class ForgeScenarioTests
{
    [Theory]
    [InlineData("green", 0, "Open", "Mergeable", "Passed", null)]
    [InlineData("ci-fails-once", 0, "Open", "Mergeable", "Failed", null)]
    [InlineData("ci-fails-once", 1, "Open", "Mergeable", "Passed", null)]
    [InlineData("ci-always-fails", 1, "Open", "Mergeable", "Failed", null)]
    [InlineData("changes-requested-once", 0, "Open", "Mergeable", "Passed", "ChangesRequested")]
    [InlineData("changes-requested-once", 1, "Open", "Mergeable", "Passed", "Approved")]
    [InlineData("conflict-once", 0, "Open", "Conflicting", "Passed", null)]
    [InlineData("conflict-once", 1, "Open", "Mergeable", "Passed", null)]
    [InlineData("pending", 0, "Open", "Mergeable", "Pending", null)]
    [InlineData("merged", 0, "Merged", "Mergeable", "Passed", null)]
    public void EachScenarioStatesTheFactsOfItsFirstHeadAndOfTheLaterOnes(string scenario, int head, string lifecycle, string mergeability, string build, string? review)
    {
        var facts = ForgeScenario.Of(scenario, head);

        Assert.Equal(
            (Enum.Parse<PullRequestLifecycle>(lifecycle), Enum.Parse<Mergeability>(mergeability), Enum.Parse<CheckStatus>(build), review),
            (facts.Lifecycle, facts.Mergeability, Assert.Single(facts.Checks).Status, facts.Reviews.Select(found => found.Verdict.ToString()).SingleOrDefault()));
    }
}
