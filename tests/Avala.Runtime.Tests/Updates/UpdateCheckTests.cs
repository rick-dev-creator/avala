using System.Net;
using Avala.Runtime.Updates;
using Avala.Sdk;
using Avala.Sdk.Updates;
using Avala.Testing;
using static Avala.Runtime.Tests.Updates.FakeReleases;

namespace Avala.Runtime.Tests.Updates;

public sealed class UpdateCheckTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ANewerReleaseIsAvailableWithItsPageAndAnnouncedAsync()
    {
        await using var data = new TemporaryFolder();
        var bus = new RecordingBus();
        using var releases = Listing(Release("v0.1.0"), Release("v0.3.0"), Release("v0.2.0"));

        var state = await Check(data, "0.1.0", releases, bus).CheckAsync(Cancellation);

        var update = new AvailableUpdate("0.3.0", new Uri("https://github.com/rick-dev-creator/avala/releases/tag/v0.3.0"));
        Assert.Equal(new UpdateState(UpdateStatus.Available, update), state);
        Assert.Equal([new UpdateFound(update)], bus.Published);
        Assert.Equal(ReleaseFeed.Releases, Assert.Single(releases.Requests).RequestUri);
        Assert.Contains("Avala", Assert.Single(releases.Requests).Headers.UserAgent.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.2.0")]
    [InlineData("0.3.0")]
    public async Task NoNewerReleaseLeavesTheBuildUpToDateAndAnnouncesNothingAsync(string current)
    {
        await using var data = new TemporaryFolder();
        var bus = new RecordingBus();
        using var releases = Listing(Release("v0.1.0"), Release("v0.2.0"));

        var state = await Check(data, current, releases, bus).CheckAsync(Cancellation);

        Assert.Equal(new UpdateState(UpdateStatus.UpToDate, Option<AvailableUpdate>.None), state);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task ARepositoryWithoutReleasesLeavesTheBuildUpToDateAsync()
    {
        await using var data = new TemporaryFolder();
        using var releases = Replying(HttpStatusCode.NotFound, """{ "message": "Not Found" }""");

        var state = await Check(data, "0.1.0", releases, new RecordingBus()).CheckAsync(Cancellation);

        Assert.Equal(UpdateStatus.UpToDate, state.Status);
    }

    [Fact]
    public async Task AStableBuildIgnoresDraftsAndPrereleasesAsync()
    {
        await using var data = new TemporaryFolder();
        using var releases = Listing(Release("v0.2.0", draft: true), Release("v0.3.0-beta.1"), Release("v0.4.0", prerelease: true), Release("v0.1.1"));

        var state = await Check(data, "0.1.0", releases, new RecordingBus()).CheckAsync(Cancellation);

        Assert.Equal("0.1.1", state.Update.Match(update => update.Version, () => "none"));
    }

    [Fact]
    public async Task APrereleaseBuildIsOfferedANewerPrereleaseAsync()
    {
        await using var data = new TemporaryFolder();
        using var releases = Listing(Release("v0.1.0-beta.1", prerelease: true), Release("v0.1.0-beta.2", prerelease: true), Release("v0.1.0-rc.1", draft: true));

        var state = await Check(data, "0.1.0-beta.1+5c371e6", releases, new RecordingBus()).CheckAsync(Cancellation);

        Assert.Equal("0.1.0-beta.2", state.Update.Match(update => update.Version, () => "none"));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "[]")]
    [InlineData(HttpStatusCode.Forbidden, """{ "message": "API rate limit exceeded" }""")]
    [InlineData(HttpStatusCode.OK, "<html>not json</html>")]
    [InlineData(HttpStatusCode.OK, """{ "tag_name": "v9.0.0" }""")]
    public async Task AnUnusableReplyLeavesGitHubUnreachableAsync(HttpStatusCode status, string body)
    {
        await using var data = new TemporaryFolder();
        var bus = new RecordingBus();
        using var releases = Replying(status, body);

        var state = await Check(data, "0.1.0", releases, bus).CheckAsync(Cancellation);

        Assert.Equal(new UpdateState(UpdateStatus.Unreachable, Option<AvailableUpdate>.None), state);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task ANetworkFailureLeavesGitHubUnreachableWithoutThrowingAsync()
    {
        await using var data = new TemporaryFolder();
        using var releases = Failing();

        var state = await Check(data, "0.1.0", releases, new RecordingBus()).CheckAsync(Cancellation);

        Assert.Equal(UpdateStatus.Unreachable, state.Status);
    }

    [Fact]
    public async Task AReleasePageOutsideGitHubFallsBackToTheReleasesPageAsync()
    {
        await using var data = new TemporaryFolder();
        using var releases = Replying(HttpStatusCode.OK, """[{ "tag_name": "v1.0.0", "html_url": "https://example.com/avala.exe" }]""");

        var state = await Check(data, "0.1.0", releases, new RecordingBus()).CheckAsync(Cancellation);

        Assert.Equal(ReleaseFeed.ReleasesPage, state.Update.Match(update => update.Release, () => new Uri("https://none")));
    }

    [Fact]
    public async Task StartupDoesNotWaitForTheReplyAsync()
    {
        await using var data = new TemporaryFolder();
        var bus = new RecordingBus();
        using var releases = Pending();
        var check = Check(data, "0.1.0", releases, bus);

        await check.RunAsync(Cancellation);
        var whileWaiting = check.Latest.Status;
        releases.Reply(HttpStatusCode.OK, """[{ "tag_name": "v0.2.0" }]""");
        var found = await bus.WaitForAsync<UpdateFound>(_ => true, Cancellation);

        Assert.Contains(whileWaiting, new[] { UpdateStatus.NotChecked, UpdateStatus.Checking });
        Assert.Equal("0.2.0", found.Update.Version);
        await check.Checking;
        Assert.Equal(UpdateStatus.Available, check.Latest.Status);
    }

    [Theory]
    [InlineData("""{ "checkOnStartup": false }""")]
    [InlineData("""{ "checkOnStartup": "no" }""")]
    [InlineData("not json")]
    public async Task TheMachineSettingTurnsTheStartupCheckOffButAskingStillChecksAsync(string settings)
    {
        await using var data = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(data.Path, UpdatesFile.FileName), settings, Cancellation);
        using var releases = Listing(Release("v0.2.0"));
        var check = Check(data, "0.1.0", releases, new RecordingBus());

        await check.RunAsync(Cancellation);
        await check.Checking;
        var atStartup = (check.Latest.Status, releases.Requests.Count);
        var asked = await check.CheckAsync(Cancellation);

        Assert.Equal((UpdateStatus.Off, 0), atStartup);
        Assert.Equal(UpdateStatus.Available, asked.Status);
    }

    [Theory]
    [InlineData("""{ "checkOnStartup": true }""")]
    [InlineData("{ }")]
    public async Task TheStartupCheckRunsUnlessTurnedOffAsync(string settings)
    {
        await using var data = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(data.Path, UpdatesFile.FileName), settings, Cancellation);
        using var releases = Listing(Release("v0.2.0"));
        var check = Check(data, "0.1.0", releases, new RecordingBus());

        await check.RunAsync(Cancellation);
        await check.Checking;

        Assert.Equal(UpdateStatus.Available, check.Latest.Status);
    }

    [Fact]
    public async Task WithoutAFeedNothingIsAskedAsync()
    {
        await using var data = new TemporaryFolder();
        var check = new UpdateCheck(new AvalaBuild("0.1.0", Option<string>.None), new UpdatesFile(new AvalaPaths(data.Path)), Option<ReleaseFeed>.None, new RecordingBus());

        await check.RunAsync(Cancellation);
        var asked = await check.CheckAsync(Cancellation);

        Assert.Equal(UpdateState.NotChecked, asked);
    }

    private static UpdateCheck Check(TemporaryFolder data, string version, FakeReleases releases, RecordingBus bus) =>
        new(
            AvalaBuild.From(version),
            new UpdatesFile(new AvalaPaths(data.Path)),
            new ReleaseFeed(new HttpClient(releases, disposeHandler: false)),
            bus);
}
