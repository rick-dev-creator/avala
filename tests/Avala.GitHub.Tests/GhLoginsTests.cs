using Avala.Forges.Contracts;
using Avala.Forges.Testing;
using Avala.GitHub.SignIn;
using Avala.Sdk.Processes;

namespace Avala.GitHub.Tests;

public sealed class GhLoginsTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheGhCliNamesTheActiveLoginOfEveryHostAndAnEnterpriseHostIsItsReferenceAsync()
    {
        var status = await FixtureServer.TextAsync("github", "gh-auth-status.txt", Cancellation);

        Assert.Equal(
            [
                new DetectedLogin(new Uri("https://api.github.com"), "octocat"),
                new DetectedLogin(new Uri("https://ghe.example.com/api/v3"), "hubot") { Reference = "ghe.example.com" },
            ],
            new GhLogins().Read(new ProcessOutcome(0, status, string.Empty)));
    }

    [Fact]
    public async Task AnOlderGhCliWritingItsStatusToStandardErrorIsReadTooAsync()
    {
        var status = await FixtureServer.TextAsync("github", "gh-auth-status-legacy.txt", Cancellation);

        Assert.Equal([new DetectedLogin(new Uri("https://api.github.com"), "octocat")], new GhLogins().Read(new ProcessOutcome(0, string.Empty, status)));
    }

    [Fact]
    public async Task AGhCliLoggedOutNamesNoLoginAsync()
    {
        var status = await FixtureServer.TextAsync("github", "gh-auth-status-logged-out.txt", Cancellation);

        Assert.Empty(new GhLogins().Read(new ProcessOutcome(1, string.Empty, status)));
    }
}
