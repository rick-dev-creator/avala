using Avala.Agents.Contracts.Events;
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

    private static PolicyRule Rule(
        string name,
        PolicyAnswer answer,
        Option<ItemKind> kind = default,
        string? target = null,
        RuleScope scope = RuleScope.Anywhere) =>
        new(RuleOrigin.Repository, name, kind, target.ToOption(), scope, answer);
}
