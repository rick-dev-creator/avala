using System.Text;
using System.Text.Json;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Triggers.Catalog;
using Avala.Triggers.Contracts;
using Avala.Triggers.Declarations;
using Avala.Triggers.Webhooks;

namespace Avala.Triggers.Firing;

internal sealed class TriggerFirer(TriggerCatalog catalog, FiringChecks checks, TriggerDispatch dispatch, RunJournal journal) : IAsyncDisposable
{
    private readonly SerialExecutor line = new();

    public RunJournal Journal => journal;

    public Task<Result<TriggerRun, TriggerError>> FireAsync(TriggerId trigger, FireRequest request, CancellationToken cancellationToken) =>
        line.RunAsync(
            async token => await catalog.Current.Find(trigger).Match(
                declared => FireDeclaredAsync(declared, request, token),
                () => Task.FromResult(Result<TriggerRun, TriggerError>.Failure(TriggerError.UnknownTrigger))),
            cancellationToken);

    public Task<Result<TriggerRun, TriggerError>> FireAsync(TriggerDeclaration trigger, FireRequest request, CancellationToken cancellationToken) =>
        line.RunAsync(token => FireDeclaredAsync(trigger, request, token), cancellationToken);

    public Task<TriggerRun> MissedAsync(TriggerDeclaration trigger, int missed, CancellationToken cancellationToken) =>
        line.RunAsync(
            token => journal.RecordAsync(Run(trigger, new FireRequest(TriggerOrigin.Schedule, "schedule"), RunOutcome.Missed) with { Missed = missed }, token),
            cancellationToken);

    public ValueTask DisposeAsync() => line.DisposeAsync();

    private async Task<Result<TriggerRun, TriggerError>> FireDeclaredAsync(TriggerDeclaration trigger, FireRequest request, CancellationToken cancellationToken)
    {
        if (!Payload(request.Payload).TryGetValue(out var document, out var error))
        {
            return error;
        }

        try
        {
            return await FireWithAsync(trigger, request, document.Map(parsed => parsed.RootElement), cancellationToken);
        }
        finally
        {
            _ = document.Match(parsed => { parsed.Dispose(); return true; }, () => false);
        }
    }

    private async Task<TriggerRun> FireWithAsync(TriggerDeclaration trigger, FireRequest request, Option<JsonElement> payload, CancellationToken cancellationToken)
    {
        var run = Run(trigger, request, RunOutcome.Submitted);

        if (await checks.RunningAsync(trigger.Id, cancellationToken) >= trigger.Concurrency)
        {
            return await journal.RecordAsync(run with { Outcome = RunOutcome.AtConcurrencyCap }, cancellationToken);
        }

        var applied = await checks.AutonomyAsync(trigger, cancellationToken);
        var instruction = trigger.Instruction.Render(new TemplateValues(
            trigger.Id,
            trigger.Repository,
            TimeZoneInfo.ConvertTime(run.At, journal.Clock.LocalTimeZone),
            payload));

        return await journal.RecordAsync(await DispatchAsync(run with { Applied = applied }, trigger, instruction, cancellationToken), cancellationToken);
    }

    private async Task<TriggerRun> DispatchAsync(TriggerRun run, TriggerDeclaration trigger, string instruction, CancellationToken cancellationToken) =>
        trigger.Target == TriggerTarget.Loop
            ? (await dispatch.EnqueueAsync(run.Id, trigger, instruction, run.Applied, cancellationToken)).Match(
                _ => run with { Outcome = RunOutcome.Enqueued },
                rejection => run with { Outcome = RunOutcome.Rejected, Rejection = rejection })
            : (await dispatch.SubmitAsync(trigger, instruction, run.Applied, cancellationToken)).Match(
                job => run with { Outcome = RunOutcome.Submitted, Job = job },
                rejection => run with { Outcome = RunOutcome.Rejected, Rejection = rejection });

    private TriggerRun Run(TriggerDeclaration trigger, FireRequest request, RunOutcome outcome) =>
        new(Guid.CreateVersion7(), trigger.Id, request.Origin, request.Who, journal.Clock.GetUtcNow(), outcome)
        {
            PayloadDigest = request.Payload.Map(text => WebhookSignature.Digest(Encoding.UTF8.GetBytes(text))),
            Delivery = request.Delivery,
            Asked = trigger.Autonomy,
            Applied = trigger.Autonomy,
        };

    private static Result<Option<JsonDocument>, TriggerError> Payload(Option<string> text)
    {
        if (text.IsNone)
        {
            return Option<JsonDocument>.None;
        }

        try
        {
            var document = JsonDocument.Parse(text.Match(json => json, () => string.Empty));

            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return Option<JsonDocument>.Some(document);
            }

            document.Dispose();
        }
        catch (JsonException)
        {
            return TriggerError.Malformed;
        }

        return TriggerError.Malformed;
    }
}
