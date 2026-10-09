using Avala.Agents.Contracts.Connections;
using Avala.Delegation.Contracts;
using Avala.Delegation.Delegating;
using Avala.Delegation.Policy;
using Avala.Delegation.RepositoryFiles;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Delegation.Tests.Policy;

public sealed class DelegationRulesTests
{
    private static readonly ConnectionName Work = new("work");
    private static readonly ConnectionName Personal = new("personal");

    [Fact]
    public void ADeclaredSectionNamesItsConnectionsRoutingDepthAndChildrenAndDefaultsTheRest()
    {
        var full = Outcomes.Present(Outcomes.Succeeds(DelegationRulesParser.Parse(
            """{ "approval": "merge", "delegation": { "connections": ["work", "personal"], "routing": "leastUsed", "maxDepth": 3, "maxChildren": 4 } }""")));
        var bare = Outcomes.Present(Outcomes.Succeeds(DelegationRulesParser.Parse("""{ "delegation": {} }""")));

        Assert.Equal([Work, Personal], full.Connections);
        Assert.Equal((Routing.LeastUsed, 3, 4), (full.Routing, full.MaxDepth, full.MaxChildren));
        Assert.Equal((0, Routing.RoundRobin, 1, 2), (bare.Connections.Count, bare.Routing, bare.MaxDepth, bare.MaxChildren));
    }

    [Fact]
    public void AJobFileWithoutADelegationSectionDeclaresNone() =>
        Assert.Equal(Option<DelegationRules>.None, Outcomes.Succeeds(DelegationRulesParser.Parse("""{ "approval": "merge" }""")));

    [Theory]
    [InlineData("not json", "Malformed")]
    [InlineData("[]", "Malformed")]
    [InlineData("""{ "delegation": ["work"] }""", "Malformed")]
    [InlineData("""{ "delegation": { "maxDepth": 1, "maxDepth": 2 } }""", "Malformed")]
    [InlineData("""{ "delegation": { "harness": "codex" } }""", "UnknownField")]
    [InlineData("""{ "delegation": { "connections": "work" } }""", "Malformed")]
    [InlineData("""{ "delegation": { "connections": [1] } }""", "Malformed")]
    [InlineData("""{ "delegation": { "connections": [] } }""", "InvalidConnections")]
    [InlineData("""{ "delegation": { "connections": ["work", "work"] } }""", "InvalidConnections")]
    [InlineData("""{ "delegation": { "connections": [" "] } }""", "InvalidConnections")]
    [InlineData("""{ "delegation": { "routing": "random" } }""", "UnknownRouting")]
    [InlineData("""{ "delegation": { "routing": 1 } }""", "Malformed")]
    [InlineData("""{ "delegation": { "maxDepth": 0 } }""", "InvalidDepth")]
    [InlineData("""{ "delegation": { "maxDepth": 9 } }""", "InvalidDepth")]
    [InlineData("""{ "delegation": { "maxDepth": "2" } }""", "Malformed")]
    [InlineData("""{ "delegation": { "maxChildren": 0 } }""", "InvalidChildren")]
    [InlineData("""{ "delegation": { "maxChildren": 1.5 } }""", "InvalidChildren")]
    [InlineData("""{ "delegation": { "maxChildren": 17 } }""", "InvalidChildren")]
    public void AnInvalidSectionIsRejectedWithItsReason(string text, string expected) =>
        Assert.Equal(Enum.Parse<DelegationError>(expected), Outcomes.FailsWith(DelegationRulesParser.Parse(text)));

    [Fact]
    public void RoundRobinGivesTheNthChildOfAParentTheNextConnectionInTurn()
    {
        var rules = new DelegationRules([Work, Personal], Routing.RoundRobin, 1, 2);

        Assert.Equal(
            [Work, Personal, Work],
            Enumerable.Range(0, 3).Select(earlier => Outcomes.Present(rules.Route(earlier, _ => 0))));
    }

    [Fact]
    public void LeastUsedPicksTheConnectionWhoseLimitIsLeastUsedAndTheFirstListedOnATie()
    {
        var rules = new DelegationRules([Work, Personal, new ConnectionName("spare")], Routing.LeastUsed, 1, 2);

        Assert.Equal(Personal, Outcomes.Present(rules.Route(0, connection => connection == Work ? 0.9 : 0.4)));
        Assert.Equal(Work, Outcomes.Present(rules.Route(5, _ => 0.4)));
    }

    [Fact]
    public void WithoutListedConnectionsNoConnectionIsRouted() =>
        Assert.Equal(Option<ConnectionName>.None, new DelegationRules([], Routing.RoundRobin, 1, 2).Route(0, _ => 0));

    [Theory]
    [InlineData(2, 0, "", "Autonomous", "DepthExceeded")]
    [InlineData(1, 2, "", "Autonomous", "TooManyChildren")]
    [InlineData(1, 0, "Autonomous", "Supervised", "AutonomyLoosened")]
    [InlineData(1, 0, "Supervised", "Autonomous", "")]
    [InlineData(1, 1, "Autonomous", "Autonomous", "")]
    public void AChildDeeperThanTheCapBeyondTheFanOutOrLooserThanItsParentIsRefused(int depth, int running, string asked, string granted, string expected)
    {
        var rules = new DelegationRules([], Routing.RoundRobin, 1, 2);

        var refused = rules.Refuses(
            depth,
            running,
            asked.Length == 0 ? Option<Autonomy>.None : Enum.Parse<Autonomy>(asked),
            Enum.Parse<Autonomy>(granted));

        Assert.Equal(expected.Length == 0 ? Option<DelegationError>.None : Enum.Parse<DelegationError>(expected), refused);
    }

    [Theory]
    [InlineData("""{ "instruction": "  Write the notes " }""", "Write the notes", "")]
    [InlineData("""{ "instruction": "Write the notes", "autonomy": "supervised" }""", "Write the notes", "Supervised")]
    public void AWellFormedInputGivesTheInstructionAndTheAutonomyAskedFor(string input, string instruction, string autonomy)
    {
        var parsed = Outcomes.Succeeds(DelegationInput.Parse(input));

        Assert.Equal(
            (instruction, autonomy.Length == 0 ? Option<Autonomy>.None : Enum.Parse<Autonomy>(autonomy)),
            (parsed.Instruction, parsed.Autonomy));
    }

    [Theory]
    [InlineData("write the notes")]
    [InlineData("""{ "autonomy": "supervised" }""")]
    [InlineData("""{ "instruction": " " }""")]
    [InlineData("""{ "instruction": 1 }""")]
    [InlineData("""{ "instruction": "Write", "autonomy": "trusted" }""")]
    [InlineData("""{ "instruction": "Write", "connection": "personal" }""")]
    public void AMalformedInputIsRefused(string input) =>
        Assert.Equal(DelegationError.MalformedInput, Outcomes.FailsWith(DelegationInput.Parse(input)));
}
