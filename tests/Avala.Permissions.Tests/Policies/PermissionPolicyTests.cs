using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.Policies;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Permissions.Tests.Policies;

public sealed class PermissionPolicyTests
{
    private static readonly PermissionRequest Migration = new(ItemKind.Command, "dotnet ef database update", InsideWorkspace: false);

    [Fact]
    public void ARequestNoRuleMatchesIsLeftToAHumanByTheDefault()
    {
        var verdict = PermissionPolicy.BuiltIn.Decide(Migration);

        Assert.Equal(new Verdict(PolicyAnswer.Ask, Option<PolicyRule>.None), verdict);
    }

    [Fact]
    public void TheFirstMatchingRuleDecides()
    {
        var policy = PermissionPolicy.With([Rule("deny-ef", PolicyAnswer.Deny, target: "dotnet ef *"), Rule("allow-dotnet", PolicyAnswer.Allow, target: "dotnet *")]);

        var verdict = policy.Decide(Migration);

        Assert.Equal(PolicyAnswer.Deny, verdict.Answer);
        Assert.Equal("deny-ef", Outcomes.Present(verdict.Rule).Name);
    }

    [Fact]
    public void ARuleForAKindIgnoresRequestsOfOtherKinds()
    {
        var policy = PermissionPolicy.With([Rule("no-web", PolicyAnswer.Deny, kind: ItemKind.Web)]);

        Assert.Equal(PolicyAnswer.Ask, policy.Decide(Migration).Answer);
        Assert.Equal(PolicyAnswer.Deny, policy.Decide(new PermissionRequest(ItemKind.Web, "https://example.com", false)).Answer);
    }

    [Theory]
    [InlineData("dotnet ef database update", true)]
    [InlineData("dotnet ef *", true)]
    [InlineData("dotnet ?f database update", true)]
    [InlineData("*update", true)]
    [InlineData("*", true)]
    [InlineData("dotnet ef", false)]
    [InlineData("ef *", false)]
    [InlineData("dotnet ef database update?", false)]
    [InlineData("Dotnet ef *", false)]
    public void ATargetPatternMatchesTheWholeTargetWithWildcards(string pattern, bool matches)
    {
        var policy = PermissionPolicy.With([Rule("pattern", PolicyAnswer.Allow, target: pattern)]);

        Assert.Equal(matches ? PolicyAnswer.Allow : PolicyAnswer.Ask, policy.Decide(Migration).Answer);
    }

    [Fact]
    public void AWorkspaceRuleMatchesOnlyRequestsInsideTheWorkspace()
    {
        var policy = PermissionPolicy.With([Rule("docs", PolicyAnswer.Deny, kind: ItemKind.FileEdit, target: "docs/*", scope: RuleScope.Workspace)]);

        Assert.Equal(PolicyAnswer.Deny, policy.Decide(new PermissionRequest(ItemKind.FileEdit, "docs/a.md", InsideWorkspace: true)).Answer);
        Assert.Equal(PolicyAnswer.Ask, policy.Decide(new PermissionRequest(ItemKind.FileEdit, "docs/a.md", InsideWorkspace: false)).Answer);
    }

    [Fact]
    public void TheBuiltInPolicyAllowsEditsInsideTheWorkspaceAndAsksForEditsOutside()
    {
        var inside = PermissionPolicy.BuiltIn.Decide(new PermissionRequest(ItemKind.FileEdit, "src/app.cs", InsideWorkspace: true));
        var outside = PermissionPolicy.BuiltIn.Decide(new PermissionRequest(ItemKind.FileEdit, "/etc/hosts", InsideWorkspace: false));

        Assert.Equal((PolicyAnswer.Allow, "edits-inside-the-workspace"), (inside.Answer, Outcomes.Present(inside.Rule).Name));
        Assert.Equal(PolicyAnswer.Ask, outside.Answer);
    }

    [Fact]
    public void RepositoryRulesComeBeforeTheBuiltInDefaults()
    {
        var policy = PermissionPolicy.With([Rule("frozen", PolicyAnswer.Deny, kind: ItemKind.FileEdit)]);

        var verdict = policy.Decide(new PermissionRequest(ItemKind.FileEdit, "src/app.cs", InsideWorkspace: true));

        Assert.Equal((PolicyAnswer.Deny, RuleOrigin.Repository), (verdict.Answer, Outcomes.Present(verdict.Rule).Origin));
    }

    [Fact]
    public void EditingThePolicyFileGoesToAHumanWhateverTheRepositoryRulesSay()
    {
        var policy = PermissionPolicy.With([Rule("anything", PolicyAnswer.Allow)]);

        var verdict = policy.Decide(new PermissionRequest(ItemKind.FileEdit, ".avala/permissions.json", InsideWorkspace: true));

        Assert.Equal((PolicyAnswer.Ask, RuleOrigin.BuiltIn), (verdict.Answer, Outcomes.Present(verdict.Rule).Origin));
    }

