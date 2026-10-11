using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Sifting;
using Avala.Triggers.Contracts;

namespace Avala.Forges.Polling;

internal sealed class ForgeNudges(ForgeConnections connections) : IForgeNudges
{
    public async ValueTask<DeliveryVerdict> VerifyAsync(ForgeRule rule, IReadOnlyDictionary<string, string> headers, ReadOnlyMemory<byte> body, string secret, CancellationToken cancellationToken) =>
        (await connections.UsableAsync(new ForgeName(rule.Connection), cancellationToken)).Match(
            forge => forge is IForgeEvents events ? HookVerification.Check(events.Signature, headers, body.Span, secret) : DeliveryVerdict.BadSignature,
            _ => DeliveryVerdict.BadSignature);
}
