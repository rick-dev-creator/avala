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
        var port = Workloads.FreePort();
        await using var server = Outcomes.Present(PageServer.Start(worktree.Path, port));
        using var client = new HttpClient();

        var response = await client.GetAsync(new Uri($"http://127.0.0.1:{port}/"), Cancellation);

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
        var port = Workloads.FreePort();
        await using var server = Outcomes.Present(PageServer.Start(worktree.Path, port));
        using var client = new HttpClient();

        server.AllowsNetwork = true;
        var response = await client.GetAsync(new Uri($"http://127.0.0.1:{port}/index.html"), Cancellation);

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
        var port = Workloads.FreePort();
        await using var server = Outcomes.Present(PageServer.Start(worktree.Path, port));
        using var client = new HttpClient();

        var response = await client.GetAsync(new Uri($"http://127.0.0.1:{port}/out/secrets.html"), Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OnlyReadingRequestsAreAnsweredAsync()
    {
        using var worktree = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(worktree.Path, "index.html"), "<h1>Orders</h1>", Cancellation);
        var port = Workloads.FreePort();
        await using var server = Outcomes.Present(PageServer.Start(worktree.Path, port));
        using var client = new HttpClient();

        var response = await client.PostAsync(new Uri($"http://127.0.0.1:{port}/index.html"), new StringContent("x"), Cancellation);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
