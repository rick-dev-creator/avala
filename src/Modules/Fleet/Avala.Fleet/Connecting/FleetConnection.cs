using System.Runtime.CompilerServices;
using Avala.Fleet.Connection;
using Avala.Fleet.Contracts;
using Avala.Fleet.Delivering;
using Avala.Fleet.Facts;
using Avala.Fleet.Recording;
using Avala.Sdk;

namespace Avala.Fleet.Connecting;

internal enum ConnectOutcome
{
    Connected,
    InvalidAddress,
    InsecureAddress,
    NotAKey,
    NoKeychain,
    KeychainFailed,
    Unreachable,
    KeyRefused,
    NotConnected,
    Disconnected,
    DisconnectedKeyKept,
    KeyNotRemoved,
    Untrusted,
}

internal enum FleetState
{
    NotConnected,
    Connected,
    Paused,
    KeyRefused,
    KeychainUnavailable,
}

internal sealed record FleetStatus(
    FleetState State,
    string Server,
    string InstanceName,
    int Waiting,
    Option<DateTimeOffset> LastDelivery,
    Option<DateTimeOffset> UnansweredSince,
    bool Untrusted,
    IReadOnlyList<RejectionNote> Rejections,
    int Dropped)
{
    public static FleetStatus Disconnected { get; } =
        new(FleetState.NotConnected, string.Empty, string.Empty, 0, Option<DateTimeOffset>.None, Option<DateTimeOffset>.None, Untrusted: false, [], 0);
}

internal sealed class FleetConnection(FleetSession session, Outbox outbox, IFleetServer server, IKeychain keychain)
{
    public async Task<ConnectOutcome> ConnectAsync(string address, string key, CancellationToken cancellationToken)
    {
        var parsed = ServerAddress.Create(address);
        var trimmed = key.Trim();

        if (!parsed.TryGetValue(out var origin, out var error))
        {
            return error == FleetError.InsecureAddress ? ConnectOutcome.InsecureAddress : ConnectOutcome.InvalidAddress;
        }

        if (!KeyShape.Matches(trimmed))
        {
            return ConnectOutcome.NotAKey;
        }

        if (!await keychain.AvailableAsync(cancellationToken))
        {
            return ConnectOutcome.NoKeychain;
        }

        var identified = await server.IdentifyAsync(origin.Origin, trimmed, cancellationToken);

        return await identified.Identity.Match(
            identity => KeepAsync(origin.Origin, identity, trimmed, cancellationToken),
            () => Task.FromResult(Outcome(identified.Answer)));
    }

    public async Task<ConnectOutcome> TestAsync(CancellationToken cancellationToken) =>
        await session.ReadingCredentials().Match(
            async credentials => Outcome((await server.IdentifyAsync(credentials.Scope.Server, credentials.Key, cancellationToken)).Answer),
            () => Task.FromResult(session.State == ConnectionState.KeyRefused ? ConnectOutcome.KeyRefused : ConnectOutcome.NotConnected));

    public async Task<ConnectOutcome> RetryAsync(CancellationToken cancellationToken)
    {
        if (!await session.RetriedAsync(cancellationToken))
        {
            return session.State == ConnectionState.KeychainUnavailable ? ConnectOutcome.KeychainFailed : ConnectOutcome.NotConnected;
        }

        outbox.Nudge();

        return await TestAsync(cancellationToken);
    }

    public async Task<FleetStatus> PauseAsync(bool paused, CancellationToken cancellationToken)
    {
        await outbox.RunAsync((_, token) => session.PausedAsync(paused, token), cancellationToken);

        if (!paused)
        {
            outbox.Nudge();
        }

        return await StatusAsync(cancellationToken);
    }

    public async Task<ConnectOutcome> DisconnectAsync(CancellationToken cancellationToken)
    {
        var removed = await session.Settings.Match(
            found => keychain.RemoveAsync(found.Server, cancellationToken).AsTask(),
            () => Task.FromResult(Result<string, KeychainFailure>.Success(string.Empty)));

        var keyKept = !removed.IsSuccess;

        if (keyKept && session.State != ConnectionState.KeychainUnavailable)
        {
            return ConnectOutcome.KeyNotRemoved;
        }

        await outbox.RunAsync(
            async (store, token) =>
            {
                await session.ForgottenAsync(token);
                await store.ForgetAsync(token);

                return true;
            },
            cancellationToken);
        outbox.Answered();
        outbox.Changes.Raise();

        return keyKept ? ConnectOutcome.DisconnectedKeyKept : ConnectOutcome.Disconnected;
    }

    public async Task<FleetStatus> StatusAsync(CancellationToken cancellationToken) =>
        await session.Settings.Match(
            async found =>
            {
                var summary = await outbox.RunAsync((store, token) => store.SummaryAsync(found.Scope.Id, token), cancellationToken);

                return new FleetStatus(State(session.State), found.Server, found.InstanceName, summary.Waiting, summary.LastDelivery, outbox.UnansweredSince, outbox.Untrusted, summary.Rejections, summary.Dropped);
            },
            () => Task.FromResult(FleetStatus.Disconnected));

    public async IAsyncEnumerable<FleetStatus> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var sessionChanges = session.Changes.Watch(cancellationToken);
        var outboxChanges = outbox.Changes.Watch(cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            var changed = await Task.WhenAny(sessionChanges.WaitToReadAsync(cancellationToken).AsTask(), outboxChanges.WaitToReadAsync(cancellationToken).AsTask());

            if (!await changed)
            {
                yield break;
            }

            Drain(sessionChanges);
            Drain(outboxChanges);

            yield return await StatusAsync(cancellationToken);
        }
    }

    private async Task<ConnectOutcome> KeepAsync(string origin, Identity identity, string key, CancellationToken cancellationToken)
    {
        var previous = session.Settings;

        if (!(await keychain.StoreAsync(origin, key, cancellationToken)).IsSuccess)
        {
            return ConnectOutcome.KeychainFailed;
        }

        await previous.Match(
            found => found.Server == origin ? Task.CompletedTask : keychain.RemoveAsync(found.Server, cancellationToken).AsTask(),
            () => Task.CompletedTask);

        var settings = new FleetSettings(origin, identity.Id, identity.Name, Enabled: true, KeyRefused: false);
        var kept = await outbox.RunAsync(
            async (store, token) =>
            {
                if (!await session.ConnectedAsync(settings, key, token))
                {
                    return false;
                }

                await store.KeepOnlyAsync(settings.Scope.Id, token);

                return true;
            },
            cancellationToken);

        if (!kept)
        {
            return ConnectOutcome.KeychainFailed;
        }

        outbox.Answered();
        outbox.Nudge();
        outbox.Changes.Raise();

        return ConnectOutcome.Connected;
    }

    private static void Drain(System.Threading.Channels.ChannelReader<bool> reader)
    {
        while (reader.TryRead(out _))
        {
        }
    }

    private static ConnectOutcome Outcome(ServerAnswer answer) => answer switch
    {
        ServerAnswer.Answered => ConnectOutcome.Connected,
        ServerAnswer.KeyRefused => ConnectOutcome.KeyRefused,
        ServerAnswer.Untrusted => ConnectOutcome.Untrusted,
        _ => ConnectOutcome.Unreachable,
    };

    private static FleetState State(ConnectionState state) => state switch
    {
        ConnectionState.Connected => FleetState.Connected,
        ConnectionState.Paused => FleetState.Paused,
        ConnectionState.KeyRefused => FleetState.KeyRefused,
        ConnectionState.KeychainUnavailable => FleetState.KeychainUnavailable,
        _ => FleetState.NotConnected,
    };
}
