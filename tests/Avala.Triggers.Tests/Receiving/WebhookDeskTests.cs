using System.Globalization;
using System.Text;
using Avala.Triggers.Contracts;
using Avala.Triggers.Receiving;
using Avala.Triggers.Webhooks;

namespace Avala.Triggers.Tests.Receiving;

public sealed class WebhookDeskTests
{
    private const string Secret = "s3cret";

    private const string Body = """{ "issue": { "title": "Totals round wrong" } }""";

    private static string Hook(int rate) =>
        $$$"""{ "id": "issue", "repository": "{{{Triggered.Repository}}}", "instruction": "Triage {{payload.issue.title}}", "webhook": { "secretEnv": "HOOK_SECRET", "ratePerHour": {{{rate}}} } }""";

    [Fact]
    public async Task ASignedFreshDeliveryIsAcceptedAndStartsAnAuditedJobAsync()
    {
        await using var triggered = await StartAsync();

        var answer = await triggered.Desk.ReceiveAsync(Signed(triggered, "n-1"), Triggered.Cancellation);

        var run = await triggered.FiredAsync(_ => true);
        var delivery = Assert.Single(triggered.Journal.Deliveries);
        Assert.Equal((202, DeliveryVerdict.Accepted), (answer.Status, answer.Verdict));
        Assert.Equal("Triage Totals round wrong", Assert.Single(triggered.Jobs.Requests).Instruction);
        Assert.Equal((TriggerOrigin.Webhook, RunOutcome.Submitted, $"webhook {delivery.Id:N}"), (run.Origin, run.Outcome, run.Who));
        Assert.Equal((run.PayloadDigest, Sdk.Option<Guid>.Some(run.Id), Sdk.Option<Guid>.Some(delivery.Id)), (delivery.Digest, delivery.Run, run.Delivery));
        Assert.Equal(WebhookSignature.Digest(Encoding.UTF8.GetBytes(Body)), delivery.Digest.Match(digest => digest, () => string.Empty));
    }

