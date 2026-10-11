using System.Collections.Immutable;
using Avala.Notifications.Composing;
using Avala.Notifications.Contracts;
using Avala.Notifications.Delivering;
using Avala.Notifications.Notices;
using Avala.Notifications.Pacing;
using Avala.Sdk;

namespace Avala.Notifications.Dispatching;

internal sealed record Retry(OutgoingMessage Message, ImmutableList<Pending> Entries, int Attempt, DateTimeOffset Due);

internal sealed class Dispatcher(Outlook outlook, SentLog log, Courier courier) : IAsyncDisposable
{
    private static readonly NotificationKind[] Always = [NotificationKind.Summary, NotificationKind.Digest, NotificationKind.Test];

    private readonly SerialExecutor executor = new();
    private Outbox outbox = Outbox.Empty;
    private ImmutableList<Pending> held = [];
    private DateTimeOffset quietEnd = DateTimeOffset.MaxValue;
    private ImmutableList<Retry> retries = [];
    private ITimer? alarm;

    public IReadOnlyList<NotificationRecord> Recent => log.Recent;

    public Task OfferAsync(Notice notice, CancellationToken cancellationToken) =>
        RunAsync(
            async token =>
            {
                if ((await outlook.SettingsAsync(token)).Match(settings => Wants(settings, notice.Kind), () => false))
                {
                    outbox = outbox.Offer(notice, outlook.Now);
                }
            },
            cancellationToken);

    public Task ResolveAsync(IReadOnlyList<string> handles, CancellationToken cancellationToken) =>
        handles.Count == 0
            ? Task.CompletedTask
            : RunAsync(
                async token =>
                {
                    foreach (var handle in handles)
                    {
                        await ResolveAsync(handle, token);
                    }
                },
                cancellationToken);

    public Task<Result<NotificationRecord, NotificationError>> TestAsync(CancellationToken cancellationToken) =>
        executor.RunAsync(
            async token =>
            {
                await log.LoadedAsync(outlook.Now, token);

                return await (await outlook.SettingsAsync(token)).Match(
                    async settings =>
                    {
                        var sent = await courier.SendAsync(Composer.Test(outlook.Now), settings, token);
                        var record = Record(NotificationKind.Test, Severity.Info, sent.IsSuccess ? NotificationOutcome.Sent : NotificationOutcome.Failed, outlook.Now) with
                        {
                            Attempts = 1,
                            Error = sent.Match(_ => Option<NotificationError>.None, error => error),
                        };
                        await log.RecordAsync(record, token);

                        return sent.Map(_ => record);
                    },
                    () => Task.FromResult(Result<NotificationRecord, NotificationError>.Failure(NotificationError.NotSetUp)));
            },
            cancellationToken);

