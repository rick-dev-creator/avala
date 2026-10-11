using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avala.Notifications.Composing;
using Avala.Notifications.Contracts;
using Avala.Testing;

namespace Avala.Notifications.Tests.Composing;

public sealed class ComposerTests
{
    private static string Redact(string text) => text.Replace("hunter2", "[redacted]", StringComparison.Ordinal);

    [Fact]
    public void AMinimalNotificationNamesTheEventTheRepositoryAndTheJobOnly()
    {
        var message = Composer.Compose([Samples.Decision(Samples.Job)], DetailLevel.Minimal, Redact, Samples.Monday);

        Assert.Equal(
            (NotificationKind.Decision, Severity.Urgent, "A decision needs you · ledger-api", "Fix the flaky login test", 1),
            (message.Kind, message.Severity, message.Title, message.Body, message.Count));
    }

    [Fact]
    public void ADetailedNotificationAddsTheRedactedDetailOnOneShortLine()
    {
        var notice = Samples.Decision(Samples.Job) with { Detail = $"Command: login --password hunter2\n{new string('x', 200)}" };

        var message = Composer.Compose([notice], DetailLevel.Detailed, Redact, Samples.Monday);

        var detail = message.Body.Split('\n')[1];
        Assert.StartsWith("Command: login --password [redacted] xxx", detail, StringComparison.Ordinal);
        Assert.Equal((Composer.LongestDetail, 2), (detail.Length, message.Body.Split('\n').Length));
    }

    [Fact]
    public void SeveralNoticesBecomeOneSummaryOfAtMostFiveLines()
    {
        var notices = Enumerable.Range(0, 7).Select(index => Samples.Failed(new Avala.Jobs.Contracts.JobId(Guid.NewGuid())) with { Repository = "ledger-api" }).ToList();
        notices[3] = Samples.Decision(Samples.Job) with { Count = 2 };

        var message = Composer.Compose(notices, DetailLevel.Minimal, Redact, Samples.Monday);

        Assert.Equal((NotificationKind.Summary, Severity.Urgent, "7 things need you", "ledger-api", 8), (message.Kind, message.Severity, message.Title, message.Repository, message.Count));
        Assert.Equal(
            ["A job failed · ledger-api · Fix the flaky login test", "A job failed · ledger-api · Fix the flaky login test", "A job failed · ledger-api · Fix the flaky login test", "2 decisions need you · ledger-api · Fix the flaky login test", "A job failed · ledger-api · Fix the flaky login test", "and 2 more"],
            message.Body.Split('\n'));
    }

    [Fact]
    public void ADigestSaysTheUpdatesWaitedForTheQuietHours()
    {
        var message = Composer.Digest([Samples.Failed(Samples.Job), Samples.Failed(Samples.Other)], Samples.Monday);

        Assert.Equal((NotificationKind.Digest, Severity.Info, "While quiet: 2 updates"), (message.Kind, message.Severity, message.Title));
    }

    [Fact]
    public void AnNtfyMessageIsPublishedAsJsonToTheServerWithItsTopicAndPriority()
    {
        var message = Composer.Compose([Samples.Decision(Samples.Job)], DetailLevel.Minimal, Redact, Samples.Monday);

        var payload = Payloads.Ntfy(message, new NtfyTarget(new Uri("https://ntfy.example.com/base"), "avala-k3j9"));

        Assert.Equal("https://ntfy.example.com/base/", payload.Address.AbsoluteUri);
        Assert.Equal(
            """{"topic":"avala-k3j9","title":"A decision needs you · ledger-api","message":"Fix the flaky login test","priority":4,"tags":["avala","decision"]}""",
            payload.Body);
    }

    [Fact]
    public void AWebhookCarriesTheEventJobRepositoryTitleSeverityAndTime()
    {
        var message = Composer.Compose([Samples.Decision(Samples.Job)], DetailLevel.Minimal, Redact, Samples.Monday);

        var payload = Payloads.Webhook(message, new WebhookTarget(new Uri("https://hooks.example.com/avala")));

        using var json = JsonDocument.Parse(payload.Body);
        var root = json.RootElement;
        Assert.Equal(
            ("decision", Samples.Job.Value.ToString("D"), "ledger-api", "A decision needs you · ledger-api", "urgent", "2026-10-12T09:00:00Z", JsonValueKind.Null),
            (root.GetProperty("event").GetString(), root.GetProperty("job").GetString(), root.GetProperty("repository").GetString(), root.GetProperty("title").GetString(), root.GetProperty("severity").GetString(), root.GetProperty("at").GetString(), root.GetProperty("link").ValueKind));
    }

    [Fact]
    public void AWebhookIsSignedLikeATriggerWebhookOverTheTimestampNonceAndBody()
    {
        var expected = "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("s3cret"), Encoding.UTF8.GetBytes("1760000000.n-1.{\"a\":1}")));

        Assert.Equal(expected, Payloads.Signature("s3cret", "1760000000", "n-1", "{\"a\":1}"));
    }

    [Fact]
    public void ThePreviewShowsExactlyTheRequestTheChannelSends()
    {
        var ntfy = new NotificationSettings(ChannelKind.Ntfy) { Ntfy = new NtfyTarget(NtfyTarget.DefaultServer, "avala-k3j9") };
        var webhook = new NotificationSettings(ChannelKind.Webhook) { Webhook = new WebhookTarget(new Uri("https://hooks.example.com/avala")) { SecretEnv = "HOOK" }, Detail = DetailLevel.Detailed };

        var sent = Payloads.Ntfy(Composer.Compose([Previews.Sample(Samples.Monday)], DetailLevel.Minimal, Redact, Samples.Monday), Outcomes.Present(ntfy.Ntfy));

        Assert.Equal($"POST https://ntfy.sh/\n\n{sent.Body}", Previews.Of(ntfy, Redact, Samples.Monday));
        Assert.StartsWith("POST https://hooks.example.com/avala\nX-Avala-Signature: sha256=…\n\n{\"event\":\"decision\"", Previews.Of(webhook, Redact, Samples.Monday), StringComparison.Ordinal);
        Assert.Contains("Command: dotnet test --filter Login", Previews.Of(webhook, Redact, Samples.Monday), StringComparison.Ordinal);
    }
}
