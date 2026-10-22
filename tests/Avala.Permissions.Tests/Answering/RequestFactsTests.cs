using System.Diagnostics;
using System.Runtime.Versioning;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Permissions.Answering;
using Avala.Permissions.Contracts;
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
    public void AnEditThroughASymbolicLinkIsLocatedWhereTheLinkLeadsAndShownAsAsked()
    {
        using var workspace = new TemporaryFolder();
        using var elsewhere = new TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "src"));
        Directory.CreateSymbolicLink(Path.Combine(workspace.Path, "home"), elsewhere.Path);
        Directory.CreateSymbolicLink(Path.Combine(workspace.Path, "src", "shared"), Path.Combine("..", "src"));

        var escaping = Requested(ItemKind.FileEdit, "home/.bashrc").Facts(workspace.Path, Links);
        var climbing = Requested(ItemKind.FileEdit, "home/../outside.txt").Facts(workspace.Path, Links);
        var staying = Requested(ItemKind.FileEdit, "src/shared/app.cs").Facts(workspace.Path, Links);

        Assert.Equal(new PermissionRequest(ItemKind.FileEdit, Path.Combine(workspace.Path, "home", ".bashrc"), InsideWorkspace: false), escaping);
        Assert.Equal(new PermissionRequest(ItemKind.FileEdit, Path.Combine(workspace.Path, "home", "..", "outside.txt"), InsideWorkspace: false), climbing);
        Assert.Equal(new PermissionRequest(ItemKind.FileEdit, "src/app.cs", InsideWorkspace: true), staying);
    }

    [Fact]
    public void AnEditOutsideAWorkingDirectoryReachedThroughALinkIsShownByThePathItWasAskedWith()
    {
        using var folder = new TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(folder.Path, "private", "var", "workspace"));
        Directory.CreateSymbolicLink(Path.Combine(folder.Path, "var"), Path.Combine(folder.Path, "private", "var"));
        var workspace = Path.Combine(folder.Path, "var", "workspace");

        var climbing = Requested(ItemKind.FileEdit, "../escape.txt").Facts(workspace, Links);
        var absolute = Requested(ItemKind.FileEdit, Path.Combine(folder.Path, "var", "other", "x.txt")).Facts(workspace, Links);
        var inside = Requested(ItemKind.FileEdit, Path.Combine(folder.Path, "private", "var", "workspace", "src", "app.cs")).Facts(workspace, Links);

        Assert.Equal(new PermissionRequest(ItemKind.FileEdit, Path.Combine(folder.Path, "var", "escape.txt"), InsideWorkspace: false), climbing);
        Assert.Equal(new PermissionRequest(ItemKind.FileEdit, Path.Combine(folder.Path, "var", "other", "x.txt"), InsideWorkspace: false), absolute);
        Assert.Equal(new PermissionRequest(ItemKind.FileEdit, "src/app.cs", InsideWorkspace: true), inside);
    }

    [Fact]
    public void AnEditThroughALoopOfLinksIsNeverInsideTheWorkspace()
    {
        using var workspace = new TemporaryFolder();
        File.CreateSymbolicLink(Path.Combine(workspace.Path, "a"), Path.Combine(workspace.Path, "b"));
        File.CreateSymbolicLink(Path.Combine(workspace.Path, "b"), Path.Combine(workspace.Path, "a"));

        Assert.False(Requested(ItemKind.FileEdit, "a/x.txt").Facts(workspace.Path, Links).InsideWorkspace);
    }

    [Fact]
    public void AnEditInsideTheSessionsScratchFolderIsInsideItsWorkspaceAndShownByItsPathWhileALinkOutOfItIsNot()
    {
        using var workspace = new TemporaryFolder();
        using var scratch = new TemporaryFolder();
        using var elsewhere = new TemporaryFolder();
        Directory.CreateSymbolicLink(Path.Combine(scratch.Path, "out"), elsewhere.Path);
        var notes = Path.Combine(scratch.Path, "notes.txt");

        var kept = Requested(ItemKind.FileEdit, notes).Facts(workspace.Path, scratch.Path, [], Links);
        var written = Requested(ItemKind.Command, $"echo x > {notes}").Facts(workspace.Path, scratch.Path, [], Links).Locate(notes);
        var escaping = Requested(ItemKind.FileEdit, Path.Combine(scratch.Path, "out", "x.txt")).Facts(workspace.Path, scratch.Path, [], Links);

        Assert.Equal(new PermissionRequest(ItemKind.FileEdit, notes, InsideWorkspace: true), kept);
        Assert.Equal(kept, written);
        Assert.False(escaping.InsideWorkspace);
    }

    [Fact]
    public void AReadIsReadableOnlyWhenItsRealPathIsInsideAReadableFolder()
    {
        using var workspace = new TemporaryFolder();
        using var projects = new TemporaryFolder();
        using var elsewhere = new TemporaryFolder();
        Directory.CreateSymbolicLink(Path.Combine(projects.Path, "out"), elsewhere.Path);
        string[] readable = [projects.Path];

        bool Reads(string path) => (Requested(ItemKind.Other, path) with { Reads = path }).Facts(workspace.Path, Option<string>.None, readable, Links).Readable;

        Assert.Equal(
            [true, false, false],
            new[] { Path.Combine(projects.Path, "sibling", "README.md"), Path.Combine(projects.Path, "out", "secret.env"), Path.Combine(elsewhere.Path, "secret.env") }.Select(Reads));
    }

    [Theory]
    [InlineData("grep -R secret docs")]
    [InlineData("rg --follow secret docs")]
    [InlineData("find -L docs -name secret.env")]
    [InlineData("cat docs/*/secret.env")]
    [InlineData("grep -rn secret docs/hom?")]
    [InlineData("ls docs/h[o]me/")]
    [InlineData("cat docs/*.md")]
    [InlineData("head -n 1 docs/l*")]
    [InlineData("grep -rn secret docs/h*")]
    [InlineData("ls docs/*/")]
    public async Task AReadThatFollowsALinkOutOfTheWorktreeRecursivelyOrThroughAGlobStillAsks(string line)
    {
        using var workspace = new TemporaryFolder();
        using var elsewhere = new TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "docs"));
        Directory.CreateSymbolicLink(Path.Combine(workspace.Path, "docs", "home"), elsewhere.Path);
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "docs", "a.md"), "a", TestContext.Current.CancellationToken);
        File.CreateSymbolicLink(Path.Combine(workspace.Path, "docs", "leak.md"), Path.Combine(elsewhere.Path, "secret.md"));

        Assert.Equal(PolicyAnswer.Ask, PermissionPolicy.BuiltIn.Decide(Requested(ItemKind.Command, line).Facts(workspace.Path, Links)).Answer);
    }

    [Theory]
    [InlineData("cat docs/*.md")]
    [InlineData("grep -rn TODO docs/*")]
    [InlineData("ls docs/*/")]
    [InlineData("wc -l docs/guide/?.md")]
    [InlineData("cat docs/*.txt")]
    public async Task AnUnquotedGlobWhoseEveryMatchIsInsideTheWorktreeIsAllowedWithoutAsking(string line)
    {
        using var workspace = new TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "docs", "guide"));
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "docs", "a.md"), "a", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "docs", "b.md"), "b", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(workspace.Path, "docs", "guide", "c.md"), "c", TestContext.Current.CancellationToken);
        Directory.CreateSymbolicLink(Path.Combine(workspace.Path, "docs", "shared"), Path.Combine(workspace.Path, "docs", "guide"));

        Assert.Equal(PolicyAnswer.Allow, PermissionPolicy.BuiltIn.Decide(Requested(ItemKind.Command, line).Facts(workspace.Path, Links)).Answer);
    }

    [Theory]
    [InlineData("cat docs/../../*")]
    [InlineData("ls docs/*/../../../*")]
    [InlineData("echo docs/*/../../../*")]
    [InlineData("echo ../*")]
    public void AGlobThatClimbsOutOfTheWorktreeStillAsks(string line)
    {
        using var workspace = new TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "docs", "guide"));

        Assert.Equal(PolicyAnswer.Ask, Decided(workspace.Path, line));
    }

    [Theory]
    [InlineData("cat docs/*")]
    [InlineData("echo docs/*")]
    public void AGlobWhoseMatchIsAHiddenLinkOutOfTheWorktreeStillAsks(string line)
    {
        using var workspace = new TemporaryFolder();
        using var elsewhere = new TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "docs"));
        Directory.CreateSymbolicLink(Path.Combine(workspace.Path, "docs", ".leak"), elsewhere.Path);

        Assert.Equal(PolicyAnswer.Ask, Decided(workspace.Path, line));
    }

    [Theory]
    [InlineData("leak", "cat docs/L*")]
    [InlineData("leak", "cat docs/LEA?")]
    [InlineData("\u212Aey", "cat docs/k*")]
    public void AGlobMatchesNamesInAnyLetterCaseSoALinkOutOfTheWorktreeStillAsks(string link, string line)
    {
        using var workspace = new TemporaryFolder();
        using var elsewhere = new TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "docs"));
        Directory.CreateSymbolicLink(Path.Combine(workspace.Path, "docs", link), elsewhere.Path);

        Assert.Equal(PolicyAnswer.Ask, Decided(workspace.Path, line));
    }

    [Theory]
    [InlineData(4096, PolicyAnswer.Allow)]
    [InlineData(4097, PolicyAnswer.Ask)]
    public void AGlobIsCheckedUpToItsMostMatchesAndAsksBeyondThem(int folders, PolicyAnswer expected)
    {
        using var workspace = new TemporaryFolder();

        foreach (var number in Enumerable.Range(0, folders))
        {
            Directory.CreateDirectory(Path.Combine(workspace.Path, "docs", $"d{number}"));
        }

        Assert.Equal(expected, Decided(workspace.Path, "cat docs/*"));
    }

    [Fact]
    public void AGlobThatWouldListMoreNamesThanCanBeCheckedStillAsks()
    {
        using var workspace = new TemporaryFolder();

        foreach (var number in Enumerable.Range(0, 65537))
        {
            Directory.CreateDirectory(Path.Combine(workspace.Path, "docs", $"d{number}"));
        }

        Assert.Equal(PolicyAnswer.Ask, Decided(workspace.Path, "cat docs/zz*"));
    }

    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void AGlobOverAFolderThatCannotBeListedStillAsks()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows() || Environment.IsPrivilegedProcess, "Only an unprivileged process on Unix is kept out of a folder by its mode.");
        using var workspace = new TemporaryFolder();
        var docs = Path.Combine(workspace.Path, "docs");
        Directory.CreateDirectory(docs);
        File.SetUnixFileMode(docs, UnixFileMode.None);
        var answer = Decided(workspace.Path, "cat docs/*");
        File.SetUnixFileMode(docs, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        Assert.Equal(PolicyAnswer.Ask, answer);
    }

    [Theory]
    [InlineData("*evil", "cat docs/*")]
    [InlineData("*evil", "grep -rn SECRET docs/*")]
    [InlineData("l*ok", "cat docs/l*k")]
    public void AGlobMeetingAWildcardInAFileNameStillSeesTheLinkBehindIt(string link, string line)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows names cannot hold a wildcard.");
        using var workspace = new TemporaryFolder();
        using var elsewhere = new TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "docs"));
        Directory.CreateSymbolicLink(Path.Combine(workspace.Path, "docs", link), elsewhere.Path);

        Assert.Equal(PolicyAnswer.Ask, Decided(workspace.Path, line));
    }

    [Theory]
    [InlineData("cat docs/e?x")]
    [InlineData("cat docs/e????x")]
    public void AGlobMatchingANameOutsideTheBasicPlaneStillSeesTheLinkBehindIt(string line)
    {
        using var workspace = new TemporaryFolder();
        using var elsewhere = new TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "docs"));
        Directory.CreateSymbolicLink(Path.Combine(workspace.Path, "docs", "e\U0001F600x"), elsewhere.Path);

        Assert.Equal(PolicyAnswer.Ask, Decided(workspace.Path, line));
    }

    [Fact]
    public async Task AGlobOverANameThatIsNotValidTextStillAsks()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Only Linux keeps a name that is not valid text.");
        using var workspace = new TemporaryFolder();
        using var elsewhere = new TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "docs"));
        var linked = await ShellAsync(workspace.Path, "ln -s \"$1\" \"$(printf 'docs/bad\\377')\"", elsewhere.Path);
        var answer = Decided(workspace.Path, "cat docs/b*");
        var removed = await ShellAsync(workspace.Path, "rm \"$(printf 'docs/bad\\377')\"", elsewhere.Path);

        Assert.Equal((0, PolicyAnswer.Ask, 0), (linked, answer, removed));
    }

    private static async Task<int> ShellAsync(string folder, string script, string argument)
    {
        using var shell = Process.Start(new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", script, "sh", argument }, WorkingDirectory = folder })!;
        await shell.WaitForExitAsync(TestContext.Current.CancellationToken);

        return shell.ExitCode;
    }

    [Fact]
    public async Task AGlobIntoAReadableFolderIsReadableButNotWritable()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "A Windows path holds a drive colon and backslashes, which no glob runs without asking with.");
        using var workspace = new TemporaryFolder();
        using var projects = new TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(projects.Path, "sibling"));
        await File.WriteAllTextAsync(Path.Combine(projects.Path, "sibling", "a.md"), "a", TestContext.Current.CancellationToken);
        var pattern = Path.Combine(projects.Path, "sibling", "*.md");
        var facts = Requested(ItemKind.Command, $"cat {pattern}").Facts(workspace.Path, Option<string>.None, [projects.Path], Links);
        var located = facts.Locate(pattern);

        Assert.Equal((PolicyAnswer.Allow, false, true), (PermissionPolicy.BuiltIn.Decide(facts).Answer, located.InsideWorkspace, located.Readable));
    }

    private static PolicyAnswer Decided(string workspace, string line) =>
        PermissionPolicy.BuiltIn.Decide(Requested(ItemKind.Command, line).Facts(workspace, Links)).Answer;

    private static PermissionRequested Requested(ItemKind kind, string target) =>
        new(SessionId.New(), TurnId.New(), new ItemId("item"), "Request", kind, target);
}
