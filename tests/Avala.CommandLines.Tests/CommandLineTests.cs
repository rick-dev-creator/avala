using Avala.CommandLines;

namespace Avala.CommandLines.Tests;

public sealed class CommandLineTests
{
    public static TheoryData<string, string[], string[], bool> Lines => new()
    {
        { "git   status  --short", ["git status --short"], [], false },
        { "ls -la; git show --stat HEAD", ["ls -la", "git show --stat HEAD"], [], false },
        { "a && b || c | d |& e & f", ["a", "b", "c", "d", "e", "f"], [], false },
        { "ls\ngit status\r", ["ls", "git status\r"], [], false },
        { "ls \\\n  -la", ["ls -la"], [], true },
        { "echo \"a; b\" 'c && d' e\\ f", ["echo \"a; b\" 'c && d' e\\ f"], [], false },
        { "echo e\\;f", ["echo e\\;f"], [], true },
        { "echo \"a\\\"; rm x; \\\"b\"", ["echo \"a\\\"; rm x; \\\"b\""], [], true },
        { "ls <# ; #> ; rm x", ["ls"], [], true },
        { "ls # ; rm -rf ~", ["ls"], [], false },
        { "echo a#b", ["echo a#b"], [], false },
        { "(cd src && ls)", ["cd src", "ls"], [], false },
        { "ls > out.txt 2>&1 >>log.txt &>/dev/null 2>/dev/null < in.txt", ["ls"], ["out.txt", "log.txt"], false },
        { "ls >&all.txt 3<>rw.txt", ["ls"], ["all.txt", "rw.txt"], false },
        { "cat > notes.md <<'EOF'\n$(rm -rf ~); rm -rf ~\nEOF\nls", ["cat", "ls"], ["notes.md"], false },
        { "cat <<-EOF\n\tplain $HOME\n\tEOF\nls", ["cat", "ls"], [], false },
        { "cat <<EOF\n$(rm -rf ~)\nEOF", ["cat"], [], true },
        { "cat <<EOF\nno end", ["cat"], [], true },
        { "cat <<<\"$HOME\"", ["cat"], [], false },
        { "echo $(ls; rm -rf ~)", ["ls", "rm -rf ~", "echo $(ls; rm -rf ~)"], [], true },
        { "echo \"`rm -rf ~`\"", ["rm -rf ~", "echo \"`rm -rf ~`\""], [], true },
        { "diff <(ls) >(rm x)", ["ls", "rm x", "diff"], [], true },
        { "echo ${HOME} $1 \"$@\"", ["echo ${HOME} $1 \"$@\""], [], false },
        { "echo ${X:-y}", ["echo ${X:-y}"], [], true },
        { "echo ${HOME", ["echo ${HOME"], [], true },
        { "echo ${}", ["echo ${}"], [], true },
        { "echo $'a\\'; b' c", ["echo $'a\\'; b' c"], [], false },
        { "echo $'unbalanced", ["echo $'unbalanced"], [], true },
        { "echo \"$'x; y'\"", ["echo \"$'x; y'\""], [], false },
        { "eval 'rm -rf ~'", ["eval 'rm -rf ~'", "rm -rf ~"], [], true },
        { "trap 'rm -rf ~' EXIT", ["trap 'rm -rf ~' EXIT", "rm -rf ~ EXIT"], [], true },
        { "sudo -u root rm -rf /", ["sudo -u root rm -rf /", "rm -rf /"], [], true },
        { "FOO=1 env -i BAR=2 /bin/rm -rf x", ["FOO=1 env -i BAR=2 /bin/rm -rf x", "env -i BAR=2 /bin/rm -rf x", "BAR=2 /bin/rm -rf x", "/bin/rm -rf x", "rm -rf x"], [], false },
        { "xargs -n 1 -I{} rm {}", ["xargs -n 1 -I{} rm {}", "rm {}"], [], false },
        { "timeout -s KILL 5 nice -n 2 nohup time ls", ["timeout -s KILL 5 nice -n 2 nohup time ls", "nice -n 2 nohup time ls", "nohup time ls", "time ls", "ls"], [], false },
        { "bash -lc 'ls; echo x > out.txt'", ["bash -lc 'ls; echo x > out.txt'", "ls", "echo x"], ["out.txt"], false },
        { "bash -o pipefail -c 'rm -rf ~'", ["bash -o pipefail -c 'rm -rf ~'", "rm -rf ~"], [], false },
        { "sh -c \"$CMD\"", ["sh -c \"$CMD\""], [], true },
        { "bash --rcfile x -c ls", ["bash --rcfile x -c ls"], [], true },
        { "bash script.sh", ["bash script.sh"], [], false },
        { "find . -name '*.tmp' -exec rm -f {} ';' -execdir grep x {} +", ["find . -name '*.tmp' -exec rm -f {} ';' -execdir grep x {} +", "rm -f {}", "grep x {}"], [], false },
        { "find . -exec grep x {} \\;", ["find . -exec grep x {} \\;", "grep x {}"], [], true },
        { "echo 'unbalanced", ["echo 'unbalanced"], [], true },
        { "echo \"unbalanced", ["echo \"unbalanced"], [], true },
        { "ls \\", ["ls"], [], true },
        { "$CMD -la", ["$CMD -la"], [], true },
        { "{rm,-rf,~}", ["{rm,-rf,~}"], [], true },
        { "~/bin/tool", ["~/bin/tool"], [], true },
        { "for f in *; do ls $f; done", ["for f in *", "do ls $f", "done"], [], true },
        { "f() { ls; }", ["f", "{ ls", "}"], [], true },
        { "ls )", ["ls"], [], true },
        { "echo x > $OUT", ["echo x"], [], true },
        { "echo x >", ["echo x"], [], true },
        { "cd /tmp && echo x > notes.txt", ["cd /tmp", "echo x"], ["notes.txt"], false },
        { "cd $DIR && echo x > notes.txt", ["cd $DIR", "echo x"], ["notes.txt"], true },
        { "cd /tmp && echo x > /tmp/notes.txt", ["cd /tmp", "echo x"], ["/tmp/notes.txt"], false },
        { "> empty.txt", [], ["empty.txt"], false },
        { "", [], [], true },
        { " # only a comment", [], [], true },
    };