    [Fact]
    public void AnEditOutsideTheWorkspaceGoesToAHumanWhateverTheRepositoryRulesSay()
    {
        var policy = PermissionPolicy.With([Rule("edits", PolicyAnswer.Allow, kind: ItemKind.FileEdit)]);

        var verdict = policy.Decide(new PermissionRequest(ItemKind.FileEdit, "/etc/hosts", InsideWorkspace: false));

        Assert.Equal((PolicyAnswer.Ask, "edits-outside-the-workspace-go-to-a-human"), (verdict.Answer, Outcomes.Present(verdict.Rule).Name));
    }

    [Theory]
    [InlineData(ItemKind.Command, "dotnet build", false, PolicyAnswer.Allow, "autonomous-commands-in-the-worktree")]
    [InlineData(ItemKind.FileEdit, "src/app.cs", true, PolicyAnswer.Allow, "edits-inside-the-workspace")]
    [InlineData(ItemKind.FileEdit, "/etc/hosts", false, PolicyAnswer.Deny, "edits-outside-the-workspace-go-to-a-human")]
    [InlineData(ItemKind.FileEdit, ".avala/permissions.json", true, PolicyAnswer.Deny, "policy-file-goes-to-a-human")]
    [InlineData(ItemKind.Web, "https://example.com", false, PolicyAnswer.Deny, "autonomous-denies-the-rest")]
    [InlineData(ItemKind.Mcp, "deploy", false, PolicyAnswer.Deny, "autonomous-denies-the-rest")]
    public void AnAutonomousPolicyAllowsWhatStaysInTheWorktreeAndDeniesWhatItWouldAsk(
        ItemKind kind,
        string target,
        bool inside,
        PolicyAnswer answer,
        string rule)
    {
        var verdict = Autonomous([]).Decide(new PermissionRequest(kind, target, inside));

        Assert.Equal((answer, rule), (verdict.Answer, Outcomes.Present(verdict.Rule).Name));
    }

    [Fact]
    public void AnAutonomousPolicyKeepsWhatTheRepositoryDeniesAndDeniesWhatItWouldAsk()
    {
        var policy = Autonomous([Rule("no-ef", PolicyAnswer.Deny, target: "dotnet ef *"), Rule("ask-tests", PolicyAnswer.Ask, target: "dotnet test")]);

        var tests = policy.Decide(new PermissionRequest(ItemKind.Command, "dotnet test", false));

        Assert.Equal(PolicyAnswer.Deny, policy.Decide(Migration).Answer);
        Assert.Equal((PolicyAnswer.Deny, "ask-tests"), (tests.Answer, Outcomes.Present(tests.Rule).Name));
    }

    [Fact]
    public void ASessionRuleMatchesItsTargetExactlyAfterTheRepositoryRulesAndBeforeTheDefaults()
    {
        var policy = PermissionPolicy.With([Rule("no-ef", PolicyAnswer.Deny, target: "dotnet ef *")]);
        PolicyRule[] session =
        [
            new(RuleOrigin.Session, "don't ask again", ItemKind.Command, "dotnet *", RuleScope.Anywhere, PolicyAnswer.Allow),
            new(RuleOrigin.Session, "don't ask again", ItemKind.Command, "dotnet ef database update", RuleScope.Anywhere, PolicyAnswer.Allow),
            new(RuleOrigin.Session, "don't ask again", ItemKind.FileEdit, "src/app.cs", RuleScope.Anywhere, PolicyAnswer.Deny),
        ];

        var edit = policy.Decide(new PermissionRequest(ItemKind.FileEdit, "src/app.cs", true), session);

        Assert.Equal(PolicyAnswer.Deny, policy.Decide(Migration, session).Answer);
        Assert.Equal(PolicyAnswer.Ask, policy.Decide(new PermissionRequest(ItemKind.Command, "dotnet test", false), session).Answer);
        Assert.Equal(PolicyAnswer.Allow, policy.Decide(new PermissionRequest(ItemKind.Command, "dotnet *", false), session).Answer);
        Assert.Equal((PolicyAnswer.Deny, RuleOrigin.Session), (edit.Answer, Outcomes.Present(edit.Rule).Origin));
    }

    [Theory]
    [InlineData(Autonomy.Autonomous, null, Autonomy.Autonomous)]
    [InlineData(Autonomy.Autonomous, Autonomy.Supervised, Autonomy.Supervised)]
    [InlineData(Autonomy.Supervised, Autonomy.Autonomous, Autonomy.Supervised)]
    public void ACeilingOnlyEverLowersTheDeclaredAutonomy(Autonomy declared, Autonomy? ceiling, Autonomy effective) =>
        Assert.Equal(effective, (PermissionPolicy.With([]) with { Declared = declared }).Capped(ceiling.ToOption()).Autonomy);

    private static PermissionPolicy Autonomous(IReadOnlyList<PolicyRule> repository) =>
        new(repository, Autonomy.Autonomous, FormStrategy.Recommended);

    private static PolicyRule Rule(
        string name,
        PolicyAnswer answer,
        Option<ItemKind> kind = default,
        string? target = null,
        RuleScope scope = RuleScope.Anywhere) =>
        new(RuleOrigin.Repository, name, kind, target.ToOption(), scope, answer);
}
