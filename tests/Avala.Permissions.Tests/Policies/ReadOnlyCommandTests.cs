using Avala.Agents.Contracts.Events;
using Avala.Permissions.Contracts;
using Avala.Permissions.Policies;
using Avala.Sdk;

namespace Avala.Permissions.Tests.Policies;

public sealed class ReadOnlyCommandTests
{
    public static TheoryData<string> ReadOnly =>
    [
        "ls -la src",
        "cat README.md",
        "head -n 20 src/app.cs",
        "tail -5 build.log",
        "grep -rn TODO src",
        "grep -rn /api src",
        "egrep -c 'a|b' notes.txt",
        "rg --type cs Greeter",
        "find . -name '*.cs' -newer README.md",
        "wc -l src/*.cs",
        "git status --short",
        "git log --oneline -5",
        "git diff HEAD~1 -- src",
        "git show --stat HEAD",
        "pwd",
        "echo done",
        "file README.md",
        "stat src",
        "tree -L 2",
        "sort -u names.txt",
        "uniq -c names.txt",
        "cut -d, -f1 data.csv",
        "tr a-z A-Z",
        "ls 2>/dev/null",
        "ls -la src | head -5; grep -rn TODO . | wc -l",
        "git status && git diff --stat 2>&1 | tail -3",
        "git log --oneline -n 20",
        "git log --format='%h %s' -10",
        "git diff --name-only HEAD~1",
        "git diff --stat",
        "rg -n --glob '*.cs' foo",
        "rg -l TODO src",
        "grep -rn --include='*.cs' TODO src",
        "grep -e /api -rn src",
        "head -n 50 README.md",
        "cat -n src/app.cs",
        "wc -l README.md",
        "find . -name '*.cs' -type f",
        "find src -maxdepth 2 -type d -newermt 2026-01-01",
        "uniq -c -",
        "git --no-pager log --oneline -5",
        "grep -rIn TODO src",
        "git log -S Rounding --oneline",
        "grep -rn '[Tt]odo' src",
    ];

    public static TheoryData<string> NotReadOnly =>
    [
        "find . -name x -exec cat {} ';'",
        "find . -execdir ls ';'",
        "find . -ok ls ';'",
        "find . -name '*.tmp' -delete",
        "find . -fprint found.txt",
        "find . -fprintf found.txt %p",
        "ls > files.txt",
        "grep -rn x src >> found.txt",
        "ls | tee files.txt",
        "cat /etc/passwd",
        "cat ../secret.txt",
        "ls ~",
        "cat $HOME/.ssh/id_rsa",
        "grep -f /etc/patterns src",
        "grep --file=/etc/patterns src",
        "sort -o sorted.txt names.txt",
        "sort -uo sorted.txt names.txt",
        "uniq names.txt unique.txt",
        "uniq - unique.txt",
        "rg --pre ./decode.sh secret",
        "rg --hostname-bin=./host.sh --hyperlink-format=default TODO",
        "git diff --output=changes.diff",
        "git diff --ext-diff",
        "git push",
        "git -C /tmp status",
        "tree -o tree.txt",
        "tree -R -H . -L 1",
        "file -C -m magic",
        "cd src && ls",
        "ls; npm install",
        "echo $(date)",
        "env ls",
        "/bin/ls",
    ];

    [Theory]
    [MemberData(nameof(ReadOnly))]
    public void AReadOnlyLineInsideTheWorktreeIsAllowedWithoutAsking(string line)
    {
        var verdict = PermissionPolicy.BuiltIn.Decide(Command(line));

        Assert.Equal((PolicyAnswer.Allow, Option<string>.Some("read-only allowed")), (verdict.Answer, verdict.Rule.Map(rule => rule.Name)));
    }

    [Theory]
    [MemberData(nameof(NotReadOnly))]
    public void ALineThatWritesRunsSomethingElseOrReadsOutsideTheWorktreeStillAsks(string line) =>
        Assert.Equal(PolicyAnswer.Ask, PermissionPolicy.BuiltIn.Decide(Command(line)).Answer);

