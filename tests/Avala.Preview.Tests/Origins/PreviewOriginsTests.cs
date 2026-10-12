using Avala.Preview.Navigation;
using Avala.Preview.Origins;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Preview.Tests.Origins;

public sealed class PreviewOriginsTests
{
    private static readonly PreviewScope Scope = new(new PortBlock(24_000, 24_009), [5_173], "/w/job");

    [Theory]
    [InlineData("http://localhost:24000/")]
    [InlineData("http://127.0.0.1:24009/app/index.html")]
    [InlineData("http://[::1]:24003/")]
    public void ALoopbackPageOnALeasedPortIsAdmitted(string address) =>
        Assert.True(Scope.Admits(new Uri(address), Option<string>.None));

    [Fact]
    public void ALoopbackPageOnAPortTheJobListensOnOutsideItsLeaseIsAdmitted() =>
        Assert.True(Scope.Admits(new Uri("http://localhost:5173/"), Option<string>.None));

    [Theory]
    [InlineData("http://localhost:24010/")]
    [InlineData("http://127.0.0.1:8080/")]
    public void ALoopbackPageOnAPortNeitherLeasedNorListenedOnIsRefused(string address) =>
        Assert.False(Scope.Admits(new Uri(address), Option<string>.None));

    [Theory]
    [InlineData("https://localhost:24000/")]
    [InlineData("http://example.com:24000/")]
    [InlineData("http://localhost.example.com:24000/")]
    [InlineData("http://user@localhost:24000/")]
    [InlineData("ftp://localhost:24000/")]
    public void AnotherSchemeHostOrCredentialIsRefused(string address) =>
        Assert.False(Scope.Admits(new Uri(address), Option<string>.None));

    [Fact]
    public async Task AFileInsideTheWorktreeIsAdmittedAsync()
    {
        using var worktree = new TemporaryFolder();
        var page = Path.Combine(worktree.Path, "site", "index.html");
        Directory.CreateDirectory(Path.GetDirectoryName(page)!);
        await File.WriteAllTextAsync(page, "<p>hi</p>", TestContext.Current.CancellationToken);
        var scope = Scope with { Worktree = RealFiles.Folder(worktree.Path) };
        var address = new Uri(page);

        Assert.True(scope.Admits(address, RealFiles.Of(address)));
    }

    [Fact]
    public void AFileThatClimbsOutOfTheWorktreeIsRefused()
    {
        using var worktree = new TemporaryFolder();
        var scope = Scope with { Worktree = RealFiles.Folder(worktree.Path) };
        var address = new Uri(Path.Combine(worktree.Path, "..", "secrets.html"));

        Assert.False(scope.Admits(address, RealFiles.Of(address)));
    }

    [Fact]
    public async Task AFileReachedThroughALinkOutOfTheWorktreeIsRefusedAsync()
    {
        using var worktree = new TemporaryFolder();
        using var elsewhere = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(elsewhere.Path, "secrets.html"), "<p>secret</p>", TestContext.Current.CancellationToken);
        Directory.CreateSymbolicLink(Path.Combine(worktree.Path, "out"), elsewhere.Path);
        var scope = Scope with { Worktree = RealFiles.Folder(worktree.Path) };
        var address = new Uri(Path.Combine(worktree.Path, "out", "secrets.html"));

        Assert.False(scope.Admits(address, RealFiles.Of(address)));
    }

    [Theory]
    [InlineData("https://unpkg.com/three@0.170.0/build/three.module.js")]
    [InlineData("http://localhost:9999/socket")]
    public void AResourceFromAnotherOriginIsRefusedUntilNetworkIsAllowed(string address)
    {
        Assert.False(Scope.AdmitsResource(new Uri(address), network: false, Option<string>.None));
        Assert.True(Scope.AdmitsResource(new Uri(address), network: true, Option<string>.None));
    }

    [Theory]
    [InlineData("ws://localhost:24000/@vite/client")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData("blob:http://localhost:24000/1f0e")]
    public void AResourceFromThePagesOwnOriginOrInlineIsAdmittedWithoutNetwork(string address) =>
        Assert.True(Scope.AdmitsResource(new Uri(address), network: false, Option<string>.None));
}