    public Task DrainedAsync(CancellationToken cancellationToken) => executor.RunAsync(_ => Task.CompletedTask, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref alarm, null)?.Dispose();
        await executor.DisposeAsync();
    }

    private static bool Wants(NotificationSettings settings, NotificationKind kind) => Always.Contains(kind) || settings.Events.Contains(kind);

    private Task RunAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        executor.RunAsync(
            async token =>
            {
                await log.LoadedAsync(outlook.Now, token);
                await operation(token);
                Arm();
            },
            cancellationToken);

    private async Task ResolveAsync(string handle, CancellationToken cancellationToken)
    {
        var (left, withdrawn) = outbox.Resolve(handle);
        outbox = left;
        var quieted = held.Where(entry => entry.Handles.Contains(handle)).ToList();
        held = held.RemoveRange(quieted);
        retries = [.. retries.Select(retry => retry with { Entries = retry.Entries.RemoveAll(entry => entry.Handles.Contains(handle)) }).Where(retry => !retry.Entries.IsEmpty)];

        var at = outlook.Now;

        foreach (var entry in withdrawn.Concat(quieted))
        {
            await log.SettleAsync(entry, Record(entry, NotificationOutcome.Withdrawn, at), cancellationToken);
        }

        await log.ForgetAsync(handle, cancellationToken);
    }

    private void Ring() => _ = RunAsync(TickAsync, CancellationToken.None);

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        var (left, ripe) = outbox.Ripen(outlook.Now);
        outbox = left;

        await (await outlook.SettingsAsync(cancellationToken)).Match(
            async settings =>
            {
                await TriageAsync(ripe, settings, cancellationToken);
                await RetryAsync(settings, cancellationToken);
                await DigestAsync(settings, cancellationToken);
            },
            () =>
            {
                retries = [];
                held = [];

                return Task.CompletedTask;
            });
    }

    private async Task TriageAsync(IReadOnlyList<Pending> ripe, NotificationSettings settings, CancellationToken cancellationToken)
    {
        if (ripe.Count == 0)
        {
            return;
        }

        var verdicts = Triage.Of(ripe, outlook.Circumstances(settings, log.Subjects, log.IsCapped(outlook.Now)));
        outbox = outbox.Later(verdicts.Later);

        var at = outlook.Now;

        foreach (var settled in verdicts.Settled)
        {
            await log.SettleAsync(settled.Entry, Record(settled.Entry, settled.Outcome, at), cancellationToken);
        }

        foreach (var quiet in verdicts.Held)
        {
            await log.SettleAsync(quiet, Record(quiet, NotificationOutcome.Quiet, at), cancellationToken);
        }

        held = held.AddRange(verdicts.Held);
        quietEnd = outlook.QuietUntil(settings).Match(end => end, () => quietEnd);

        if (verdicts.Send.Count > 0)
        {
            var message = Composer.Compose([.. verdicts.Send.Select(entry => entry.Notice)], settings.Detail, outlook.Redact, outlook.Now);
            await DeliverAsync(new Retry(message, [.. verdicts.Send], 1, outlook.Now), settings, cancellationToken);
        }
    }

    private async Task RetryAsync(NotificationSettings settings, CancellationToken cancellationToken)
    {
        var due = retries.Where(retry => retry.Due <= outlook.Now).ToList();
        retries = retries.RemoveRange(due);

        foreach (var retry in due)
        {
            await DeliverAsync(retry, settings, cancellationToken);
        }
    }

    private async Task DigestAsync(NotificationSettings settings, CancellationToken cancellationToken)
    {
        if (held.IsEmpty || quietEnd > outlook.Now || outlook.QuietUntil(settings).IsSome)
        {
            return;
        }

        var now = outlook.Circumstances(settings, [], log.IsCapped(outlook.Now));

        var digest = held;
        held = [];
        quietEnd = DateTimeOffset.MaxValue;
        var outcome = !now.Away ? NotificationOutcome.Present : now.Capped ? NotificationOutcome.Capped : NotificationOutcome.Sent;

        if (outcome != NotificationOutcome.Sent)
        {
            var at = outlook.Now;

            foreach (var entry in digest)
            {
                await log.SettleAsync(entry, Record(entry, outcome, at), cancellationToken);
            }

            return;
        }

        await DeliverAsync(new Retry(Composer.Digest([.. digest.Select(entry => entry.Notice)], outlook.Now), digest, 1, outlook.Now), settings, cancellationToken);
    }

    private async Task DeliverAsync(Retry attempt, NotificationSettings settings, CancellationToken cancellationToken)
    {
        var sent = await courier.SendAsync(attempt.Message, settings, cancellationToken);

        if (!sent.TryGetValue(out _, out var error) && Courier.Retryable(error) && attempt.Attempt <= Courier.Backoff.Count)
        {
            retries = retries.Add(attempt with { Attempt = attempt.Attempt + 1, Due = outlook.Now + Courier.Backoff[attempt.Attempt - 1] });

            return;
        }

        var at = outlook.Now;

        foreach (var entry in attempt.Entries)
        {
            await log.SettleAsync(entry, Record(entry, sent.IsSuccess ? NotificationOutcome.Sent : NotificationOutcome.Failed, at) with
            {
                Attempts = attempt.Attempt,
                Error = sent.IsSuccess ? Option<NotificationError>.None : error,
            }, cancellationToken);
        }
    }

    private void Arm()
    {
        var next = new[]
        {
            outbox.NextDue.Match(due => due, () => DateTimeOffset.MaxValue),
            held.IsEmpty ? DateTimeOffset.MaxValue : quietEnd,
            retries.IsEmpty ? DateTimeOffset.MaxValue : retries.Min(retry => retry.Due),
        }.Min();

        var armed = next == DateTimeOffset.MaxValue ? null : outlook.Alarm(Ring, next);
        Interlocked.Exchange(ref alarm, armed)?.Dispose();
    }

    private static NotificationRecord Record(Pending entry, NotificationOutcome outcome, DateTimeOffset at) =>
        Record(entry.Notice.Kind, entry.Notice.Severity, outcome, at) with
        {
            Job = entry.Notice.Job,
            Repository = entry.Notice.Repository,
            Count = entry.Notice.Count,
        };

    private static NotificationRecord Record(NotificationKind kind, Severity severity, NotificationOutcome outcome, DateTimeOffset at) =>
        new(Guid.NewGuid(), kind, severity, at, outcome);
}
