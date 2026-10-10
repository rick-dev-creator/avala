using Avala.Agents.Contracts.Events;
using Avala.Permissions.Contracts;
using Avala.Permissions.Policies;
using Avala.Sdk;

namespace Avala.Permissions.Tests.Policies;

public sealed class RememberingTests
{
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

    [Theory]
    [MemberData(nameof(Requests))]
    public void AlwaysInThisRepositoryIsOfferedOnlyForARuleThatMatchesExactlyWhatWasAsked(ItemKind kind, string target, bool inside, string? exact)
    {
        var policy = PermissionPolicy.With([new(RuleOrigin.Repository, "pushes go to a human", ItemKind.Command, "git push*", RuleScope.Anywhere, PolicyAnswer.Ask)]);
        var request = new PermissionRequest(kind, target, inside)
        {
            Locate = path => new PermissionRequest(ItemKind.FileEdit, path, InsideWorkspace: !path.StartsWith('/')),
        };

        var offered = policy.InRepository(request);

        Assert.Equal(
            exact is null ? Option<PolicyRule>.None : new PolicyRule(RuleOrigin.Repository, "always in this repository", kind, exact, RuleScope.Anywhere, PolicyAnswer.Allow),
            offered);
    }
}
