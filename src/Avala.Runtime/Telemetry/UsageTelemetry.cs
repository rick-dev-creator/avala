using Avala.Sdk;
using Avala.Sdk.Telemetry;

namespace Avala.Runtime.Telemetry;

internal sealed class UsageTelemetry(TelemetryFile file, TelemetrySource source, Option<AptabaseSender> sender, TimeProvider clock)
    : ITelemetry, IStartupTask, IAsyncDisposable
{
    private static readonly TimeSpan PastMidnight = TimeSpan.FromSeconds(1);

    private readonly SerialExecutor work = new();
    private bool isLoaded;
    private Option<TelemetryRecord> current;
    private ITimer? midnight;
    private CancellationToken lifetime;
    private Task sending = Task.CompletedTask;

    public Task Sending => Volatile.Read(ref sending);

    private bool Silenced => sender.IsNone || source.OptedOutBy.IsSome;

    public Task RunAsync(CancellationToken cancellationToken)
    {
        if (!Silenced)
        {
            lifetime = cancellationToken;
            Volatile.Write(ref sending, PassAsync(cancellationToken));
        }

        return Task.CompletedTask;
    }

    public Task<TelemetryState> ReadAsync(CancellationToken cancellationToken) =>
        sender.IsNone
            ? Task.FromResult(TelemetryState.NotConfigured)
            : work.RunAsync(async token => State(await LoadedAsync(token)), cancellationToken);

    public Task<string> PreviewAsync(CancellationToken cancellationToken) =>
        sender.IsNone
            ? Task.FromResult(string.Empty)
            : work.RunAsync(
                async token => (await LoadedAsync(token)).Bind(record => record.InstallId).Match(
                    id => source.Payload(TelemetrySource.Active, id, clock.GetUtcNow()),
                    () => string.Empty),
                cancellationToken);

    public Task<TelemetryState> TurnAsync(bool on, CancellationToken cancellationToken) =>
        ChangeAsync(record => record with { Enabled = on, NoticeShown = true }, cancellationToken);

    public Task<TelemetryState> ResetInstallIdAsync(CancellationToken cancellationToken) =>
        ChangeAsync(record => record with { InstallId = TelemetryRecord.NewInstallId() }, cancellationToken);

    public Task<bool> ClaimNoticeAsync(CancellationToken cancellationToken) =>
        Silenced
            ? Task.FromResult(false)
            : work.RunAsync(
                async token =>
                {
                    if ((await LoadedAsync(token)).Match<TelemetryRecord?>(record => record, () => null) is not { Enabled: true, NoticeShown: false } unseen)
                    {
                        return false;
                    }

                    _ = await CommitAsync(unseen with { NoticeShown = true }, token);

                    return true;
                },
                cancellationToken);

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref midnight, null)?.Dispose();

        return work.DisposeAsync();
    }

    private Task<TelemetryState> ChangeAsync(Func<TelemetryRecord, TelemetryRecord> change, CancellationToken cancellationToken) =>
        sender.IsNone
            ? Task.FromResult(TelemetryState.NotConfigured)
            : work.RunAsync(
                async token =>
                {
                    var before = await LoadedAsync(token);
                    var changed = change(before.Match(record => record, () => TelemetryRecord.Fresh with { InstallId = TelemetryRecord.NewInstallId() }));

                    return State(await CommitAsync(changed, token) ? changed : before);
                },
                cancellationToken);

    private async Task PassAsync(CancellationToken cancellationToken)
    {
        try
        {
            var today = Today();
            var due = await work.RunAsync(async token => Due(await LoadedAsync(token), today).ToList(), cancellationToken);

            foreach (var (id, eventName) in due)
            {
                if (await SendAsync(eventName, id, cancellationToken))
                {
                    await work.RunAsync(token => MarkAsync(eventName, today, token), cancellationToken);
                }
            }

            await work.RunAsync(_ => ScheduleAsync(), cancellationToken);
        }
        catch (Exception stopped) when (stopped is OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    private Task<bool> SendAsync(string eventName, string id, CancellationToken cancellationToken) =>
        sender.Match(
            found => found.SendAsync(source.Payload(eventName, id, clock.GetUtcNow()), cancellationToken),
            () => Task.FromResult(false));

    private static IEnumerable<(string Id, string Event)> Due(Option<TelemetryRecord> found, DateOnly today)
    {
        if (found.Match<TelemetryRecord?>(record => record, () => null) is not { Enabled: true } record
            || record.InstallId.Match<string?>(id => id, () => null) is not { } id)
        {
            yield break;
        }

        if (!record.InstalledSent)
        {
            yield return (id, TelemetrySource.Installed);
        }

        if (record.ActiveOn != today)
        {
            yield return (id, TelemetrySource.Active);
        }
    }

    private async Task MarkAsync(string eventName, DateOnly today, CancellationToken cancellationToken)
    {
        if ((await LoadedAsync(cancellationToken)).Match<TelemetryRecord?>(record => record, () => null) is { } record)
        {
            _ = await CommitAsync(
                eventName == TelemetrySource.Installed ? record with { InstalledSent = true } : record with { ActiveOn = today },
                cancellationToken);
        }
    }

    private Task ScheduleAsync()
    {
        var now = clock.GetLocalNow();
        Interlocked.Exchange(ref midnight, null)?.Dispose();
        midnight = clock.CreateTimer(_ => Volatile.Write(ref sending, PassAsync(lifetime)), null, now.Date.AddDays(1) - now.DateTime + PastMidnight, Timeout.InfiniteTimeSpan);

        return Task.CompletedTask;
    }

    private DateOnly Today() => DateOnly.FromDateTime(clock.GetLocalNow().DateTime);

    private async Task<Option<TelemetryRecord>> LoadedAsync(CancellationToken cancellationToken)
    {
        if (!isLoaded)
        {
            current = await file.ReadAsync(cancellationToken);
            isLoaded = true;

            if (current.Match<TelemetryRecord?>(record => record, () => null) is { InstallId.IsNone: true } fresh)
            {
                _ = await CommitAsync(fresh with { InstallId = TelemetryRecord.NewInstallId() }, cancellationToken);
            }
        }

        return current;
    }

    private async Task<bool> CommitAsync(TelemetryRecord record, CancellationToken cancellationToken)
    {
        if (!await file.WriteAsync(record, cancellationToken))
        {
            return false;
        }

        current = record;

        return true;
    }

    private TelemetryState State(Option<TelemetryRecord> shown) =>
        new(
            sender.IsNone ? TelemetryStatus.NotConfigured
            : source.OptedOutBy.IsSome ? TelemetryStatus.OffByEnvironment
            : shown.Match(record => record.Enabled, () => false) ? TelemetryStatus.On
            : TelemetryStatus.Off,
            shown.Bind(record => record.InstallId),
            source.OptedOutBy);
}
