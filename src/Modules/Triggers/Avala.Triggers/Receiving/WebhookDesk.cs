using System.Text;
using Avala.Sdk;
using Avala.Triggers.Catalog;
using Avala.Triggers.Contracts;
using Avala.Triggers.Declarations;
using Avala.Triggers.Firing;
using Avala.Triggers.Scheduling;
using Avala.Triggers.Webhooks;

namespace Avala.Triggers.Receiving;

internal sealed class WebhookDesk(TriggerCatalog catalog, ScheduleBook book, WebhookGate gate, TriggerFirer firer) : IAsyncDisposable
{
    private readonly SerialExecutor desk = new();

    public Task<WebhookAnswer> ReceiveAsync(WebhookRequest request, CancellationToken cancellationToken) =>
        desk.RunAsync(token => AnswerAsync(request, token), cancellationToken);

    public ValueTask DisposeAsync() => desk.DisposeAsync();

    private async Task<WebhookAnswer> AnswerAsync(WebhookRequest request, CancellationToken cancellationToken)
    {
        var delivery = new WebhookDelivery(Guid.CreateVersion7(), gate.Clock.GetUtcNow(), $"/hooks/{request.Name}", DeliveryVerdict.Accepted)
        {
            Digest = request.TooLarge ? Option<string>.None : WebhookSignature.Digest(request.Body),
            Nonce = request.Header(WebhookSignature.NonceHeader),
        };

        if (!string.Equals(request.Method, "POST", StringComparison.Ordinal))
        {
            return await AnsweredAsync(delivery with { Verdict = DeliveryVerdict.NotPost }, TimeSpan.Zero, cancellationToken);
        }

        return await catalog.Current.Hook(request.Name).Match(
            trigger => CheckedAsync(trigger, request, delivery with { Trigger = trigger.Id }, cancellationToken),
            () => AnsweredAsync(delivery with { Verdict = DeliveryVerdict.UnknownTrigger }, TimeSpan.Zero, cancellationToken));
    }

    private async Task<WebhookAnswer> CheckedAsync(TriggerDeclaration trigger, WebhookRequest request, WebhookDelivery delivery, CancellationToken cancellationToken)
    {
        if (request.TooLarge)
        {
            return await AnsweredAsync(delivery with { Verdict = DeliveryVerdict.TooLarge }, TimeSpan.Zero, cancellationToken);
        }

        var (verdict, retry) = trigger.Webhook.Match(rule => gate.Check(trigger, rule, request), () => (DeliveryVerdict.UnknownTrigger, TimeSpan.Zero));

        if (verdict == DeliveryVerdict.Accepted && !book.Enabled(trigger))
        {
            verdict = DeliveryVerdict.Disabled;
        }

        if (verdict != DeliveryVerdict.Accepted)
        {
            return await AnsweredAsync(delivery with { Verdict = verdict }, retry, cancellationToken);
        }

        var fired = await firer.FireAsync(
            trigger,
            new FireRequest(TriggerOrigin.Webhook, $"webhook {delivery.Id:N}") { Payload = Encoding.UTF8.GetString(request.Body), Delivery = delivery.Id },
            cancellationToken);

        return await AnsweredAsync(delivery with { Run = fired.Match(run => Option<Guid>.Some(run.Id), _ => Option<Guid>.None) }, TimeSpan.Zero, cancellationToken);
    }

    private async Task<WebhookAnswer> AnsweredAsync(WebhookDelivery delivery, TimeSpan retry, CancellationToken cancellationToken)
    {
        var recorded = await gate.RecordAsync(delivery, cancellationToken);

        return new WebhookAnswer(WebhookSignature.StatusOf(recorded.Verdict), recorded.Verdict, recorded.Run, retry);
    }
}
