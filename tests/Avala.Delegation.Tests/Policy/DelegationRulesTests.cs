using Avala.Agents.Contracts.Connections;
using Avala.Delegation.Contracts;
using Avala.Delegation.Delegating;
using Avala.Delegation.Policy;
using Avala.Delegation.RepositoryFiles;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Delegation.Tests.Delegating;
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
            """{ "approval": "merge", "delegation": { "connections": ["work", "personal"], "routing": "roundRobin", "maxDepth": 3, "maxChildren": 4 } }""")));
        var bare = Outcomes.Present(Outcomes.Succeeds(DelegationRulesParser.Parse("""{ "delegation": {} }""")));

        Assert.Equal([Work, Personal], full.Connections);
        Assert.Equal((Routing.RoundRobin, 3, 4), (full.Routing, full.MaxDepth, full.MaxChildren));
        Assert.Equal((0, Routing.Capacity, 1, 2), (bare.Connections.Count, bare.Routing, bare.MaxDepth, bare.MaxChildren));
    }

    [Theory]
    [InlineData("capacity")]
    [InlineData("leastUsed")]
    public void CapacityAndItsEarlierNameLeastUsedRouteByCapacity(string routing) =>
        Assert.Equal(
            Routing.Capacity,
            Outcomes.Present(Outcomes.Succeeds(DelegationRulesParser.Parse($$"""{ "delegation": { "routing": "{{routing}}" } }"""))).Routing);

    [Fact]
    public void TheSectionDeclaresEscalationItsWindowAndTheRoleOfChildrenWhichOtherwiseFallToTheMachineThenTheBuiltIns()
    {
        var declared = Outcomes.Present(Outcomes.Succeeds(DelegationRulesParser.Parse(
            """{ "delegation": { "escalation": "parent", "parentWindowSeconds": 30, "role": "reviewer" } }""")));
        var bare = Outcomes.Present(Outcomes.Succeeds(DelegationRulesParser.Parse("""{ "delegation": {} }""")));
        var machine = Outcomes.Succeeds(DelegationRulesParser.ParseMachine("""{ "escalation": "parent", "parentWindowSeconds": 45 }"""));
        var human = new EscalationTerms(false, Option<TimeSpan>.None);

        Assert.Equal(Option<ParentEscalation>.Some(new ParentEscalation(TimeSpan.FromSeconds(30))), declared.Escalation.Over(human).Kept);
        Assert.Equal(Option<ChildRole>.Some(ChildRole.Reviewer), declared.Role);
        Assert.Equal(Option<ParentEscalation>.Some(new ParentEscalation(TimeSpan.FromSeconds(45))), bare.Escalation.Over(machine).Kept);
        Assert.Equal(Option<ParentEscalation>.None, bare.Escalation.Over(EscalationTerms.Undeclared).Kept);
        Assert.Equal(Option<ParentEscalation>.Some(new ParentEscalation(EscalationTerms.DefaultWindow)), (bare.Escalation with { AsksParent = true }).Over(EscalationTerms.Undeclared).Kept);
        Assert.Equal(Option<ParentEscalation>.None, (bare.Escalation with { AsksParent = false }).Over(machine).Kept);
        Assert.Equal(Option<ChildRole>.None, bare.Role);
    }

    [Theory]
    [InlineData("""{ "escalation": "parent", "maxDepth": 2 }""", "UnknownField")]
    [InlineData("""{ "escalation": "nobody" }""", "UnknownEscalation")]
    [InlineData("""{ "parentWindowSeconds": 0 }""", "InvalidWindow")]
    [InlineData("[]", "Malformed")]
    public void TheMachinesDelegationFileDeclaresOnlyEscalationAndItsWindow(string text, string expected) =>
        Assert.Equal(Enum.Parse<DelegationError>(expected), Outcomes.FailsWith(DelegationRulesParser.ParseMachine(text)));

    [Theory]
    [InlineData(null, ChildRole.Worker, null, ChildRole.Worker, null)]
    [InlineData("reviewer", ChildRole.Worker, null, ChildRole.Reviewer, null)]
    [InlineData(null, ChildRole.Worker, ChildRole.Research, ChildRole.Research, null)]
    [InlineData(null, ChildRole.Reviewer, null, ChildRole.Reviewer, null)]
    [InlineData("worker", ChildRole.Reviewer, null, ChildRole.Worker, "RoleLoosened")]
    [InlineData("worker", ChildRole.Worker, ChildRole.Reviewer, ChildRole.Worker, "RoleLoosened")]
    [InlineData("research", ChildRole.Reviewer, null, ChildRole.Research, null)]
    public void AChildIsReadOnlyWhenItsCallItsSectionOrItsParentSaysSoAndCanNeverBeLooser(string? asked, ChildRole caller, ChildRole? section, ChildRole role, string? refusal)
    {
        var rules = new DelegationRules([], Routing.Capacity, 1, 2) { Role = section.ToOption() };
        var named = asked is null ? Option<ChildRole>.None : Roles.Named(asked);

        Assert.Equal(
            (role, refusal is null ? Option<DelegationError>.None : Enum.Parse<DelegationError>(refusal)),
            (rules.RoleOf(named, caller), rules.RefusesRole(named, caller)));
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
    [InlineData("""{ "delegation": { "escalation": "agent" } }""", "UnknownEscalation")]
    [InlineData("""{ "delegation": { "escalation": true } }""", "Malformed")]
    [InlineData("""{ "delegation": { "parentWindowSeconds": 9 } }""", "InvalidWindow")]
    [InlineData("""{ "delegation": { "parentWindowSeconds": 3601 } }""", "InvalidWindow")]
    [InlineData("""{ "delegation": { "parentWindowSeconds": 30.5 } }""", "InvalidWindow")]
    [InlineData("""{ "delegation": { "role": "editor" } }""", "UnknownRole")]
    public void AnInvalidSectionIsRejectedWithItsReason(string text, string expected) =>
        Assert.Equal(Enum.Parse<DelegationError>(expected), Outcomes.FailsWith(DelegationRulesParser.Parse(text)));

    [Fact]
    public void RoundRobinGivesTheNthChildOfAParentTheNextConnectionInTurn()
    {
        var rules = new DelegationRules([Work, Personal], Routing.RoundRobin, 1, 2);

        Assert.Equal(
            [Work, Personal, Work],
            Enumerable.Range(0, 3).Select(earlier => Outcomes.Present(rules.InTurn(earlier))));
    }

    [Fact]
    public async Task CapacityRoutingAsksTheSelectorAmongTheListedConnectionsAndKeepsItsChoiceAsync()
    {
        var selector = new FixedSelector { Chosen = Personal };

        var routed = await new ConnectionRouter([selector]).RouteAsync(
            new DelegationRules([Work, Personal], Routing.Capacity, 1, 2),
            0,
            Desk.Worktree,
            TestContext.Current.CancellationToken);

        var question = Assert.Single(selector.Questions);
        Assert.Equal(Desk.Worktree, question.Worktree);
        Assert.Equal([Work, Personal], question.Candidates);
        Assert.Equal(Option<ConnectionName>.Some(Personal), routed.Connection);
        Assert.Equal(Option<ConnectionName>.Some(Personal), routed.Choice.Map(choice => choice.Connection));
    }

    [Theory]
    [InlineData("capacity", true, "work")]
    [InlineData("roundRobin", true, "personal")]
    [InlineData("capacity", false, "")]
    public async Task WithoutASelectorCapacityTakesTheFirstListedRoundRobinIgnoresItAndNoListRoutesNothingAsync(string routing, bool listed, string expected)
    {
        var selector = new FixedSelector { Chosen = Personal };
        var rules = new DelegationRules(listed ? [Work, Personal] : [], routing == "capacity" ? Routing.Capacity : Routing.RoundRobin, 1, 2);
        var selectors = routing == "capacity" ? Array.Empty<IConnectionSelector>() : [selector];

        var routed = await new ConnectionRouter(selectors).RouteAsync(rules, 1, Desk.Worktree, TestContext.Current.CancellationToken);

        Assert.Equal(expected.Length == 0 ? Option<ConnectionName>.None : new ConnectionName(expected), routed.Connection);
        Assert.True(routed.Choice.IsNone);
        Assert.Empty(selector.Questions);
    }

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
