using Avala.Notifications.Contracts;
using Avala.Notifications.NotificationFiles;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Notifications.Tests.NotificationFiles;

public sealed class NotificationFileParserTests
{
    [Fact]
    public void ATopicAloneIsReadWithEveryDefault()
    {
        var settings = Outcomes.Succeeds(NotificationFileParser.Parse("""{ "ntfy": { "topic": "avala-k3j9" } }"""));

        Assert.Equal((true, ChannelKind.Ntfy, DetailLevel.Minimal, false), (settings.Enabled, settings.Channel, settings.Detail, settings.Quiet.IsSome));
        Assert.Equal(new NtfyTarget(new Uri("https://ntfy.sh"), "avala-k3j9"), Outcomes.Present(settings.Ntfy));
        Assert.Equal(
            [NotificationKind.Decision, NotificationKind.Held, NotificationKind.Budget, NotificationKind.Failed, NotificationKind.LoopEnded],
            settings.Events);
    }

    [Fact]
    public void AWebhookWithItsSecretChosenEventsDetailAndQuietHoursIsRead()
    {
        var settings = Outcomes.Succeeds(NotificationFileParser.Parse("""
            {
              "enabled": false,
              "channel": "webhook",
              "webhook": { "url": "https://hooks.example.com/avala", "secretEnv": "AVALA_HOOK_SECRET" },
              "events": { "review": true, "loopEnded": false },
              "detail": "detailed",
              "quietHours": { "from": "22:00", "to": "07:00", "days": ["fri", "sat"] }
            }
            """));

        Assert.Equal((false, ChannelKind.Webhook, DetailLevel.Detailed), (settings.Enabled, settings.Channel, settings.Detail));
        Assert.Equal((new Uri("https://hooks.example.com/avala"), "AVALA_HOOK_SECRET"), Outcomes.Present(settings.Webhook.Map(hook => (hook.Url, Outcomes.Present(hook.SecretEnv)))));
        Assert.Equal(
            [NotificationKind.Decision, NotificationKind.Held, NotificationKind.Budget, NotificationKind.Failed, NotificationKind.Review],
            settings.Events);
        var quiet = Outcomes.Present(settings.Quiet);
        Assert.Equal((new TimeOnly(22, 0), new TimeOnly(7, 0)), (quiet.From, quiet.To));
        Assert.Equal([DayOfWeek.Friday, DayOfWeek.Saturday], quiet.Days);
    }

    [Fact]
    public void PlainHttpIsAcceptedOnlyForThisComputer()
    {
        var loopback = NotificationFileParser.Parse("""{ "ntfy": { "server": "http://localhost:8080", "topic": "t" } }""");
        var remote = NotificationFileParser.Parse("""{ "ntfy": { "server": "http://ntfy.example.com", "topic": "t" } }""");

        Assert.Equal(new Uri("http://localhost:8080"), Outcomes.Present(Outcomes.Succeeds(loopback).Ntfy).Server);
        Assert.Equal(NotificationError.InsecureAddress, Outcomes.FailsWith(remote));
    }

