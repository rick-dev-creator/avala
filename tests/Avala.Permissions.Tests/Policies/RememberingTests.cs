using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.Governance;
using Avala.Permissions.Policies;
using Avala.Permissions.PolicyFiles;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Permissions.Tests.Policies;

public sealed class RememberingTests
{
    private static readonly PermissionPolicy Policy =
        PermissionPolicy.With([new(RuleOrigin.Repository, "pushes go to a human", ItemKind.Command, "git push*", RuleScope.Anywhere, PolicyAnswer.Ask)]);

    public static TheoryData<ItemKind, string, bool, string?> Requests => new()
    {
        { ItemKind.Command, "dotnet ef database update", false, "dotnet ef database update" },
        { ItemKind.Command, "dotnet   test  2>&1", false, "dotnet test" },
        { ItemKind.Command, "dotnet build > build.log", false, "dotnet build" },
        { ItemKind.Command, "ls -la; rm -rf build", false, null },
        { ItemKind.Command, "ls -la && git status", false, null },
        { ItemKind.Command, "FOO=1 node --test", false, null },
        { ItemKind.Command, "echo $(date)", false, null },
        { ItemKind.Command, "ls *.cs", false, null },
        { ItemKind.Command, "dotnet build > /tmp/build.log", false, null },
        { ItemKind.Command, "git push", false, null },
        { ItemKind.Web, "https://example.com/style", false, "https://example.com/style" },
        { ItemKind.FileEdit, "/etc/hosts", false, null },
        { ItemKind.FileEdit, ".avala/permissions.json", true, null },
    };

    public static TheoryData<ItemKind, string, bool, string?, ScopeRefusal?> Scopes => new()
    {
        { ItemKind.Command, "dotnet ef database update", false, "dotnet", null },
        { ItemKind.Command, "npm ci && npm test", false, "npm", null },
        { ItemKind.Command, "grep -rn TODO . | sed -n 1,5p | xargs touch", false, "sed xargs touch", null },
        { ItemKind.Command, "dotnet build > build.log", false, "dotnet", null },
        { ItemKind.Command, "echo $(date)", false, null, ScopeRefusal.Unanalysable },
        { ItemKind.Command, "ls 'unbalanced", false, null, ScopeRefusal.Unanalysable },
        { ItemKind.Command, "dotnet build > /tmp/build.log", false, null, ScopeRefusal.StillAsked },
        { ItemKind.Command, "git push --force", false, null, ScopeRefusal.StillAsked },
        { ItemKind.Web, "https://example.com/style?lang=en", false, "example.com", null },
        { ItemKind.Web, "how greetings are written", false, null, ScopeRefusal.NoSubject },
        { ItemKind.Mcp, "mcp__github__create_issue", false, "mcp__github__create_issue", null },
        { ItemKind.Other, "Canvas", false, "Canvas", null },
        { ItemKind.FileEdit, "/etc/hosts", false, null, ScopeRefusal.StillAsked },
        { ItemKind.FileEdit, ".avala/permissions.json", true, null, ScopeRefusal.StillAsked },
    };

    public static TheoryData<ItemKind, string, string, bool> Matches => new()
    {
        { ItemKind.Command, "grep sed", "grep -rn other /tmp", true },
        { ItemKind.Command, "grep sed", "sed -i s/a/b/ notes.txt", true },
        { ItemKind.Command, "grep sed", "grep x /tmp/a | sed 1d", true },
        { ItemKind.Command, "grep sed", "grep", true },
        { ItemKind.Command, "grep sed", "grep x /tmp/a; rm -rf build", false },
        { ItemKind.Command, "grep sed", "grep x /tmp/a > /tmp/out", false },
        { ItemKind.Command, "grep sed", "env grep x /tmp/a", false },
        { ItemKind.Command, "grep sed", "echo $(grep x /tmp/a)", false },
        { ItemKind.Command, "grep sed", "grepx /tmp/a", false },
        { ItemKind.Web, "example.com", "https://example.com/other?q=1", true },
        { ItemKind.Web, "example.com", "https://evil.test/example.com", false },
        { ItemKind.Mcp, "mcp__github__create_issue", "mcp__github__create_issue", true },
        { ItemKind.Mcp, "mcp__github__create_issue", "mcp__github__delete_repo", false },
        { ItemKind.Other, "Canvas", "Canvas", true },
    };

    [Theory]
    [MemberData(nameof(Requests))]
    public void AlwaysInThisRepositoryIsOfferedOnlyForARuleThatMatchesExactlyWhatWasAsked(ItemKind kind, string target, bool inside, string? exact)
    {
        var offered = Policy.InRepository(Request(kind, target, inside));

        Assert.Equal(
            exact is null ? Option<PolicyRule>.None : new PolicyRule(RuleOrigin.Repository, "always in this repository", kind, exact, RuleScope.Anywhere, PolicyAnswer.Allow),
            offered);
    }

    [Theory]
    [MemberData(nameof(Scopes))]
    public void AlwaysInThisJobNamesTheProgramsHostOrToolItWouldAllowOrWhyItCannot(ItemKind kind, string target, bool inside, string? subjects, ScopeRefusal? refusal)
    {
        var scope = Policy.InJob(Request(kind, target, inside), []);

        Assert.Equal(
            (subjects.ToOption(), refusal is { } refused ? Option<ScopeRefusal>.Some(refused) : Option<ScopeRefusal>.None),
            (scope.Rule.Bind(rule => rule.Target), scope.Refusal));
        Assert.All(scope.Rule.Match(rule => new[] { rule }, () => []), rule => Assert.Equal((RuleOrigin.Job, true), (rule.Origin, rule.AnyArguments)));
    }

    [Theory]
    [MemberData(nameof(Matches))]
    public void AnAlwaysRuleAllowsItsSubjectWithAnyArgumentsInEveryPartOfALineButNothingItChains(ItemKind kind, string subjects, string target, bool allowed)
    {
        PolicyRule[] job = [Remembering.Always(kind, subjects.Split(' '), PolicyAnswer.Allow, contest: false)];

        Assert.Equal(allowed ? PolicyAnswer.Allow : PolicyAnswer.Ask, PermissionPolicy.BuiltIn.Decide(Request(kind, target, inside: false), job).Answer);
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData("""{ "rules": [] }""", null, true)]
    [InlineData("""{ "autonomy": "autonomous" }""", Autonomy.Supervised, true)]
    [InlineData("""{ "autonomy": "supervised" }""", null, false)]
    [InlineData("""{ "autonomy": "sometimes" }""", null, false)]
    public void EverythingInThisJobIsOfferedOnlyWhenTheRepositoryDeclaresNoSupervisedCeiling(string? file, Autonomy? requested, bool raisable)
    {
        var read = new PolicyFile(
            Option<Workspaces.Contracts.FileOrigin>.None,
            file is null ? Option<PermissionPolicy>.None : PolicyFileParser.Parse(file).Map(Option<PermissionPolicy>.Some));

        var scope = read.Resolved.Policy.Capped(requested.ToOption()).InJob(Request(ItemKind.Command, "dotnet build", inside: false), []);

        Assert.Equal(raisable, scope.Raisable);
    }

    private static PermissionRequest Request(ItemKind kind, string target, bool inside) =>
        new(kind, target, inside)
        {
            Locate = path => new PermissionRequest(ItemKind.FileEdit, path, InsideWorkspace: !path.StartsWith('/')),
        };
}
