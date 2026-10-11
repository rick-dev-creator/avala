using System.Globalization;
using System.Net.Http.Headers;
using Avala.Notifications.Composing;
using Avala.Notifications.Contracts;
using Avala.Notifications.Delivering;
using Avala.Sdk;

namespace Avala.Notifications.Channels;

internal sealed class WebhookChannel(NotifierHttp http, ISecrets secrets, TimeProvider clock) : INotificationChannel
{
    public ChannelKind Kind => ChannelKind.Webhook;

    public Task<Result<OutgoingMessage, NotificationError>> SendAsync(OutgoingMessage message, NotificationSettings settings, CancellationToken cancellationToken) =>
        settings.Webhook.Match(
            target => Secrets.Named(secrets, target.SecretEnv).Match(
                secret => PostAsync(message, Payloads.Webhook(message, target), secret, cancellationToken),
                error => Task.FromResult(Result<OutgoingMessage, NotificationError>.Failure(error))),
            () => Task.FromResult(Result<OutgoingMessage, NotificationError>.Failure(NotificationError.NotSetUp)));

    private Task<Result<OutgoingMessage, NotificationError>> PostAsync(OutgoingMessage message, Payload payload, Option<string> secret, CancellationToken cancellationToken) =>
        http.PostAsync(message, payload, Signed(payload, secret), Option<AuthenticationHeaderValue>.None, cancellationToken);

    private IReadOnlyList<KeyValuePair<string, string>> Signed(Payload payload, Option<string> secret)
    {
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("N");

        return
        [
            new(Payloads.TimestampHeader, timestamp),
            new(Payloads.NonceHeader, nonce),
            .. secret.Match<KeyValuePair<string, string>[]>(key => [new(Payloads.SignatureHeader, Payloads.Signature(key, timestamp, nonce, payload.Body))], () => []),
        ];
    }
}