    [Theory]
    [InlineData("{", NotificationError.Malformed)]
    [InlineData("[]", NotificationError.Malformed)]
    [InlineData("""{ "detail": "minimal", "detail": "detailed" }""", NotificationError.Malformed)]
    [InlineData("""{ "enabled": "yes", "ntfy": { "topic": "t" } }""", NotificationError.Malformed)]
    [InlineData("""{ "sound": true, "ntfy": { "topic": "t" } }""", NotificationError.UnknownField)]
    [InlineData("""{ "ntfy": { "topic": "t", "token": "x" } }""", NotificationError.UnknownField)]
    [InlineData("""{ "channel": "sms", "ntfy": { "topic": "t" } }""", NotificationError.InvalidChannel)]
    [InlineData("""{ "channel": "webhook", "ntfy": { "topic": "t" } }""", NotificationError.MissingChannel)]
    [InlineData("""{}""", NotificationError.MissingChannel)]
    [InlineData("""{ "ntfy": { "server": "ftp://ntfy.sh", "topic": "t" } }""", NotificationError.InvalidAddress)]
    [InlineData("""{ "ntfy": { "server": "https://user:pass@ntfy.sh", "topic": "t" } }""", NotificationError.InvalidAddress)]
    [InlineData("""{ "channel": "webhook", "webhook": { "secretEnv": "S" } }""", NotificationError.InvalidAddress)]
    [InlineData("""{ "channel": "webhook", "webhook": { "url": "http://hooks.example.com" } }""", NotificationError.InsecureAddress)]
    [InlineData("""{ "ntfy": { "topic": "" } }""", NotificationError.InvalidTopic)]
    [InlineData("""{ "ntfy": { "topic": "a/b" } }""", NotificationError.InvalidTopic)]
    [InlineData("""{ "ntfy": { "server": "https://ntfy.sh" } }""", NotificationError.InvalidTopic)]
    [InlineData("""{ "ntfy": { "topic": "t", "tokenEnv": "1TOKEN" } }""", NotificationError.InvalidSecret)]
    [InlineData("""{ "ntfy": { "topic": "t" }, "events": { "decision": "on" } }""", NotificationError.InvalidEvents)]
    [InlineData("""{ "ntfy": { "topic": "t" }, "events": { "deploy": true } }""", NotificationError.UnknownField)]
    [InlineData("""{ "ntfy": { "topic": "t" }, "detail": "full" }""", NotificationError.InvalidDetail)]
    [InlineData("""{ "ntfy": { "topic": "t" }, "quietHours": { "from": "25:00", "to": "07:00" } }""", NotificationError.InvalidQuietHours)]
    [InlineData("""{ "ntfy": { "topic": "t" }, "quietHours": { "from": "07:00", "to": "07:00" } }""", NotificationError.InvalidQuietHours)]
    [InlineData("""{ "ntfy": { "topic": "t" }, "quietHours": { "from": "22:00", "to": "07:00", "days": ["mon", "mon"] } }""", NotificationError.InvalidQuietHours)]
    [InlineData("""{ "ntfy": { "topic": "t" }, "quietHours": { "from": "22:00" } }""", NotificationError.InvalidQuietHours)]
    public void AFileOutsideItsFormatIsRejectedWithItsError(string text, NotificationError error) =>
        Assert.Equal(error, Outcomes.FailsWith(NotificationFileParser.Parse(text)));

    [Fact]
    public void AFileOverSixteenKibibytesIsTooLarge() =>
        Assert.Equal(
            NotificationError.TooLarge,
            Outcomes.FailsWith(NotificationFileParser.Parse($$"""{ "ntfy": { "topic": "t" }, "pad": "{{new string('x', NotificationFileParser.MaximumBytes)}}" }""")));

    [Fact]
    public void WrittenSettingsReadBackTheSame()
    {
        var settings = new NotificationSettings(ChannelKind.Webhook)
        {
            Enabled = false,
            Ntfy = new NtfyTarget(new Uri("https://ntfy.example.com"), "avala-team") { TokenEnv = "AVALA_NTFY_TOKEN" },
            Webhook = new WebhookTarget(new Uri("https://hooks.example.com/avala")) { SecretEnv = "AVALA_HOOK_SECRET" },
            Events = [NotificationKind.Decision, NotificationKind.Review],
            Detail = DetailLevel.Detailed,
            Quiet = new QuietHours(new TimeOnly(22, 30), new TimeOnly(6, 45)) { Days = [DayOfWeek.Sunday] },
        };

        var read = Outcomes.Succeeds(NotificationFileParser.Parse(NotificationText.Write(settings)));

        Assert.Equal(
            (settings.Enabled, settings.Channel, settings.Ntfy, settings.Webhook, settings.Detail),
            (read.Enabled, read.Channel, read.Ntfy, read.Webhook, read.Detail));
        Assert.Equal(settings.Events, read.Events);
        var quiet = Outcomes.Present(read.Quiet);
        Assert.Equal((new TimeOnly(22, 30), new TimeOnly(6, 45)), (quiet.From, quiet.To));
        Assert.Equal([DayOfWeek.Sunday], quiet.Days);
    }
}
