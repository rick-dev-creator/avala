using System.Text.Json;
using Avala.Runtime.Telemetry;
using Avala.Sdk;
using Avala.Sdk.Telemetry;
using Avala.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Runtime.Tests.Telemetry;

public sealed class UsageTelemetryTests
{
    private const string AppKey = "A-EU-1234567890";

    private static readonly DateTimeOffset Morning = new(2026, 10, 12, 9, 30, 0, TimeSpan.Zero);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task InstalledIsSentOnceEverAcrossRestartsAsync()
    {
        await using var data = new TemporaryFolder();
        using var aptabase = new FakeAptabase();
        var clock = Clock(Morning);

        await StartAsync(data, aptabase, clock);
        await StartAsync(data, aptabase, clock);
        clock.Advance(TimeSpan.FromDays(1));
        await StartAsync(data, aptabase, clock);

        Assert.Equal([TelemetrySource.Installed, TelemetrySource.Active, TelemetrySource.Active], aptabase.Events);
    }

    [Fact]
    public async Task ActiveIsSentAtMostOncePerLocalDayWhileRunningAcrossMidnightAsync()
    {
        await using var data = new TemporaryFolder();
        using var aptabase = new FakeAptabase();
        var clock = Clock(new DateTimeOffset(2026, 10, 12, 23, 0, 0, TimeSpan.Zero));
        await using var telemetry = Telemetry(data, aptabase, clock);
        await telemetry.RunAsync(Cancellation);
        await telemetry.Sending;

        clock.Advance(TimeSpan.FromMinutes(30));
        await telemetry.Sending;
        var beforeMidnight = aptabase.Events.Count;
        clock.Advance(TimeSpan.FromMinutes(31));
        await telemetry.Sending;
        clock.Advance(TimeSpan.FromHours(12));
        await telemetry.Sending;

        Assert.Equal(2, beforeMidnight);
        Assert.Equal([TelemetrySource.Installed, TelemetrySource.Active, TelemetrySource.Active], aptabase.Events);
    }

    [Theory]
    [InlineData("toggle")]
    [InlineData("DO_NOT_TRACK=1")]
    [InlineData("AVALA_TELEMETRY=0")]
    [InlineData("no key")]
    public async Task NothingIsSentWhenTelemetryIsOffOrNotConfiguredAsync(string off)
    {
        await using var data = new TemporaryFolder();
        using var aptabase = new FakeAptabase();
        var clock = Clock(Morning);
        var environment = off.Split('=') is [var name, var value] ? new Dictionary<string, string> { [name] = value } : new Dictionary<string, string>();

        if (off == "toggle")
        {
            await using var settings = Telemetry(data, aptabase, clock);
            _ = await settings.TurnAsync(false, Cancellation);
        }

        await using var telemetry = Telemetry(data, aptabase, clock, TelemetrySource.OptOut(environment.GetValueOrDefault), configured: off != "no key");
        await telemetry.RunAsync(Cancellation);
        await telemetry.Sending;
        clock.Advance(TimeSpan.FromDays(1));
        await telemetry.Sending;
        var state = await telemetry.ReadAsync(Cancellation);

        Assert.Empty(aptabase.Sent);
        Assert.Equal(
            off switch
            {
                "toggle" => TelemetryStatus.Off,
                "no key" => TelemetryStatus.NotConfigured,
                _ => TelemetryStatus.OffByEnvironment,
            },
            state.Status);
        Assert.Equal(off.Contains('=', StringComparison.Ordinal) ? off : string.Empty, state.OptedOutBy.Match(variable => variable, () => string.Empty));
    }

    [Fact]
    public async Task AnEventHoldsOnlyTheInstallIdTheVersionTheOperatingSystemAndTheArchitectureAsync()
    {
        await using var data = new TemporaryFolder();
        using var aptabase = new FakeAptabase();

        await StartAsync(data, aptabase, Clock(Morning));

        var sent = aptabase.Sent[0];
        var root = JsonDocument.Parse(sent.Body).RootElement;
        Assert.Equal(new Uri("https://eu.aptabase.com/api/v0/event"), sent.Address);
        Assert.Equal(AppKey, sent.Key);
        Assert.Equal(["timestamp", "sessionId", "eventName", "systemProps", "props"], Keys(root));
        Assert.Equal(["osName", "osVersion", "appVersion", "sdkVersion"], Keys(root.GetProperty("systemProps")));
        Assert.Equal(["architecture"], Keys(root.GetProperty("props")));
        Assert.Equal(
            ["2026-10-12T09:30:00.000Z", "installed", "Linux", "6.10.2", "0.2.0", "avala", "x64"],
            [
                root.GetProperty("timestamp").GetString(),
                root.GetProperty("eventName").GetString(),
                root.GetProperty("systemProps").GetProperty("osName").GetString(),
                root.GetProperty("systemProps").GetProperty("osVersion").GetString(),
                root.GetProperty("systemProps").GetProperty("appVersion").GetString(),
                root.GetProperty("systemProps").GetProperty("sdkVersion").GetString(),
                root.GetProperty("props").GetProperty("architecture").GetString(),
            ]);
        Assert.True(Guid.TryParseExact(root.GetProperty("sessionId").GetString(), "D", out _));
    }

