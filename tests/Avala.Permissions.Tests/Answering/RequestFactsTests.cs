using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Permissions.Answering;
using Avala.Permissions.Policies;
using Avala.Sdk;

namespace Avala.Permissions.Tests.Answering;

public sealed class RequestFactsTests
{
    private static readonly string Workspace = Path.Combine(Path.GetTempPath(), "avala-workspace");

    public static TheoryData<string, string, bool> Edits => new()
    {
        { "src/app.cs", "src/app.cs", true },
        { "./src/../README.md", "README.md", true },
        { Path.Combine(Workspace, "docs", "a.md"), "docs/a.md", true },
        { "..foo/x.txt", "..foo/x.txt", true },
        { "../escape.txt", Path.GetFullPath(Path.Combine(Workspace, "..", "escape.txt")), false },
        { Path.Combine(Workspace + "-sibling", "x.txt"), Path.Combine(Workspace + "-sibling", "x.txt"), false },
    };

    [Theory]
    [MemberData(nameof(Edits))]
    public void AnEditIsLocatedAgainstTheWorkingDirectory(string target, string located, bool inside)
    {
        var facts = Requested(ItemKind.FileEdit, target).Facts(Workspace);

        Assert.Equal(new PermissionRequest(ItemKind.FileEdit, located, inside), facts);
    }

    [Fact]
    public void NothingIsInsideTheWorkspaceOfASessionWithoutAKnownWorkingDirectory()
    {
        var facts = Requested(ItemKind.FileEdit, "src/app.cs").Facts(Option<string>.None);

        Assert.Equal(new PermissionRequest(ItemKind.FileEdit, "src/app.cs", InsideWorkspace: false), facts);
    }

    [Fact]
    public void ACommandIsNeverInsideTheWorkspaceAndKeepsItsTarget()
    {
        var facts = Requested(ItemKind.Command, "rm -rf src").Facts(Workspace);

        Assert.Equal(new PermissionRequest(ItemKind.Command, "rm -rf src", InsideWorkspace: false), facts);
    }

    private static PermissionRequested Requested(ItemKind kind, string target) =>
        new(SessionId.New(), TurnId.New(), new ItemId("item"), "Request", kind, target);
}
