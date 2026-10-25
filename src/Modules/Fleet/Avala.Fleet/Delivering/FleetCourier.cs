using Avala.Fleet.Connecting;
using Avala.Fleet.Delivery;
using Avala.Fleet.Recording;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Fleet.Delivering;

internal sealed class FleetCourier(Outbox outbox, IFleetServer server, FleetSession session, TimeProvider clock) : IHandle<StartupCompleted>, IAsyncDisposable
{
    public const int BatchSize = 500;

    private readonly SerialExecutor control = new();

    private readonly CancellationTokenSource stopping = new();

    private ITimer? alarm;

    private DateTimeOffset armedFor = DateTimeOffset.MaxValue;

    private DateTimeOffset backedOffUntil = DateTimeOffset.MinValue;

    private int failures;

    private int disposed;

    public static TimeSpan Debounce { get; } = TimeSpan.FromSeconds(2);

    public async ValueTask HandleAsync(StartupCompleted integrationEvent, CancellationToken cancellationToken)
    {
        var stop = stopping.Token;
        outbox.WhenAppended(() =>
        {
            var due = clock.GetUtcNow() + Debounce;
            _ = control.RunAsync(_ => ArmAsync(Later(due)), stop);
        });
        await control.RunAsync(_ => ArmAsync(clock.GetUtcNow()), cancellationToken);
    }

    public Task DrainedAsync(CancellationToken cancellationToken) => control.RunAsync(_ => Task.CompletedTask, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        outbox.WhenAppended(() => { });
        await stopping.CancelAsync();
        Interlocked.Exchange(ref alarm, null)?.Dispose();
        await control.DisposeAsync();
        stopping.Dispose();
    }

    private DateTimeOffset Later(DateTimeOffset due) => due > backedOffUntil ? due : backedOffUntil;

    private Task ArmAsync(DateTimeOffset due)
    {
        if (due < armedFor)
        {
            armedFor = due;
            Interlocked.Exchange(ref alarm, clock.CreateAlarm(Ring, due))?.Dispose();
        }

        return Task.CompletedTask;
    }

    private void Ring() => _ = control.RunAsync(RoundAsync, stopping.Token);

    private async Task RoundAsync(CancellationToken cancellationToken)
    {
        armedFor = DateTimeOffset.MaxValue;
        Interlocked.Exchange(ref alarm, null)?.Dispose();

        try
        {
            await session.Credentials().Match(
                credentials => DeliverAsync(credentials, cancellationToken),
                () => Task.CompletedTask);
        }
        catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            outbox.RoundFailed(failure);
            await BackOffAsync();
        }
    }

    private async Task DeliverAsync(FleetCredentials credentials, CancellationToken cancellationToken)
    {
        var scope = credentials.Scope.Id;
        var now = clock.GetUtcNow();
        var due = await outbox.RunAsync(
            async (store, token) =>
            {
                if (await store.ExpireAsync(scope, now, token) > 0)
                {
                    outbox.Changes.Raise();
                }

                return await store.DueAsync(scope, now, BatchSize, token);
            },
            cancellationToken);

        if (due.Count == 0)
        {
            await ArmNextAsync(scope, cancellationToken);

            return;
        }

        var sent = await server.SendAsync(credentials.Scope.Server, credentials.Key, [.. due.Select(fact => fact.Body)], cancellationToken);

        switch (sent.Answer)
        {
            case ServerAnswer.Answered:
                failures = 0;
                backedOffUntil = DateTimeOffset.MinValue;
                outbox.Answered();
                await SettleAsync(scope, due, sent.Receipt, cancellationToken);
                await (due.Count == BatchSize ? ArmAsync(clock.GetUtcNow()) : ArmNextAsync(scope, cancellationToken));
                break;
            case ServerAnswer.KeyRefused:
                await session.RefusedAsync(credentials.Scope, cancellationToken);
                break;
            default:
                outbox.Unanswered(now);
                await BackOffAsync();
                break;
        }
    }

    private Task BackOffAsync()
    {
        failures++;
        backedOffUntil = clock.GetUtcNow() + Backoff.After(failures);

        return ArmAsync(backedOffUntil);
    }

    private async Task SettleAsync(string scope, IReadOnlyList<StoredFact> due, Option<Receipt> receipt, CancellationToken cancellationToken)
    {
        var plan = ReceiptPlan.Of([.. due.Select(fact => fact.Id)], receipt.Match(found => found, () => new Receipt([], [], [], [])));
        var settled = await outbox.RunAsync(
            async (store, token) =>
            {
                if (!session.Scope.Match(current => current.Id == scope, () => false))
                {
                    return false;
                }

                await store.SettleAsync(scope, plan, clock.GetUtcNow(), token);

                return true;
            },
            cancellationToken);

        if (settled)
        {
            var kinds = due.ToDictionary(fact => fact.Id, fact => fact.Kind);

            foreach (var rejected in plan.Rejected)
            {
                outbox.Rejected(kinds[rejected.Id], rejected.Reason);
            }
        }

        outbox.Changes.Raise();
    }

    private async Task ArmNextAsync(string scope, CancellationToken cancellationToken)
    {
        var next = await outbox.RunAsync((store, token) => store.NextDueAsync(scope, token), cancellationToken);

        await next.Match(ArmAsync, () => Task.CompletedTask);
    }
}
