using Avala.Runtime;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
using Avala.Workspaces.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workspaces.Tests.Conformance;

public sealed class WorkspaceFidelityTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string, bool> Cases =>
        [.. WorkspaceFidelity.Cases.Keys.SelectMany(name => new[] { (name, true), (name, false) })];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task TheInMemoryWorkspacesAnswerAsTheModuleWithGitDoesAsync(string name, bool realGit)
    {
        Assert.SkipWhen(WorkspaceFidelity.Approximated.ContainsKey(name), WorkspaceFidelity.Approximated.GetValueOrDefault(name, string.Empty));
        Assert.SkipWhen(
            realGit && name == "InitializingAFolderLeavesItsFilesAsTheyWereAsync" && !await HasGitIdentityAsync(),
            "The module with git initializes a folder only for a git identity, and this machine has no user.name or user.email.");
        await using var bench = await WorkspaceBench.CreateAsync(realGit, Cancellation);

        Assert.Empty(await WorkspaceFidelity.Cases[name](bench.Bench, Cancellation));
    }

    private static async Task<bool> HasGitIdentityAsync()
    {
        await using var data = new TemporaryFolder();
        var processes = new ServiceCollection().AddRuntime(new AvalaPaths(data.Path)).BuildServiceProvider().GetRequiredService<IProcessRunner>();

        foreach (var key in new[] { "user.name", "user.email" })
        {
            var found = await processes.RunAsync(new ProcessRequest("git", ["config", "--get", key]), Cancellation);

            if (!found.Match(outcome => outcome.Succeeded && outcome.Output.Trim().Length > 0, _ => false))
            {
                return false;
            }
        }

        return true;
    }
}
