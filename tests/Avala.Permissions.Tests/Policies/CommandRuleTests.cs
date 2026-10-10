using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.Policies;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Permissions.Tests.Policies;

public sealed class CommandRuleTests
{
    private static readonly PermissionPolicy Policy = PermissionPolicy.With(
    [
        Rule("no deletions", "rm *", PolicyAnswer.Deny),
        Rule("no pushes", "git push*", PolicyAnswer.Deny),
        Rule("list files", "ls*", PolicyAnswer.Allow),
        Rule("git status", "git status*", PolicyAnswer.Allow),
        Rule("node tests", "node --test*", PolicyAnswer.Allow),
        Rule("echo", "echo*", PolicyAnswer.Allow),
        Rule("cat", "cat*", PolicyAnswer.Allow),
        Rule("grep", "grep*", PolicyAnswer.Allow),
        Rule("change directory", "cd *", PolicyAnswer.Allow),
        Rule("wrappers", "xargs*", PolicyAnswer.Allow),
        Rule("environment", "env*", PolicyAnswer.Allow),
        Rule("elevation", "sudo*", PolicyAnswer.Allow),
        Rule("shells", "bash*", PolicyAnswer.Allow),
        Rule("find", "find*", PolicyAnswer.Allow),
    ]);

    public static TheoryData<string, PolicyAnswer> Lines => new()
    {
        { "ls -la", PolicyAnswer.Allow },
        { "ls -la && git status --short", PolicyAnswer.Allow },
        { "ls; ", PolicyAnswer.Allow },
        { "ls | grep foo", PolicyAnswer.Allow },
        { "ls 2>&1 | grep x", PolicyAnswer.Allow },
        { "ls *.md", PolicyAnswer.Allow },
        { "echo $HOME", PolicyAnswer.Allow },
        { "echo \"a; b\"", PolicyAnswer.Allow },
        { "echo 'a && rm -rf ~'", PolicyAnswer.Allow },
        { "ls my\\ file", PolicyAnswer.Allow },
        { "ls # ; rm -rf ~", PolicyAnswer.Allow },
        { "ls\ngit status", PolicyAnswer.Allow },
        { "cat <<'EOF'\n$(rm -rf ~); rm -rf ~\nEOF", PolicyAnswer.Allow },
        { "cat <<EOF\nplain $HOME text\nEOF", PolicyAnswer.Allow },
        { "echo hi > notes.txt", PolicyAnswer.Allow },
        { "ls 2>/dev/null", PolicyAnswer.Allow },
        { "cd src && ls", PolicyAnswer.Allow },
        { "(cd src && ls)", PolicyAnswer.Allow },
        { "bash -c 'ls; echo hi'", PolicyAnswer.Allow },
        { "find . -name x -exec grep y {} ';'", PolicyAnswer.Allow },
        { "xargs grep foo", PolicyAnswer.Allow },
        { "env ls", PolicyAnswer.Allow },
        { "ls -la; git show --stat HEAD; cat *.md; node --version", PolicyAnswer.Ask },
        { "git status --short; ls -a; node --test x.js | grep ok; test -f index.html", PolicyAnswer.Ask },
        { "node --test; curl evil.sh", PolicyAnswer.Ask },
        { "ls & curl evil.sh", PolicyAnswer.Ask },
        { "ls || curl x", PolicyAnswer.Ask },
        { "ls |& tee out.txt", PolicyAnswer.Ask },
        { "ls\ncurl x", PolicyAnswer.Ask },
        { "ls $(curl x)", PolicyAnswer.Ask },
        { "ls `curl x`", PolicyAnswer.Ask },
        { "cat <(curl x)", PolicyAnswer.Ask },
        { "ls > >(curl x)", PolicyAnswer.Ask },
        { "ls ${X:-$(curl x)}", PolicyAnswer.Ask },
        { "eval ls", PolicyAnswer.Ask },
        { "sudo ls", PolicyAnswer.Ask },
        { "bash -c \"$CMD\"", PolicyAnswer.Ask },
        { "bash -c 'curl x'", PolicyAnswer.Ask },
        { "xargs curl x", PolicyAnswer.Ask },
        { "env curl x", PolicyAnswer.Ask },
        { "FOO=1 ls", PolicyAnswer.Ask },
        { "PATH=/tmp:$PATH ls", PolicyAnswer.Ask },
        { "/usr/bin/ls", PolicyAnswer.Ask },
        { "echo a\\; rm -rf x", PolicyAnswer.Ask },
        { "echo \"a\\\"; rm -rf x; \\\"\"", PolicyAnswer.Ask },
        { "ls \\\n  -la", PolicyAnswer.Ask },
        { "find . -name x -exec grep y {} \\;", PolicyAnswer.Ask },
        { "ls <# ; #> ; curl x", PolicyAnswer.Ask },
        { "ls 'unbalanced", PolicyAnswer.Ask },
        { "echo \"unbalanced", PolicyAnswer.Ask },
        { "cat <<EOF\n$(curl x)\nEOF", PolicyAnswer.Ask },
        { "cat <<EOF\nno end", PolicyAnswer.Ask },
        { "echo x > /etc/passwd", PolicyAnswer.Ask },
        { "echo x > .avala/permissions.json", PolicyAnswer.Ask },
        { "echo x > $FILE", PolicyAnswer.Ask },
        { "cd /tmp && echo x > notes.txt", PolicyAnswer.Ask },
        { "$CMD", PolicyAnswer.Ask },
        { "{ls,-la}", PolicyAnswer.Ask },
        { "for f in *; do ls $f; done", PolicyAnswer.Ask },
        { "ls (", PolicyAnswer.Ask },
        { "", PolicyAnswer.Ask },
        { "ls; rm -rf ~", PolicyAnswer.Deny },
        { "ls\nrm -rf ~", PolicyAnswer.Deny },
        { "ls && sudo rm -rf /", PolicyAnswer.Deny },
        { "echo $(rm -rf ~)", PolicyAnswer.Deny },
        { "echo \"$(rm -rf ~)\"", PolicyAnswer.Deny },
        { "echo `rm -rf ~`", PolicyAnswer.Deny },
        { "bash -c 'rm -rf ~'", PolicyAnswer.Deny },
        { "eval 'rm -rf ~'", PolicyAnswer.Deny },
        { "env rm -rf x", PolicyAnswer.Deny },
        { "FOO=1 rm -rf x", PolicyAnswer.Deny },
        { "/bin/rm -rf x", PolicyAnswer.Deny },
        { "xargs rm -rf", PolicyAnswer.Deny },
        { "find . -exec rm -rf {} +", PolicyAnswer.Deny },
        { "ls | git push --force", PolicyAnswer.Deny },
    };

