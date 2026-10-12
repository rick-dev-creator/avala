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
        "rg --pre ./decode.sh secret",
        "git diff --output=changes.diff",
        "git diff --ext-diff",
        "git push",
        "git -C /tmp status",
        "tree -o tree.txt",
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

    [Fact]
    public void ARepositoryRuleStillDecidesAReadOnlyCommandBeforeTheDefault()
    {
        var policy = PermissionPolicy.With([new(RuleOrigin.Repository, "no reading secrets", ItemKind.Command, "cat *", RuleScope.Anywhere, PolicyAnswer.Deny)]);

        Assert.Equal(PolicyAnswer.Deny, policy.Decide(Command("ls; cat README.md")).Answer);
    }

    private static PermissionRequest Command(string line) =>
        new(ItemKind.Command, line, InsideWorkspace: false)
        {
            Locate = path => new PermissionRequest(ItemKind.FileEdit, path, InsideWorkspace: !path.StartsWith('/') && !path.StartsWith("..", StringComparison.Ordinal)),
        };
}
