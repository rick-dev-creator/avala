using Avala.Runtime.Containment;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Runtime.Tests.Processes;

public sealed class MacListingTests
{
    private const string Marker = $"{ProcessTreeId.Variable}=tree-1";

    private const int Harness = 500;

    private const string Processes = $"""
          100     1 Ss   /bin/zsh HOME=/Users/dev
          200   100 S    dotnet run HOME=/Users/dev {Marker}
          201   200 S    node server.js
          202   201 S    node worker.js
          203   200 Z    (node)
          300     1 S    /bin/sleep 60
          301   300 S    /bin/cat
          400     1 S    /usr/sbin/unrelated
          500   200 S    harness
        truncated
        """;

    [Fact]
    public void TheMembersAreTheMarkedAndAdoptedProcessesWithTheirLivingDescendantsButNeverTheHarness() =>
        Assert.Equal(
            [200, 201, 202, 300, 301],
            MacListings.Members(Processes, Marker, [300], Harness).Order());

    [Fact]
    public void WithoutAProcessListingThereAreNoMembers() =>
        Assert.Empty(MacListings.Members(Option<string>.None, Marker, [300], Harness));

    [Fact]
    public void EveryListeningPortIsReportedOnceWithTheProcessThatHoldsIt() =>
        Assert.Equal(
            [new Listener(24_000, 812), new Listener(5_173, 913)],
            MacListings.Listeners("p812\nn*:24000\nn127.0.0.1:24000\np913\nn[::1]:5173\n"));

    [Fact]
    public void WithoutAListenerListingThereAreNoListeners() =>
        Assert.Empty(MacListings.Listeners(Option<string>.None));
}
