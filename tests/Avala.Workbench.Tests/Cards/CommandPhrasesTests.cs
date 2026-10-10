using Avala.Agents.Contracts.Events;
using Avala.Workbench.Presenting;

namespace Avala.Workbench.Tests.Cards;

public sealed class CommandPhrasesTests
{
    private const string Smoke = """
        set -o pipefail; node --test --test-reporter=tap | grep -E '^# (pass|fail)'; test -f index.html && echo "page exists" && node --check app.js && echo "app.js syntax ok"
        cat > /tmp/claude-1000/smoke-app.js <<'EOF'
        const fs=require('fs'),vm=require('vm');
        console.log('a; b | c > d');
        EOF
        node /tmp/claude-1000/smoke-app.js "$PWD"; rm /tmp/claude-1000/smoke-app.js
        """;

    [Fact]
    public void AMultiLineCommandIsTitledByEverySimpleCommandItRunsWithItsWritesNotByItsFirstLine() =>
        Assert.Equal(
            "Run 10 commands: set, node, grep, test, echo, cat > /tmp/claude-1000/smoke-app.js, rm",
            CommandPhrases.Title(ItemKind.Command, "Run set -o pipefail; node --test …", Smoke));

    [Fact]
    public void AScriptOfManyCommandsListsThemUpTo120CharactersCutAtAWholeCommandAndSaysMoreFollow()
    {
        var script = string.Join("\n", Enumerable.Range(1, 20).Select(index => $"echo {index} > notes-{index}.md"));

        var title = CommandPhrases.Title(ItemKind.Command, "Run echo 1 > notes-1.md", script);

        Assert.Equal(
            "Run 20 commands: echo > notes-1.md, echo > notes-2.md, echo > notes-3.md, echo > notes-4.md, echo > notes-5.md, echo > notes-6.md, …",
            title);
    }

    [Fact]
    public void TheWritesOfACommandAreTheOnesItsLineIsJudgedOnEachShownOnce()
    {
        Assert.Equal(["/tmp/claude-1000/smoke-app.js"], CommandPhrases.Writes(ItemKind.Command, Smoke));
        Assert.Equal(["log.txt"], CommandPhrases.Writes(ItemKind.Command, "make build > log.txt 2>&1 && echo ok >> log.txt"));
    }

    [Fact]
    public void AShortSingleLineCommandKeepsTheTitleItCameWithForItAlreadyShowsAllOfIt() =>
        Assert.Equal(
            "Run for f in .avala/*; do cat \"$f\"; done",
            CommandPhrases.Title(ItemKind.Command, "Run for f in .avala/*; do cat \"$f\"; done", "for f in .avala/*; do cat \"$f\"; done"));

    [Fact]
    public void ALongSingleCommandIsTitledByItsFirstLineAndHowManyLinesFollow() =>
        Assert.Equal(
            "Run cat > notes.md <<'EOF' +3 lines",
            CommandPhrases.Title(ItemKind.Command, "Run cat > notes.md <<'EOF'", "cat > notes.md <<'EOF'\n# Notes\nshort\nEOF"));

    [Fact]
    public void OneLineCutsALongFirstLineAndCountsTheLinesAfterIt()
    {
        var shown = CommandPhrases.OneLine($"{new string('x', 120)}\nsecond\nthird");

        Assert.Equal($"{new string('x', 55)}… +2 lines", shown);
    }

    [Fact]
    public void AnythingButACommandKeepsItsTitleAndWritesNothing()
    {
        Assert.Equal("Edit src/app.js", CommandPhrases.Title(ItemKind.FileEdit, "Edit src/app.js", "src/app.js\n> not a redirect"));
        Assert.Empty(CommandPhrases.Writes(ItemKind.FileEdit, "a > b"));
    }
}