    [Theory]
    [InlineData("ls --some-new-option")]
    [InlineData("head --lin=5 README.md")]
    [InlineData("sort --files0-from=list.txt")]
    [InlineData("sort '-o' out.txt names.txt")]
    [InlineData("sort \"-ofoo\" names.txt")]
    [InlineData("sort *")]
    [InlineData("cat [-]o")]
    [InlineData("grep --exclude-from=patterns.txt TODO src")]
    [InlineData("git log --output=log.txt")]
    [InlineData("git diff --textconv")]
    [InlineData("rg --search-zip TODO")]
    [InlineData("file -zS archive.lz")]
    [InlineData("tail -f build.log")]
    [InlineData("find . -name x -newer /etc/passwd")]
    [InlineData("find . -fls found.txt")]
    public void AnOptionOutsideWhatAReadOnlyProgramIsKnownToReadWithIsNotAllowedWithoutAsking(string line) =>
        Assert.Equal(PolicyAnswer.Ask, PermissionPolicy.BuiltIn.Decide(Command(line)).Answer);

    [Theory]
    [InlineData("find {.,-exec,touch,pwned,';'}")]
    [InlineData("find . {-delete,}")]
    [InlineData("sort {-o,sorted.txt} names.txt")]
    [InlineData("git diff {--output=changes.diff,HEAD}")]
    [InlineData("cat {/etc/passwd,README.md}")]
    public void AWordWhoseBracesCouldExpandIntoAnOptionOrAPathOutsideStillAsks(string line) =>
        Assert.Equal(PolicyAnswer.Ask, PermissionPolicy.BuiltIn.Decide(Command(line)).Answer);

    [Theory]
    [InlineData("cat < /etc/passwd")]
    [InlineData("head -n 5 < ../secret.txt")]
    [InlineData("wc -l 0</etc/shadow")]
    public void AnInputRedirectedFromOutsideTheWorktreeStillAsks(string line) =>
        Assert.Equal(PolicyAnswer.Ask, PermissionPolicy.BuiltIn.Decide(Command(line)).Answer);

    [Theory]
    [InlineData("rg --files /etc")]
    [InlineData("rg --files ../other")]
    public void ListingTheFilesOfAFolderOutsideTheWorktreeWithRgStillAsks(string line) =>
        Assert.Equal(PolicyAnswer.Ask, PermissionPolicy.BuiltIn.Decide(Command(line)).Answer);

    [Fact]
    public void ARepositoryRuleStillDecidesAReadOnlyCommandBeforeTheDefault()
    {
        var policy = PermissionPolicy.With([new(RuleOrigin.Repository, "no reading secrets", ItemKind.Command, "cat *", RuleScope.Anywhere, PolicyAnswer.Deny)]);

        Assert.Equal(PolicyAnswer.Deny, policy.Decide(Command("ls; cat README.md")).Answer);
    }

    [Fact]
    public void AReadOnlyLineReadingAReadableFolderIsAllowedWithoutAsking()
    {
        var line = new PermissionRequest(ItemKind.Command, "cat /projects/sibling/README.md", InsideWorkspace: false)
        {
            Locate = path => new PermissionRequest(ItemKind.FileEdit, path, InsideWorkspace: false) { Readable = path.StartsWith("/projects/", StringComparison.Ordinal) },
        };

        Assert.Equal(PolicyAnswer.Allow, PermissionPolicy.BuiltIn.Decide(line).Answer);
    }

    private static PermissionRequest Command(string line) =>
        new(ItemKind.Command, line, InsideWorkspace: false)
        {
            Locate = path => new PermissionRequest(ItemKind.FileEdit, path, InsideWorkspace: !path.StartsWith('/') && !path.StartsWith("..", StringComparison.Ordinal)),
        };
}
