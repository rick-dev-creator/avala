using Avala.Workspaces.Testing;

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
        Assert.SkipWhen(!realGit && WorkspaceFidelity.Approximated.ContainsKey(name), WorkspaceFidelity.Approximated.GetValueOrDefault(name, string.Empty));
        await using var bench = await WorkspaceBench.CreateAsync(realGit, Cancellation);

        Assert.Empty(await WorkspaceFidelity.Cases[name](bench.Bench, Cancellation));
    }
}
