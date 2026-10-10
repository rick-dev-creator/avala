using Avala.Delegation.Contracts;
using Avala.Delegation.MachineFiles;
using Avala.Delegation.Policy;
using Avala.Delegation.RepositoryFiles;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Delegation.Tests.RepositoryFiles;

public sealed class MachineDelegationTests
{
    private const string Worktree = "/worktrees/1";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("""{ "escalation": "parent", "parentWindowSeconds": 45 }""", true, 45)]
    [InlineData("""{ "escalation": "parent", "harness": "codex" }""", false, 0)]
    [InlineData("not json", false, 0)]
    [InlineData(null, false, 0)]
    public async Task ARepositoryThatDeclaresNoEscalationUsesTheMachinesValidFileElseTheBuiltInsAsync(string? machineFile, bool asksParent, int seconds)
    {
        await using var data = new TemporaryFolder();
        var paths = new AvalaPaths(data.Path);

        if (machineFile is not null)
        {
            Directory.CreateDirectory(paths.Data);
            await File.WriteAllTextAsync(Path.Combine(paths.Data, MachineDelegation.FileName), machineFile, Cancellation);
        }

        var committed = new CommittedFiles().With(Worktree, DelegationRulesReader.JobFile, """{ "delegation": {} }""");
        var declared = new CommittedFiles().With(Worktree, DelegationRulesReader.JobFile, """{ "delegation": { "escalation": "human" } }""");
        var machine = new MachineDelegation(paths);

        var rules = Outcomes.Present(Outcomes.Succeeds(await new DelegationRulesReader(committed, machine).OfWorktreeAsync(Worktree, Cancellation)));
        var human = Outcomes.Present(Outcomes.Succeeds(await new DelegationRulesReader(declared, machine).OfWorktreeAsync(Worktree, Cancellation)));

        Assert.Equal(
            asksParent ? Option<ParentEscalation>.Some(new ParentEscalation(TimeSpan.FromSeconds(seconds))) : Option<ParentEscalation>.None,
            rules.Escalation.Kept);
        Assert.Equal(Option<ParentEscalation>.None, human.Escalation.Kept);
    }
}
