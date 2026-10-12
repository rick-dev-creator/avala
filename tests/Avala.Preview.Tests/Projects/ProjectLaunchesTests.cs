using Avala.Preview.Projects;
using Avala.Preview.Running;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Preview.Tests.Projects;

public sealed class ProjectLaunchesTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static string Npm => OperatingSystem.IsWindows() ? "npm.cmd" : "npm";

    [Fact]
    public async Task TheDevScriptIsRunBeforeStartAsync()
    {
        using var worktree = await WorktreeAsync(("package.json", """{ "scripts": { "start": "node server.js", "dev": "vite" } }"""));

        var launch = Outcomes.Present(await new ProjectLaunches().FindAsync(worktree.Path, Cancellation));

        Assert.Equal(("dev", LaunchKind.Script, Npm, "run dev"), (launch.Name, launch.Kind, launch.FileName, string.Join(' ', launch.Arguments)));
    }

    [Fact]
    public async Task TheStartScriptIsRunWhenThereIsNoDevScriptAsync()
    {
        using var worktree = await WorktreeAsync(("package.json", """{ "scripts": { "start": "node server.js", "test": "vitest" } }"""));

        Assert.Equal("start", Outcomes.Present(await new ProjectLaunches().FindAsync(worktree.Path, Cancellation)).Name);
    }

    [Fact]
    public async Task TheLockFileChoosesThePackageManagerAsync()
    {
        using var worktree = await WorktreeAsync(("package.json", """{ "scripts": { "dev": "vite" } }"""), ("pnpm-lock.yaml", "lockfileVersion: '9.0'"));

        var launch = Outcomes.Present(await new ProjectLaunches().FindAsync(worktree.Path, Cancellation));

        Assert.Equal(OperatingSystem.IsWindows() ? "pnpm.cmd" : "pnpm", launch.FileName);
    }

    [Fact]
    public async Task AnIndexPageIsServedWhenThereIsNoScriptAsync()
    {
        using var worktree = await WorktreeAsync(("package.json", """{ "name": "site" }"""), ("index.html", "<h1>Hi</h1>"));

        var launch = Outcomes.Present(await new ProjectLaunches().FindAsync(worktree.Path, Cancellation));

        Assert.Equal(("index.html", LaunchKind.Page), (launch.Name, launch.Kind));
    }

    [Theory]
    [InlineData("""{ "scripts": { "test": "vitest" } }""")]
    [InlineData("""{ "scripts": { "dev": 3 } }""")]
    [InlineData("not json")]
    public async Task NothingIsLaunchedWithoutAScriptOrAPageAsync(string manifest)
    {
        using var worktree = await WorktreeAsync(("package.json", manifest));

        Assert.Equal(Option<Launch>.None, await new ProjectLaunches().FindAsync(worktree.Path, Cancellation));
    }

    private static async Task<TemporaryFolder> WorktreeAsync(params (string Path, string Content)[] files)
    {
        var worktree = new TemporaryFolder();

        foreach (var (path, content) in files)
        {
            await File.WriteAllTextAsync(Path.Combine(worktree.Path, path), content, Cancellation);
        }

        return worktree;
    }
}