    [Theory]
    [MemberData(nameof(Lines))]
    public void ALineIsSplitIntoItsSimpleCommandsItsWritesAndWhatCannotBeAnalysed(string text, string[] commands, string[] writes, bool opaque)
    {
        var line = CommandLine.Parse(text);

        Assert.Equal(commands, line.Commands.Select(command => command.Text));
        Assert.Equal(writes, line.Writes);
        Assert.Equal(opaque, line.Opaque);
    }

    public static TheoryData<string, string[], string[]> Landings => new()
    {
        { "echo x > a.txt", ["a.txt"], [] },
        { "cd src && echo x > a.txt", ["a.txt", "src/a.txt"], [] },
        { "cd /work/src && cat > Pid.cs <<'EOF'\nclass Pid;\nEOF", ["Pid.cs", "/work/src/Pid.cs"], [] },
        { "echo x > a.txt; cd /tmp", ["a.txt"], [] },
        { "cd src; cd ../docs && echo x > a.txt", ["a.txt", "src/a.txt", "../docs/a.txt", "src/../docs/a.txt"], [] },
        { "(cd /tmp && echo x > a.txt)", ["a.txt", "/tmp/a.txt"], [] },
        { "cd src && echo x > /tmp/a.txt", ["/tmp/a.txt"], [] },
        { "cd src && bash -c 'echo x > a.txt'", ["a.txt", "src/a.txt"], [] },
        { "bash -c 'cd /tmp' && echo x > a.txt", [], ["a.txt"] },
        { "cd $DIR && echo x > a.txt", [], ["a.txt"] },
        { "cd && echo x > a.txt", [], ["a.txt"] },
        { "cd - && echo x > a.txt", [], ["a.txt"] },
        { "pushd src && echo x > a.txt", [], ["a.txt"] },
        { "env -C src echo x > a.txt", ["a.txt"], [] },
        { "env -C src true && echo x > a.txt", [], ["a.txt"] },
    };

    [Theory]
    [MemberData(nameof(Landings))]
    public void AWriteLandsInEveryFolderACdBeforeItMayHaveReachedAndIsAdriftWhereTheFolderIsUnknown(string text, string[] landings, string[] adrift)
    {
        var line = CommandLine.Parse(text);

        Assert.Equal(landings, line.Landings);
        Assert.Equal(adrift, line.Adrift);
    }

    [Fact]
    public void ALineThatChangesDirectoryInManyWaysLeavesItsWritesAdrift()
    {
        var line = CommandLine.Parse(string.Concat(Enumerable.Range(0, 5).Select(step => $"cd d{step}; ")) + "echo x > a.txt");

        Assert.Empty(line.Landings);
        Assert.Equal(["a.txt"], line.Adrift);
    }

    [Fact]
    public void EachCommandKeepsItsUnquotedWordsAndItsOwnWritesAndTheRestatementsARuleMayNameAreMarked()
    {
        var line = CommandLine.Parse("FOO=1 sudo /bin/rm x > log.txt; echo 'a b' $(ls > inner.txt) > out.txt");

        Assert.Equal(
            [
                ("FOO=1 sudo /bin/rm x", "FOO=1|sudo|/bin/rm|x", "log.txt", false),
                ("sudo /bin/rm x", "sudo|/bin/rm|x", "", true),
                ("/bin/rm x", "/bin/rm|x", "", false),
                ("rm x", "rm|x", "", true),
                ("ls", "ls", "inner.txt", false),
                ("echo 'a b' $(ls > inner.txt)", "echo|a b|$(ls > inner.txt)", "out.txt", false),
            ],
            line.Commands.Select(command => (command.Text, string.Join('|', command.Words), string.Join('|', command.Writes), command.Restated)));
        Assert.Equal(["log.txt", "inner.txt", "out.txt"], line.Writes);
    }
}