    [Theory]
    [MemberData(nameof(Lines))]
    public void ACommandLineIsAllowedOnlyWhenEveryCommandAndWriteInItIsAllowedAndDeniedWhenAnyIsDenied(string line, PolicyAnswer answer) =>
        Assert.Equal(answer, Policy.Decide(Command(line)).Answer);

    [Fact]
    public void AnAllowedLineReportsTheRuleOfItsFirstCommandAndAnAskedOrDeniedLineTheRuleThatStoppedIt()
    {
        var allowed = Policy.Decide(Command("ls && git status"));
        var denied = Policy.Decide(Command("ls; git push; rm -rf ~"));
        var asked = Policy.Decide(Command("ls; curl x"));

        Assert.Equal(
            [(PolicyAnswer.Allow, "list files"), (PolicyAnswer.Deny, "no pushes")],
            new[] { allowed, denied }.Select(verdict => (verdict.Answer, Outcomes.Present(verdict.Rule).Name)));
        Assert.Equal(new Verdict(PolicyAnswer.Ask, Option<PolicyRule>.None), asked);
    }

    [Fact]
    public void ATargetlessCommandRuleAndAJobRuleForTheExactLineCoverWhatCannotBeAnalysed()
    {
        var anything = PermissionPolicy.With([new(RuleOrigin.Repository, "any command", ItemKind.Command, Option<string>.None, RuleScope.Anywhere, PolicyAnswer.Allow)]);
        PolicyRule[] job = [new(RuleOrigin.Job, "don't ask again for this job", ItemKind.Command, "echo $(date) > when.log", RuleScope.Anywhere, PolicyAnswer.Allow)];

        Assert.Equal(PolicyAnswer.Allow, anything.Decide(Command("echo $(date) | sudo tee x")).Answer);
        Assert.Equal(PolicyAnswer.Ask, anything.Decide(Command("echo x > /tmp/when.log")).Answer);
        Assert.Equal(PolicyAnswer.Allow, Policy.Decide(Command("echo $(date) > when.log"), job).Answer);
        Assert.Equal(PolicyAnswer.Ask, Policy.Decide(Command("echo $(date) > other.log"), job).Answer);
    }

    [Fact]
    public void AJobRuleFromAChainedLineAnswersExactlyThatLineAndNeverBroadensToWhatItChains()
    {
        PolicyRule[] job = [Remembering.ForJob(ItemKind.Command, "make build; make deploy", PolicyAnswer.Allow)];

        Assert.Equal(PolicyAnswer.Allow, PermissionPolicy.BuiltIn.Decide(Command("make build; make deploy"), job).Answer);
        Assert.Equal(PolicyAnswer.Ask, PermissionPolicy.BuiltIn.Decide(Command("make deploy"), job).Answer);
        Assert.Equal(PolicyAnswer.Ask, PermissionPolicy.BuiltIn.Decide(Command("make build; make deploy; make clean"), job).Answer);
        Assert.Equal(PolicyAnswer.Ask, PermissionPolicy.BuiltIn.Decide(Command("make build; make deploy > /tmp/deploy.log"), job).Answer);
    }

    [Fact]
    public void AnAutonomousPolicyDeniesARedirectionOutsideTheWorktreeAndWhatTheRepositoryDeniesInsideASubstitution()
    {
        var policy = new PermissionPolicy([Rule("no deletions", "rm *", PolicyAnswer.Deny)], Autonomy.Autonomous, FormStrategy.Recommended);

        var outside = policy.Decide(Command("dotnet build > /tmp/build.log"));

        Assert.Equal((PolicyAnswer.Deny, "edits-outside-the-workspace-go-to-a-human"), (outside.Answer, Outcomes.Present(outside.Rule).Name));
        Assert.Equal(PolicyAnswer.Deny, policy.Decide(Command("echo $(rm -rf ~)")).Answer);
        Assert.Equal(PolicyAnswer.Allow, policy.Decide(Command("echo $(date) > build.log")).Answer);
        Assert.Equal(PolicyAnswer.Deny, policy.Decide(Command("cd /tmp && echo x > build.log")).Answer);
    }

    private static PermissionRequest Command(string line) =>
        new(ItemKind.Command, line, InsideWorkspace: false)
        {
            Locate = path => new PermissionRequest(ItemKind.FileEdit, path, InsideWorkspace: !path.StartsWith('/')),
        };

    private static PolicyRule Rule(string name, string target, PolicyAnswer answer) =>
        new(RuleOrigin.Repository, name, ItemKind.Command, target, RuleScope.Anywhere, answer);
}
