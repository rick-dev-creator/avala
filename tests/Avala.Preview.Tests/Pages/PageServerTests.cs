using System.Net;
using Avala.Preview.Pages;
using Avala.Testing;

namespace Avala.Preview.Tests.Pages;

public sealed class PageServerTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AWorktreesPageIsServedOnLoopbackWithAPolicyThatKeepsOtherOriginsOutAsync()
    {
        using var worktree = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(worktree.Path, "index.html"), "<h1>Orders</h1>", Cancellation);
        await using var server = Outcomes.Present(Workloads.OnFreePort(port => PageServer.Start(worktree.Path, port)));
        using var client = new HttpClient();

        var response = await client.GetAsync(new Uri($"http://127.0.0.1:{server.Port}/"), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("<h1>Orders</h1>", await response.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(PageServer.Policy, Assert.Single(response.Headers.GetValues("Content-Security-Policy")));
    }

    [Fact]
    public async Task AllowingNetworkServesPagesWithoutThePolicyAsync()
    {
        using var worktree = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(worktree.Path, "index.html"), "<h1>Orders</h1>", Cancellation);
        await using var server = Outcomes.Present(Workloads.OnFreePort(port => PageServer.Start(worktree.Path, port)));
        using var client = new HttpClient();

        server.AllowsNetwork = true;
        var response = await client.GetAsync(new Uri($"http://127.0.0.1:{server.Port}/index.html"), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Content-Security-Policy"));
    }

    [Fact]
    public async Task AFileReachedThroughALinkOutOfTheWorktreeIsNotServedAsync()
    {
        using var worktree = new TemporaryFolder();
        using var elsewhere = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(elsewhere.Path, "secrets.html"), "<p>secret</p>", Cancellation);
        Directory.CreateSymbolicLink(Path.Combine(worktree.Path, "out"), elsewhere.Path);
        await using var server = Outcomes.Present(Workloads.OnFreePort(port => PageServer.Start(worktree.Path, port)));
        using var client = new HttpClient();

        var response = await client.GetAsync(new Uri($"http://127.0.0.1:{server.Port}/out/secrets.html"), Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OnlyReadingRequestsAreAnsweredAsync()
    {
        using var worktree = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(worktree.Path, "index.html"), "<h1>Orders</h1>", Cancellation);
        await using var server = Outcomes.Present(Workloads.OnFreePort(port => PageServer.Start(worktree.Path, port)));
        using var client = new HttpClient();

        var response = await client.PostAsync(new Uri($"http://127.0.0.1:{server.Port}/index.html"), new StringContent("x"), Cancellation);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Theory]
    [InlineData("__avala/viewer/", "text/html")]
    [InlineData("__avala/viewer/viewer.js", "text/javascript")]
    [InlineData("__avala/viewer/three/three.module.js", "text/javascript")]
    [InlineData("__avala/viewer/three/addons/loaders/GLTFLoader.js", "text/javascript")]
    public async Task TheModelViewerIsServedFromAvalaItselfAsync(string path, string type)
    {
        using var worktree = new TemporaryFolder();
        await using var server = Outcomes.Present(Workloads.OnFreePort(port => PageServer.Start(worktree.Path, port)));
        using var client = new HttpClient();

        var response = await client.GetAsync(new Uri($"http://127.0.0.1:{server.Port}/{path}"), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(type, response.Content.Headers.ContentType?.MediaType);
        Assert.NotEmpty(await response.Content.ReadAsByteArrayAsync(Cancellation));
    }

    [Fact]
    public async Task AModelInTheWorktreeIsServedWithItsTypeAsync()
    {
        using var worktree = new TemporaryFolder();
        Directory.CreateDirectory(Path.Combine(worktree.Path, "parts"));
        await File.WriteAllTextAsync(Path.Combine(worktree.Path, "parts", "bracket.stl"), "solid bracket\nendsolid bracket\n", Cancellation);
        await using var server = Outcomes.Present(Workloads.OnFreePort(port => PageServer.Start(worktree.Path, port)));
        using var client = new HttpClient();

        var response = await client.GetAsync(new Uri($"http://127.0.0.1:{server.Port}/parts/bracket.stl"), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("model/stl", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task AViewerFileAvalaDoesNotShipIsNotFoundAsync()
    {
        using var worktree = new TemporaryFolder();
        await using var server = Outcomes.Present(Workloads.OnFreePort(port => PageServer.Start(worktree.Path, port)));
        using var client = new HttpClient();

        var response = await client.GetAsync(new Uri($"http://127.0.0.1:{server.Port}/__avala/viewer/../../secrets.txt"), Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void APageMayReadDataItAlreadyHoldsButReachesNoOtherOrigin() =>
        Assert.EndsWith("connect-src 'self' data: blob: ws://127.0.0.1:*", PageServer.Policy, StringComparison.Ordinal);
}
