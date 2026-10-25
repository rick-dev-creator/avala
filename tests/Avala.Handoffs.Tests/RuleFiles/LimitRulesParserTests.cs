using Avala.Agents.Contracts.Connections;
using Avala.Handoffs.Contracts;
using Avala.Handoffs.Policy;
using Avala.Handoffs.RuleFiles;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Handoffs.Tests.RuleFiles;

public sealed class LimitRulesParserTests
{
    [Fact]
    public void AJobFileWithoutTheSectionDeclaresNothing() =>
        Assert.Equal(Option<LimitRules>.None, Outcomes.Succeeds(LimitRulesParser.ParseJobFile("""{ "connection": "work" }""")));

    [Fact]
    public void ASectionDeclaresItsActionThresholdAndConnections()
    {
        var rules = Outcomes.Present(Outcomes.Succeeds(LimitRulesParser.ParseJobFile(
            """{ "limits": { "onLimit": "handoff-any-harness", "threshold": 0.8, "connections": ["work", "personal"] } }""")));

        Assert.Equal((OnLimit.HandOffAnyHarness, 0.8), (rules.OnLimit, rules.Threshold));
        Assert.Equal([new ConnectionName("work"), new ConnectionName("personal")], rules.Connections);
    }

    [Fact]
    public void TheSectionIsReadWhateverTheDepthOfTheOtherSectionsOfTheJobFile() =>
        Assert.Equal(
            OnLimit.HandOffAnyHarness,
            Outcomes.Present(Outcomes.Succeeds(LimitRulesParser.ParseJobFile("""{ "limits": { "onLimit": "handoff-any-harness" }, "future": { "nested": { "deeper": ["x"] } } }"""))).OnLimit);

    [Fact]
    public void AnEmptySectionHoldsAtNinetyPercentWithNoListAndAsksBeforeResumingAtAReset()
    {
        var rules = Outcomes.Succeeds(LimitRulesParser.ParseMachine("{}"));

        Assert.Equal((OnLimit.Hold, LimitRules.DefaultThreshold, 0, ResumeAtReset.Ask), (rules.OnLimit, rules.Threshold, rules.Connections.Count, rules.Resume));
    }

    [Fact]
    public void AnEmptySectionCapsAtNinetyEightPercentPicksTheMostHeadroomWindsDownAndCompactsBeforeResuming()
    {
        var rules = Outcomes.Succeeds(LimitRulesParser.ParseMachine("{}"));

        Assert.Equal((LimitRules.DefaultHardCap, HandoffStrategy.MostHeadroom, true, true), (rules.HardCap, rules.Strategy, rules.WindDown, rules.CompactOnResume));
    }

    [Fact]
    public void ASectionDeclaresItsHardCapStrategyWindDownAndCompaction()
    {
        var rules = Outcomes.Succeeds(LimitRulesParser.ParseMachine(
            """{ "threshold": 0.85, "hardCap": 0.95, "strategy": "round-robin", "windDown": false, "compactOnResume": false }"""));

        Assert.Equal((0.95, HandoffStrategy.RoundRobin, false, false), (rules.HardCap, rules.Strategy, rules.WindDown, rules.CompactOnResume));
    }

    [Fact]
    public void AThresholdAboveTheDefaultHardCapRaisesTheCapToTheThreshold() =>
        Assert.Equal(0.99, Outcomes.Succeeds(LimitRulesParser.ParseMachine("""{ "threshold": 0.99 }""")).HardCap);

    [Theory]
    [InlineData("most-headroom", HandoffStrategy.MostHeadroom)]
    [InlineData("in-order", HandoffStrategy.InOrder)]
    [InlineData("soonest-reset", HandoffStrategy.SoonestReset)]
    [InlineData("round-robin", HandoffStrategy.RoundRobin)]
    public void EachStrategyIsRead(string written, HandoffStrategy strategy) =>
        Assert.Equal(strategy, Outcomes.Succeeds(LimitRulesParser.ParseMachine($$"""{ "strategy": "{{written}}" }""")).Strategy);

