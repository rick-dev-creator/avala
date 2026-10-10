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
    public void AnEmptySectionHoldsAtNinetyPercentWithNoList()
    {
        var rules = Outcomes.Succeeds(LimitRulesParser.ParseMachine("{}"));

        Assert.Equal((OnLimit.Hold, LimitRules.DefaultThreshold, 0), (rules.OnLimit, rules.Threshold, rules.Connections.Count));
    }

    [Theory]
    [InlineData("hold", OnLimit.Hold)]
    [InlineData("handoff-same-harness", OnLimit.HandOffSameHarness)]
    [InlineData("handoff-any-harness", OnLimit.HandOffAnyHarness)]
    public void EachActionIsRead(string written, OnLimit action) =>
        Assert.Equal(action, Outcomes.Succeeds(LimitRulesParser.ParseMachine($$"""{ "onLimit": "{{written}}" }""")).OnLimit);

    [Theory]
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
