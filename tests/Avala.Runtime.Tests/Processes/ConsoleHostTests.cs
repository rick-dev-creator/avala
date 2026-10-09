using Avala.Runtime.Processes;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Tests.Processes;

public sealed class ConsoleHostTests
{
    private static readonly TreeProcess Server = new(41, "dotnet", 2_048, TimeSpan.FromSeconds(3));
    private static readonly TreeProcess Build = new(42, "dotnet", 4_096, TimeSpan.FromSeconds(5));

    [Fact]
    public void AConsoleHostIsCountedInItsClientInsteadOfListedOnItsOwn()
    {
        var host = new TreeProcess(43, "conhost", 512, TimeSpan.FromSeconds(1));

        var members = ConsoleHosts.Attributed([Server, host, Build], Parents((43, 41)));

        Assert.Equal([Server with { MemoryBytes = 2_560, CpuTime = TimeSpan.FromSeconds(4) }, Build], members);
    }

    [Fact]
    public void AConsoleHostWhoseClientIsNotAMemberIsNotListed()
    {
        var orphaned = new TreeProcess(43, "ConHost", 512, TimeSpan.FromSeconds(1));
        var unknown = new TreeProcess(44, "conhost", 512, TimeSpan.FromSeconds(1));

        var members = ConsoleHosts.Attributed([orphaned, unknown, Server], Parents((43, 7)));

        Assert.Equal([Server], members);
    }

    private static Func<int, Option<int>> Parents(params (int Child, int Parent)[] parents)
    {
        var known = parents.ToDictionary(pair => pair.Child, pair => pair.Parent);

        return child => known.TryGetValue(child, out var parent) ? parent : Option<int>.None;
    }
}