    [Theory]
    [InlineData("hold", OnLimit.Hold)]
    [InlineData("handoff-same-harness", OnLimit.HandOffSameHarness)]
    [InlineData("handoff-any-harness", OnLimit.HandOffAnyHarness)]
    public void EachActionIsRead(string written, OnLimit action) =>
        Assert.Equal(action, Outcomes.Succeeds(LimitRulesParser.ParseMachine($$"""{ "onLimit": "{{written}}" }""")).OnLimit);

    [Theory]
    [InlineData("ask", ResumeAtReset.Ask)]
    [InlineData("auto", ResumeAtReset.Auto)]
    public void EachWayOfResumingAtAResetIsRead(string written, ResumeAtReset resume) =>
        Assert.Equal(resume, Outcomes.Succeeds(LimitRulesParser.ParseMachine($$"""{ "resumeAtReset": "{{written}}" }""")).Resume);

    [Theory]
    [InlineData("""{ "resumeAtReset": "later" }""", HandoffError.UnknownResume)]
    [InlineData("""{ "resumeAtReset": true }""", HandoffError.Malformed)]
    [InlineData("""{ "onLimit": "move" }""", HandoffError.UnknownAction)]
    [InlineData("""{ "onLimit": 1 }""", HandoffError.Malformed)]
    [InlineData("""{ "threshold": 0 }""", HandoffError.InvalidThreshold)]
    [InlineData("""{ "threshold": 1.2 }""", HandoffError.InvalidThreshold)]
    [InlineData("""{ "threshold": "high" }""", HandoffError.Malformed)]
    [InlineData("""{ "connections": [] }""", HandoffError.InvalidConnections)]
    [InlineData("""{ "connections": ["work", "work"] }""", HandoffError.InvalidConnections)]
    [InlineData("""{ "connections": [" "] }""", HandoffError.InvalidConnections)]
    [InlineData("""{ "connections": [1] }""", HandoffError.InvalidConnections)]
    [InlineData("""{ "connections": "work" }""", HandoffError.Malformed)]
    [InlineData("""{ "onLimit": "hold", "onLimit": "hold" }""", HandoffError.Malformed)]
    [InlineData("""{ "when": "now" }""", HandoffError.UnknownField)]
    [InlineData("""{ "strategy": "cheapest" }""", HandoffError.UnknownStrategy)]
    [InlineData("""{ "strategy": 2 }""", HandoffError.Malformed)]
    [InlineData("""{ "threshold": 0.9, "hardCap": 0.8 }""", HandoffError.InvalidHardCap)]
    [InlineData("""{ "hardCap": 1.5 }""", HandoffError.InvalidHardCap)]
    [InlineData("""{ "hardCap": "high" }""", HandoffError.Malformed)]
    [InlineData("""{ "windDown": "yes" }""", HandoffError.Malformed)]
    [InlineData("""{ "compactOnResume": 1 }""", HandoffError.Malformed)]
    [InlineData("""not json""", HandoffError.Malformed)]
    [InlineData("""[]""", HandoffError.Malformed)]
    public void AnInvalidDeclarationIsRejectedWithItsError(string text, HandoffError error) =>
        Assert.Equal(error, Outcomes.FailsWith(LimitRulesParser.ParseMachine(text)));

    [Fact]
    public void MoreThanSixteenConnectionsAreRejected() =>
        Assert.Equal(
            HandoffError.InvalidConnections,
            Outcomes.FailsWith(LimitRulesParser.ParseMachine($$"""{ "connections": [{{string.Join(", ", Enumerable.Range(1, 17).Select(number => $"\"c{number}\""))}}] }""")));

    [Fact]
    public void AFileOverSixteenKilobytesIsRejected() =>
        Assert.Equal(HandoffError.TooLarge, Outcomes.FailsWith(LimitRulesParser.ParseMachine($$"""{ "onLimit": "hold" {{new string(' ', LimitRulesParser.MaximumBytes)}} }""")));

    [Fact]
    public void AnInvalidSectionOfTheJobFileIsRejected() =>
        Assert.Equal(HandoffError.UnknownAction, Outcomes.FailsWith(LimitRulesParser.ParseJobFile("""{ "limits": { "onLimit": "move" } }""")));
}
