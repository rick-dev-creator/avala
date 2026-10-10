using System.Text.Json;
using Avala.Sdk;
using Avala.Triggers.Contracts;
using Avala.Triggers.Declarations;
using Avala.Triggers.Firing;
using Avala.Triggers.Webhooks;

namespace Avala.Triggers.Receiving;

internal sealed record WebhookRequest(string Method, string Name, IReadOnlyDictionary<string, string> Headers, byte[] Body, bool TooLarge)
{
    public Option<string> Header(string name) =>
        Headers.FirstOrDefault(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)).Value.ToOption();
}

internal sealed record WebhookAnswer(int Status, DeliveryVerdict Verdict, Option<Guid> Run, TimeSpan RetryAfter);

internal interface ISecrets
{
    Option<string> Of(string variable);
}

internal sealed class WebhookGate(ISecrets secrets, RunJournal journal) : IStartupTask
{
    private ReplayGuard replays = ReplayGuard.Empty;
    private RateWindow rates = RateWindow.Empty;

    public TimeProvider Clock => journal.Clock;

    public Task RunAsync(CancellationToken cancellationToken)
    {
        var now = Clock.GetUtcNow();

        foreach (var delivery in journal.Deliveries.Where(delivery => delivery.Verdict == DeliveryVerdict.Accepted && now - delivery.At <= ReplayGuard.Window * 2))
        {
            replays = delivery.Trigger.Match(
                trigger => delivery.Nonce.Match(nonce => replays.Accept(trigger.Key, nonce, delivery.At), () => replays),
                () => replays);
        }

        return Task.CompletedTask;
    }

    public (DeliveryVerdict Verdict, TimeSpan RetryAfter) Check(TriggerDeclaration trigger, WebhookRule rule, WebhookRequest request)
    {
        var now = Clock.GetUtcNow();
        (rates, var retry) = rates.Admit(trigger.Id.Key, now, rule.RatePerHour);

        if (retry > TimeSpan.Zero)
        {
            return (DeliveryVerdict.RateLimited, retry);
        }

        return (Signed(trigger, rule, request, now), TimeSpan.Zero);
    }

    public Task<WebhookDelivery> RecordAsync(WebhookDelivery delivery, CancellationToken cancellationToken) =>
        journal.DeliveredAsync(delivery, cancellationToken);

    private DeliveryVerdict Signed(TriggerDeclaration trigger, WebhookRule rule, WebhookRequest request, DateTimeOffset now)
    {
        var timestamp = OrEmpty(request.Header(WebhookSignature.TimestampHeader));
        var nonce = OrEmpty(request.Header(WebhookSignature.NonceHeader));
        var signature = OrEmpty(request.Header(WebhookSignature.SignatureHeader));

        if (timestamp.Length == 0 || signature.Length == 0 || !WebhookSignature.WellFormedNonce(nonce))
        {
            return DeliveryVerdict.NotSigned;
        }

        var secret = OrEmpty(secrets.Of(rule.SecretVariable));

        if (secret.Length == 0)
        {
            return DeliveryVerdict.NoSecret;
        }

        if (!WebhookSignature.Verifies(secret, timestamp, nonce, request.Body, signature))
        {
            return DeliveryVerdict.BadSignature;
        }

        if (!WebhookSignature.TryTimestamp(timestamp, out var at) || !ReplayGuard.Fresh(at, now))
        {
            return DeliveryVerdict.Stale;
        }

        if (replays.Seen(trigger.Id.Key, nonce, now))
        {
            return DeliveryVerdict.Replayed;
        }

        replays = replays.Accept(trigger.Id.Key, nonce, now);

        return JsonObject(request.Body) ? DeliveryVerdict.Accepted : DeliveryVerdict.Malformed;
    }

    private static string OrEmpty(Option<string> text) => text.Match(found => found, () => string.Empty);

    private static bool JsonObject(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