    [Fact]
    public async Task AFailedSendDoesNotThrowAndIsSentAgainAtTheNextStartAsync()
    {
        await using var data = new TemporaryFolder();
        using var aptabase = new FakeAptabase { Failing = true };
        var clock = Clock(Morning);

        await StartAsync(data, aptabase, clock);
        aptabase.Failing = false;
        await StartAsync(data, aptabase, clock);

        Assert.Equal([TelemetrySource.Installed, TelemetrySource.Active], aptabase.Events);
    }

    [Fact]
    public async Task ThePreviewIsExactlyWhatIsSentAsync()
    {
        await using var data = new TemporaryFolder();
        using var aptabase = new FakeAptabase();
        var clock = Clock(Morning);
        await using var telemetry = Telemetry(data, aptabase, clock);
        var preview = await telemetry.PreviewAsync(Cancellation);

        await telemetry.RunAsync(Cancellation);
        await telemetry.Sending;

        Assert.Equal(TelemetrySource.Active, aptabase.Events[^1]);
        Assert.Equal(preview, aptabase.Sent[^1].Body);
    }

    [Fact]
    public async Task TheInstallIdIsKeptAcrossRestartsUntilItIsResetAsync()
    {
        await using var data = new TemporaryFolder();
        using var aptabase = new FakeAptabase();
        var clock = Clock(Morning);
        await StartAsync(data, aptabase, clock);
        await using var telemetry = Telemetry(data, aptabase, clock);
        var kept = (await telemetry.ReadAsync(Cancellation)).InstallId;

        var reset = (await telemetry.ResetInstallIdAsync(Cancellation)).InstallId;
        clock.Advance(TimeSpan.FromDays(1));
        await StartAsync(data, aptabase, clock);

        Assert.Equal(Sessions(aptabase)[0], kept.Match(id => id, () => "none"));
        Assert.NotEqual(kept, reset);
        Assert.Equal(reset.Match(id => id, () => "none"), Sessions(aptabase)[^1]);
    }

    [Fact]
    public async Task TheNoticeIsClaimedOnceEverAsync()
    {
        await using var data = new TemporaryFolder();
        using var aptabase = new FakeAptabase();
        var clock = Clock(Morning);
        bool first;

        await using (var telemetry = Telemetry(data, aptabase, clock))
        {
            first = await telemetry.ClaimNoticeAsync(Cancellation);
        }

        await using var restarted = Telemetry(data, aptabase, clock);

        Assert.True(first);
        Assert.False(await restarted.ClaimNoticeAsync(Cancellation));
    }

    [Fact]
    public async Task AnUnreadableTelemetryFileSendsNothingAndIsKeptAsync()
    {
        await using var data = new TemporaryFolder();
        using var aptabase = new FakeAptabase();
        var path = Path.Combine(data.Path, TelemetryFile.FileName);
        await File.WriteAllTextAsync(path, "not json", Cancellation);

        await StartAsync(data, aptabase, Clock(Morning));

        Assert.Empty(aptabase.Sent);
        Assert.Equal("not json", await File.ReadAllTextAsync(path, Cancellation));
    }

    [Theory]
    [InlineData("A-EU-1234567890", "", "https://eu.aptabase.com/api/v0/event")]
    [InlineData("A-US-1234567890", "", "https://us.aptabase.com/api/v0/event")]
    [InlineData("A-SH-1234567890", "https://analytics.example.com/", "https://analytics.example.com/api/v0/event")]
    [InlineData("A-SH-1234567890", "", "none")]
    [InlineData("A-XX-1234567890", "", "none")]
    [InlineData("not a key", "", "none")]
    public void TheAppKeyDecidesWhereEventsGo(string key, string host, string events) =>
        Assert.Equal(
            events,
            TelemetryKey.Parse(key, host.Length > 0 ? host : Option<string>.None).Match(found => found.Events.AbsoluteUri, () => "none"));

    private static async Task StartAsync(TemporaryFolder data, FakeAptabase aptabase, FakeTimeProvider clock)
    {
        await using var telemetry = Telemetry(data, aptabase, clock);
        await telemetry.RunAsync(Cancellation);
        await telemetry.Sending;
    }

    private static UsageTelemetry Telemetry(TemporaryFolder data, FakeAptabase aptabase, FakeTimeProvider clock, Option<string> optedOutBy = default, bool configured = true) =>
        new(
            new TelemetryFile(new AvalaPaths(data.Path)),
            new TelemetrySource(new AvalaBuild("0.2.0", "5c371e6"), new TelemetryPlatform("Linux", "6.10.2", "x64"), optedOutBy),
            configured
                ? new AptabaseSender(new HttpClient(aptabase, disposeHandler: false), TelemetryKey.Parse(AppKey, Option<string>.None).Match(key => key, () => throw new InvalidOperationException()), NullLogger<AptabaseSender>.Instance)
                : Option<AptabaseSender>.None,
            clock);

    private static FakeTimeProvider Clock(DateTimeOffset start)
    {
        var clock = new FakeTimeProvider(start);
        clock.SetLocalTimeZone(TimeZoneInfo.Utc);

        return clock;
    }

    private static string[] Keys(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];

    private static string[] Sessions(FakeAptabase aptabase) =>
        [.. aptabase.Sent.Select(sent => JsonDocument.Parse(sent.Body).RootElement.GetProperty("sessionId").GetString()!)];
}
