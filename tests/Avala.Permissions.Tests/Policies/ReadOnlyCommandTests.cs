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
        "grep -n 'Async$' src",
        "git log --oneline @{u}..",
        "git status -uno",
        "grep -rno TODO src",
        "git log --relative-date -3",
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
    [InlineData("cat .?/.?/.?/.?/.?/.?/.?/.?/.?/.?/etc/passwd")]
    [InlineData("grep -rn PRIVATE src/.*/.*/.*/.*/.*/.*/.*/.*/.*/home")]
    [InlineData("ls .?")]
    public void AWordWhoseDotGlobCanMatchTheParentFolderStillAsks(string line) =>
        Assert.Equal(PolicyAnswer.Ask, PermissionPolicy.BuiltIn.Decide(Command(line)).Answer);

    [Theory]
    [InlineData("git log --format='%h %G?' -5")]
    [InlineData("git show --pretty=%GS HEAD")]
    public void AGitFormatThatVerifiesSignaturesRunsAnotherProgramAndStillAsks(string line) =>
        Assert.Equal(PolicyAnswer.Ask, PermissionPolicy.BuiltIn.Decide(Command(line)).Answer);

    [Theory]
    [InlineData("git diff -U ~/.ssh/id_rsa README.md")]
    [InlineData("git diff --unified /etc/passwd README.md")]
    [InlineData("git diff -U --output=../outside.diff")]
    [InlineData("git log -1 --pretty --output=.git/config")]
    [InlineData("git show --dirstat-by-file --output=.avala/permissions.json")]
    public void AGitOptionWhoseValueIsOptionalDoesNotHideTheWordAfterIt(string line) =>
        Assert.Equal(PolicyAnswer.Ask, PermissionPolicy.BuiltIn.Decide(Command(line)).Answer);

    [Theory]
    [InlineData("git log --format --output=.avala/permissions.json")]
    [InlineData("git show --format --output=.git/config")]
    [InlineData("git log --encoding --output=.git/config")]
    public void AGitFormatWithoutAnEqualsSignDoesNotHideTheWordAfterIt(string line) =>
        Assert.Equal(PolicyAnswer.Ask, PermissionPolicy.BuiltIn.Decide(Command(line)).Answer);

    [Theory]
    [InlineData("git log --until yesterday --oneline")]
    [InlineData("git log --after 2026-01-01 -5")]
    [InlineData("git log --before '2 weeks ago'")]
    [InlineData("git log --since 3.days --oneline")]
    [InlineData("git log --committer rick -5")]
    [InlineData("git log --author rick --oneline")]
    [InlineData("git show --until yesterday --stat HEAD")]
    public void AGitLogFilterWithItsValueAsTheNextWordIsAllowedWithoutAsking(string line) =>
        Assert.Equal(PolicyAnswer.Allow, PermissionPolicy.BuiltIn.Decide(Command(line)).Answer);

    [Fact]
    public void AGitLogFilterTakesTheNextWordAsItsValueEvenWhenItLooksLikeAnOption() =>
        Assert.Equal(PolicyAnswer.Allow, PermissionPolicy.BuiltIn.Decide(Command("git log --committer --output=.git/config")).Answer);

    [Theory]
    [InlineData("git log --committer rick --output=.git/config")]
    [InlineData("git log --until yesterday --output=log.txt")]
    [InlineData("git show --author --output --output=.avala/permissions.json")]
    [InlineData("git log --before")]
    public void AGitLogFilterTakesOnlyTheNextWordAndWhatFollowsItIsStillDecided(string line) =>
        Assert.Equal(PolicyAnswer.Ask, PermissionPolicy.BuiltIn.Decide(Command(line)).Answer);

    [Theory]
    [InlineData("ls src/*.cs")]
    [InlineData("cat src/*.md")]
    [InlineData("grep foo ./*.txt")]
    [InlineData("grep -rn TODO src/*")]
    [InlineData("wc -l src/*.cs")]
    [InlineData("head -n 5 docs/*.md")]
    [InlineData("tail -n 3 logs/*.log")]
    [InlineData("cat src/app?.cs")]
    [InlineData("ls .github/workflows/*.yml")]
    [InlineData("sort data/*.txt")]
    [InlineData("cut -d, -f1 data/*.csv")]
    [InlineData("file docs/*")]
    [InlineData("stat src/*")]
    [InlineData("tree src/*")]
    [InlineData("rg TODO src/*/")]
    [InlineData("ls -- *.cs")]
    [InlineData("grep -n foo -- *.txt")]
    [InlineData("git diff --stat -- src/*.cs")]
    [InlineData("git log --oneline -- src/*.cs")]
    [InlineData("echo src/*")]
    [InlineData("echo *")]
    public void AnUnquotedGlobThatCanOnlyExpandToPathsToReadIsAllowedWithoutAsking(string line) =>
        Assert.Equal(PolicyAnswer.Allow, PermissionPolicy.BuiltIn.Decide(Command(line)).Answer);

    [Theory]
    [InlineData("ls *.cs")]
    [InlineData("grep foo *.txt")]
    [InlineData("cat ?ut")]
    [InlineData("sort -- [-]o names.txt")]
    [InlineData("uniq src/*.txt")]
    [InlineData("uniq -c -- src/*.txt")]
    [InlineData("pwd src/*")]
    [InlineData("grep src/*.cs")]
    [InlineData("grep -- src/*.cs")]
    [InlineData("grep -e src/* README.md")]
    [InlineData("head -n src/* README.md")]
    [InlineData("rg -g src/* TODO")]
    [InlineData("grep --include=src/*.cs TODO src")]
    [InlineData("git log --author src/* --oneline")]
    [InlineData("sort -o src/* names.txt")]
    [InlineData("cat src/**/*.cs")]
    [InlineData("cat src/.*")]
    [InlineData("cat src/*/.?/secret")]
    [InlineData("cat 'src/'*.md")]
    [InlineData("cat src/*(e:touch\\ pwned:)")]
    [InlineData("cat src/*=x")]
    [InlineData("cat ~/*")]
    [InlineData("cat /etc/*")]
    [InlineData("cat ../*")]
    [InlineData("cat < src/*.md")]
    [InlineData("find src/* -name x")]
    [InlineData("git sta* --short")]
    [InlineData("c?t README.md")]
    [InlineData("tr a b /etc/p*")]
    [InlineData("git diff -U*")]
    [InlineData("echo ../*")]
    [InlineData("echo /etc/*")]
    [InlineData("git log --grep -- *.cs")]
    [InlineData("grep -e -- *.txt")]
    [InlineData("sort -k -- *.txt")]
    [InlineData("tree -P src/* .")]
    [InlineData("ls -I src/* src")]
    [InlineData("git log -n src/* --oneline")]
    public void AnUnquotedGlobThatCouldExpandIntoAnOptionAPatternOrAWrittenPathStillAsks(string line) =>
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