    [Fact]
    public async Task AMissingHeaderAnotherSecretAnotherBodyAndAnOldTimestampAreRejectedAsync()
    {
        await using var triggered = await StartAsync();
        var now = Stamp(triggered);
        var forged = WebhookSignature.Of("other", now, "n-2", Encoding.UTF8.GetBytes(Body));
        var stale = (triggered.Clock.GetUtcNow() - ReplayGuard.Window - TimeSpan.FromSeconds(1)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

        DeliveryVerdict[] verdicts =
        [
            (await triggered.Desk.ReceiveAsync(Signed(triggered, "n-1") with { Headers = new Dictionary<string, string> { [WebhookSignature.NonceHeader] = "n-1" } }, Triggered.Cancellation)).Verdict,
            (await triggered.Desk.ReceiveAsync(Request(Body, now, "n-2", forged), Triggered.Cancellation)).Verdict,
            (await triggered.Desk.ReceiveAsync(Request("""{ "issue": 1 }""", now, "n-3", WebhookSignature.Of(Secret, now, "n-3", Encoding.UTF8.GetBytes(Body))), Triggered.Cancellation)).Verdict,
        ];
        var old = await triggered.Desk.ReceiveAsync(Request(Body, stale, "n-4", WebhookSignature.Of(Secret, stale, "n-4", Encoding.UTF8.GetBytes(Body))), Triggered.Cancellation);

        Assert.Equal([DeliveryVerdict.NotSigned, DeliveryVerdict.BadSignature, DeliveryVerdict.BadSignature], verdicts);
        Assert.Equal((401, DeliveryVerdict.Stale), (old.Status, old.Verdict));
        Assert.Empty(triggered.Jobs.Requests);
    }

    [Fact]
    public async Task ANonceAcceptedWithinTheWindowIsAReplayAndIsForgottenAfterItAsync()
    {
        await using var triggered = await StartAsync();
        _ = await triggered.Desk.ReceiveAsync(Signed(triggered, "n-1"), Triggered.Cancellation);

        var replay = await triggered.Desk.ReceiveAsync(Signed(triggered, "n-1"), Triggered.Cancellation);
        triggered.Clock.Advance(ReplayGuard.Window * 2 + TimeSpan.FromSeconds(1));
        var later = await triggered.Desk.ReceiveAsync(Signed(triggered, "n-1"), Triggered.Cancellation);

        Assert.Equal((409, DeliveryVerdict.Replayed), (replay.Status, replay.Verdict));
        Assert.Equal(DeliveryVerdict.Accepted, later.Verdict);
    }

    [Fact]
    public async Task AReplayIsStillRefusedAfterARestartAsync()
    {
        var triggered = await StartAsync();
        _ = await triggered.Desk.ReceiveAsync(Signed(triggered, "n-1"), Triggered.Cancellation);

        await using var restarted = await triggered.RestartAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(DeliveryVerdict.Replayed, (await restarted.Desk.ReceiveAsync(Signed(restarted, "n-1"), Triggered.Cancellation)).Verdict);
    }

    [Fact]
    public async Task MoreRequestsInAnHourThanItsRateAreRateLimitedWithTheWaitAsync()
    {
        await using var triggered = await StartAsync(rate: 3);

        for (var request = 0; request < 3; request++)
        {
            _ = await triggered.Desk.ReceiveAsync(Signed(triggered, $"n-{request}"), Triggered.Cancellation);
            triggered.Clock.Advance(TimeSpan.FromMinutes(10));
        }

        var limited = await triggered.Desk.ReceiveAsync(Signed(triggered, "n-9"), Triggered.Cancellation);

        Assert.Equal((429, DeliveryVerdict.RateLimited, TimeSpan.FromMinutes(30)), (limited.Status, limited.Verdict, limited.RetryAfter));
    }

    [Fact]
    public async Task OtherMethodsUnknownTriggersLargeBodiesMissingSecretsNonObjectsAndDisabledTriggersFireNothingAsync()
    {
        await using var triggered = await StartAsync();
        var now = Stamp(triggered);
        var array = WebhookSignature.Of(Secret, now, "n-5", Encoding.UTF8.GetBytes("[1]"));

        var answers = new List<WebhookAnswer>
        {
            await triggered.Desk.ReceiveAsync(Signed(triggered, "n-1") with { Method = "GET" }, Triggered.Cancellation),
            await triggered.Desk.ReceiveAsync(Signed(triggered, "n-2") with { Name = "nothing" }, Triggered.Cancellation),
            await triggered.Desk.ReceiveAsync(Signed(triggered, "n-3") with { Body = [], TooLarge = true }, Triggered.Cancellation),
            await triggered.Desk.ReceiveAsync(Request("[1]", now, "n-5", array), Triggered.Cancellation),
        };
        triggered.World.Secrets.Values.Clear();
        answers.Add(await triggered.Desk.ReceiveAsync(Signed(triggered, "n-6"), Triggered.Cancellation));
        triggered.World.Secrets.Values["HOOK_SECRET"] = Secret;
        _ = await triggered.Triggers.EnableAsync(new TriggerId(TriggerId.Machine, "issue"), enabled: false, Triggered.Cancellation);
        answers.Add(await triggered.Desk.ReceiveAsync(Signed(triggered, "n-7"), Triggered.Cancellation));

        Assert.Equal(
            [(405, DeliveryVerdict.NotPost), (404, DeliveryVerdict.UnknownTrigger), (413, DeliveryVerdict.TooLarge), (400, DeliveryVerdict.Malformed), (503, DeliveryVerdict.NoSecret), (409, DeliveryVerdict.Disabled)],
            answers.Select(answer => (answer.Status, answer.Verdict)));
        Assert.Empty(triggered.Jobs.Requests);
        Assert.Equal(6, triggered.Journal.Deliveries.Count);
    }

    private static Task<Triggered> StartAsync(int rate = 60) =>
        Triggered.StartAsync(Declared.Machine(Hook(rate)), world => world.Secrets.Values["HOOK_SECRET"] = Secret);

    private static string Stamp(Triggered triggered) =>
        triggered.Clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    private static WebhookRequest Signed(Triggered triggered, string nonce)
    {
        var now = Stamp(triggered);

        return Request(Body, now, nonce, WebhookSignature.Of(Secret, now, nonce, Encoding.UTF8.GetBytes(Body)));
    }

    private static WebhookRequest Request(string body, string timestamp, string nonce, string signature) =>
        new(
            "POST",
            "issue",
            new Dictionary<string, string>
            {
                [WebhookSignature.TimestampHeader] = timestamp,
                [WebhookSignature.NonceHeader] = nonce,
                [WebhookSignature.SignatureHeader] = signature,
            },
            Encoding.UTF8.GetBytes(body),
            TooLarge: false);
}
