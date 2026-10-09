using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Permissions.Answering;
using Avala.Permissions.Links;
using Avala.Permissions.Policies;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Permissions.Tests.Answering;

public sealed class RequestFactsTests
{
    private static readonly string Workspace = Path.Combine(Path.GetTempPath(), "avala-workspace");

    private static readonly SymbolicLinks Links = new();

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
        var facts = Requested(ItemKind.FileEdit, target).Facts(Workspace, Links);

        Assert.Equal(new PermissionRequest(ItemKind.FileEdit, located, inside), facts);
    }

    [Fact]
    public void NothingIsInsideTheWorkspaceOfASessionWithoutAKnownWorkingDirectory()
    {
        var edit = Requested(ItemKind.FileEdit, "src/app.cs").Facts(Option<string>.None, Links);
        var command = Requested(ItemKind.Command, "echo x > src/app.cs").Facts(Option<string>.None, Links);

        Assert.Equal(new PermissionRequest(ItemKind.FileEdit, "src/app.cs", InsideWorkspace: false), edit);
        Assert.Equal(edit, command.Locate("src/app.cs"));
    }

    [Fact]
    public void ACommandKeepsItsTargetAndLocatesWhatItWritesAgainstTheWorkingDirectory()
    {
        var facts = Requested(ItemKind.Command, "rm -rf src > ../log.txt").Facts(Workspace, Links);

        Assert.Equal((ItemKind.Command, "rm -rf src > ../log.txt", false), (facts.Kind, facts.Target, facts.InsideWorkspace));
        Assert.Equal(new PermissionRequest(ItemKind.FileEdit, "out/log.txt", InsideWorkspace: true), facts.Locate("out/log.txt"));
        Assert.Equal(new PermissionRequest(ItemKind.FileEdit, Path.GetFullPath(Path.Combine(Workspace, "..", "log.txt")), InsideWorkspace: false), facts.Locate("../log.txt"));
    }

    [Fact]
    public void AnEditThroughASymbolicLinkIsLocatedWhereTheLinkLeads()
    {
        using var workspace = new TemporaryFolder();
        using var elsewhere = new TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "src"));
        Directory.CreateSymbolicLink(Path.Combine(workspace.Path, "home"), elsewhere.Path);
        Directory.CreateSymbolicLink(Path.Combine(workspace.Path, "src", "shared"), Path.Combine("..", "src"));

        var escaping = Requested(ItemKind.FileEdit, "home/.bashrc").Facts(workspace.Path, Links);
        var climbing = Requested(ItemKind.FileEdit, "home/../outside.txt").Facts(workspace.Path, Links);
        var staying = Requested(ItemKind.FileEdit, "src/shared/app.cs").Facts(workspace.Path, Links);

        Assert.Equal(new PermissionRequest(ItemKind.FileEdit, Path.Combine(Outcomes.Present(Links.Resolve(elsewhere.Path)), ".bashrc"), InsideWorkspace: false), escaping);
        Assert.False(climbing.InsideWorkspace);
        Assert.Equal(new PermissionRequest(ItemKind.FileEdit, "src/app.cs", InsideWorkspace: true), staying);
    }

    [Fact]
    public void AnEditThroughALoopOfLinksIsNeverInsideTheWorkspace()
    {
        using var workspace = new TemporaryFolder();
        File.CreateSymbolicLink(Path.Combine(workspace.Path, "a"), Path.Combine(workspace.Path, "b"));
        File.CreateSymbolicLink(Path.Combine(workspace.Path, "b"), Path.Combine(workspace.Path, "a"));

        Assert.False(Requested(ItemKind.FileEdit, "a/x.txt").Facts(workspace.Path, Links).InsideWorkspace);
    }

    private static PermissionRequested Requested(ItemKind kind, string target) =>
        new(SessionId.New(), TurnId.New(), new ItemId("item"), "Request", kind, target);
}
