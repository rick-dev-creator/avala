using Avala.Forges.Contracts;
using Avala.Forges.Testing;
using Avala.Gitea.SignIn;
using Avala.Sdk.Processes;

namespace Avala.Gitea.Tests;

public sealed class TeaLoginsTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryTeaLoginIsReadWithItsHostItsUserAndItsNameAsTheReferenceAsync()
    {
        var logins = await FixtureServer.FixtureAsync("gitea", "tea-logins", Cancellation);

        Assert.Equal(
            [
                new DetectedLogin(new Uri("https://codeberg.org"), "ada") { Reference = "codeberg" },
                new DetectedLogin(new Uri("https://gitea.example.com"), "ada-work") { Reference = "work" },
            ],
            new TeaLogins().Read(new ProcessOutcome(0, logins, string.Empty)));
    }

    [Theory]
    [InlineData(1, "[]")]
    [InlineData(0, "not json")]
    public void AFailedOrUnreadableLoginListNamesNoLogin(int exitCode, string output) =>
        Assert.Empty(new TeaLogins().Read(new ProcessOutcome(exitCode, output, string.Empty)));
}
