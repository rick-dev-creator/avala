using Avala.Fleet.Connecting;
using Avala.Fleet.Delivering;
using Avala.Fleet.Delivery;
using Avala.Fleet.Facts;
using Avala.Fleet.Recording;
using Avala.Sdk;
using Avala.Sdk.Events;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Fleet.Tests.Delivering;

public sealed class FleetCourierTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeFactStore store = new();

    private readonly FakeFleetServer server = new();

    private readonly FakeKeychain keychain = new();

    private readonly FakeTimeProvider clock = new(Noon);

    private readonly Outbox outbox;

    private FleetCourier? courier;

    public FleetCourierTests() => outbox = new Outbox(store, Microsoft.Extensions.Logging.Abstractions.NullLogger<Outbox>.Instance);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NudgesWithinTwoSecondsAreOneRound()
    {
        await StartAsync();

        await AppendAsync(1);
        clock.Advance(TimeSpan.FromSeconds(1));
        await AppendAsync(2);
        await PassAsync(FleetCourier.Debounce);

        Assert.Equal(2, Assert.Single(server.Batches).Events.Count);
    }

    [Fact]
    public async Task ABatchHoldsAtMostFiveHundredDueFactsOfTheCurrentScopeAndAFullOneIsFollowedAtOnce()
    {
        store.Entries.Add(new StoredEntry("another|scope", Fact(0)));

        foreach (var number in Enumerable.Range(1, 501))
        {
            store.Entries.Add(new StoredEntry(Fleets.Scope, Fact(number)));
        }

        await StartAsync();
        await PassAsync(TimeSpan.Zero);

        Assert.Equal(("500,1", 1), (string.Join(",", server.Batches.Select(batch => batch.Events.Count)), store.Entries.Count));
    }

    [Fact]
    public async Task TheKeyIsSentAsABearerTokenToTheScopesServer()
    {
        store.Entries.Add(new StoredEntry(Fleets.Scope, Fact(1)));

        await StartAsync();
        await PassAsync(TimeSpan.Zero);

        Assert.Equal((Fleets.Server, Fleets.Key), (Assert.Single(server.Batches).Server, server.Batches[0].Key));
    }

    [Fact]
    public async Task AReceiptIsAppliedToTheOutbox()
    {
        store.Entries.AddRange([new StoredEntry(Fleets.Scope, Fact(1)), new StoredEntry(Fleets.Scope, Fact(2)), new StoredEntry(Fleets.Scope, Fact(3))]);
        server.Answers.Enqueue(ids => new SendOutcome(
            ServerAnswer.Answered,
            new Receipt([ids[0]], [], [new ReceiptEntry(ids[1].ToString(), "unknown kind 'x'")], [new ReceiptEntry(ids[2].ToString(), "the job id is empty")])));

        await StartAsync();
        await PassAsync(TimeSpan.Zero);

        Assert.Equal(
            (Id(2), Noon + Backoff.After(1), "the job id is empty"),
            (Assert.Single(store.Entries).Fact.Id, store.Entries[0].DueAt, Assert.Single(store.Rejections).Note.Reason));
    }

    [Fact]
    public async Task AnUnreachableServerBacksOffAndRetries()
    {
        store.Entries.Add(new StoredEntry(Fleets.Scope, Fact(1)));
        server.Answers.Enqueue(_ => new SendOutcome(ServerAnswer.Unreachable, Option<Receipt>.None));
        server.Answers.Enqueue(_ => new SendOutcome(ServerAnswer.Unreachable, Option<Receipt>.None));

        await StartAsync();
        await PassAsync(TimeSpan.Zero);
        await PassAsync(Backoff.After(1));
        var afterTwo = server.Batches.Count;
        await PassAsync(Backoff.After(2) - TimeSpan.FromSeconds(1));
        var beforeThird = server.Batches.Count;
        await PassAsync(TimeSpan.FromSeconds(1));

        Assert.Equal((2, 2, 3, 0), (afterTwo, beforeThird, server.Batches.Count, store.Entries.Count));
    }

    [Fact]
    public async Task ABadRequestBacksOffLikeAnUnreachableServer()
    {
        store.Entries.Add(new StoredEntry(Fleets.Scope, Fact(1)));
        server.Answers.Enqueue(_ => new SendOutcome(ServerAnswer.BadRequest, Option<Receipt>.None));

        await StartAsync();
        await PassAsync(TimeSpan.Zero);
        await PassAsync(Backoff.After(1));

        Assert.Equal((2, 0), (server.Batches.Count, store.Entries.Count));
    }

    [Fact]
    public async Task ARefusedKeyStopsSending()
    {
        store.Entries.Add(new StoredEntry(Fleets.Scope, Fact(1)));
        server.Answers.Enqueue(_ => new SendOutcome(ServerAnswer.KeyRefused, Option<Receipt>.None));

        var session = await StartAsync();
        await PassAsync(TimeSpan.Zero);
        await AppendAsync(2);
        await PassAsync(TimeSpan.FromHours(1));

        Assert.Equal((1, Connection.ConnectionState.KeyRefused), (server.Batches.Count, session.State));
    }

    [Fact]
    public async Task ADeferredFactIsRetriedAfterItsBackoff()
    {
        store.Entries.Add(new StoredEntry(Fleets.Scope, Fact(1)));
        server.Answers.Enqueue(ids => new SendOutcome(ServerAnswer.Answered, new Receipt([], [], [new ReceiptEntry(ids[0].ToString(), "unknown kind 'x'")], [])));

        await StartAsync();
        await PassAsync(TimeSpan.Zero);
        await PassAsync(Backoff.After(1));

        Assert.Equal((2, 0), (server.Batches.Count, store.Entries.Count));
    }

    [Fact]
    public async Task AnExpiredDeferredFactIsDroppedAndCounted()
    {
        store.Entries.Add(new StoredEntry(Fleets.Scope, Fact(1)) { FirstDeferredAt = Noon.AddDays(-31), Attempts = 9 });

        await StartAsync();
        await PassAsync(TimeSpan.Zero);

        Assert.Equal((0, 0, 1), (server.Batches.Count, store.Entries.Count, store.Dropped[Fleets.Scope]));
    }

    [Fact]
    public async Task NothingIsSentUnlessConnected()
    {
        store.Entries.Add(new StoredEntry(Fleets.Scope, Fact(1)));
        keychain.Keys[Fleets.Server] = Fleets.Key;
        courier = new FleetCourier(outbox, server, await Fleets.SessionAsync(keychain, Fleets.Settings(enabled: false)), clock);

        await courier.HandleAsync(new StartupCompleted(), Cancellation);
        await PassAsync(TimeSpan.FromHours(1));

        Assert.Empty(server.Batches);
    }

    [Fact]
    public async Task DisconnectingDuringARoundCompletesAndKeepsNothing()
    {
        store.Entries.AddRange([new StoredEntry(Fleets.Scope, Fact(1)), new StoredEntry(Fleets.Scope, Fact(2))]);
        var session = await Fleets.ConnectedAsync(keychain);
        var connection = new FleetConnection(session, outbox, server, keychain);
        var disconnecting = Task.FromResult(ConnectOutcome.NotConnected);
        server.Answers.Enqueue(ids =>
        {
            disconnecting = connection.DisconnectAsync(CancellationToken.None);

            return new SendOutcome(ServerAnswer.Answered, new Receipt([], [], [new ReceiptEntry(ids[0].ToString(), "unknown kind 'x'")], [new ReceiptEntry(ids[1].ToString(), "the job id is empty")]));
        });

        await StartAsync(session);
        await PassAsync(TimeSpan.Zero);

        Assert.Equal(
            (ConnectOutcome.Disconnected, 0, 0, 0, 0),
            (await disconnecting, store.Entries.Count, store.Rejections.Count, store.Delivered.Count, keychain.Keys.Count));
    }

    [Fact]
    public async Task AppendsDuringAnOutageWaitForTheBackoff()
    {
        store.Entries.Add(new StoredEntry(Fleets.Scope, Fact(1)));
        server.Answers.Enqueue(_ => new SendOutcome(ServerAnswer.Unreachable, Option<Receipt>.None));
        server.Answers.Enqueue(_ => new SendOutcome(ServerAnswer.Unreachable, Option<Receipt>.None));

        await StartAsync();
        await PassAsync(TimeSpan.Zero);
        await AppendAsync(2);
        await PassAsync(FleetCourier.Debounce);
        var duringFirst = server.Batches.Count;
        await PassAsync(Backoff.After(1) - FleetCourier.Debounce);
        await AppendAsync(3);
        await PassAsync(Backoff.After(2) - TimeSpan.FromSeconds(1));
        var duringSecond = server.Batches.Count;
        await PassAsync(TimeSpan.FromSeconds(1));

        Assert.Equal((1, 2, 3, 0), (duringFirst, duringSecond, server.Batches.Count, store.Entries.Count));
    }

    [Fact]
    public async Task AnUnansweredRoundIsSaidUntilTheServerAnswers()
    {
        store.Entries.Add(new StoredEntry(Fleets.Scope, Fact(1)));
        server.Answers.Enqueue(_ => new SendOutcome(ServerAnswer.Unreachable, Option<Receipt>.None));

        await StartAsync();
        await PassAsync(TimeSpan.Zero);
        var unanswered = outbox.UnansweredSince;
        await PassAsync(Backoff.After(1));

        Assert.Equal((Noon, true), (unanswered.Match(at => at, () => DateTimeOffset.MinValue), outbox.UnansweredSince.IsNone));
    }

    [Fact]
    public async Task AnUntrustedRoundIsSaidAsSuch()
    {
        store.Entries.Add(new StoredEntry(Fleets.Scope, Fact(1)));
        server.Answers.Enqueue(_ => new SendOutcome(ServerAnswer.Untrusted, Option<Receipt>.None));

        await StartAsync();
        await PassAsync(TimeSpan.Zero);

        Assert.Equal((true, true, 1), (outbox.UnansweredSince.IsSome, outbox.Untrusted, store.Entries.Count));
    }

    [Fact]
    public void AChangedCauseRestartsTheUnansweredTime()
    {
        outbox.Unanswered(Noon, certificateUntrusted: false);

        outbox.Unanswered(Noon.AddHours(1), certificateUntrusted: true);

        Assert.Equal((Noon.AddHours(1), true), (outbox.UnansweredSince.Match(at => at, () => DateTimeOffset.MinValue), outbox.Untrusted));
    }

    [Fact]
    public async Task ARoundThatFailsIsRetriedAfterTheBackoff()
    {
        store.Entries.Add(new StoredEntry(Fleets.Scope, Fact(1)));
        store.FailsDue = 1;

        await StartAsync();
        await PassAsync(TimeSpan.Zero);
        var afterFailure = server.Batches.Count;
        await PassAsync(Backoff.After(1));

        Assert.Equal((0, 1, 0), (afterFailure, server.Batches.Count, store.Entries.Count));
    }

    [Fact]
    public async Task ARefusalAnsweredAfterAReconnectLeavesTheNewConnectionAlone()
    {
        store.Entries.Add(new StoredEntry(Fleets.Scope, Fact(1)));
        var session = await Fleets.ConnectedAsync(keychain);
        var reconnecting = Task.FromResult(false);
        server.Answers.Enqueue(_ =>
        {
            reconnecting = session.ConnectedAsync(Fleets.Settings() with { Instance = Guid.NewGuid() }, Fleets.Key, CancellationToken.None);

            return new SendOutcome(ServerAnswer.KeyRefused, Option<Receipt>.None);
        });

        await StartAsync(session);
        await PassAsync(TimeSpan.Zero);

        Assert.Equal((true, Connection.ConnectionState.Connected), (await reconnecting, session.State));
    }

    public async ValueTask DisposeAsync()
    {
        if (courier is not null)
        {
            await courier.DisposeAsync();
        }

        await outbox.DisposeAsync();
    }

    private static Guid Id(int number) => Guid.Parse($"0199f6a0-0000-7000-8000-{number:D12}");

    private static Fact Fact(int number) => new(Id(number), Noon, new JobProgressedFact(Guid.NewGuid(), "running"));

    private async Task<FleetSession> StartAsync() => await StartAsync(await Fleets.ConnectedAsync(keychain));

    private async Task<FleetSession> StartAsync(FleetSession session)
    {
        courier = new FleetCourier(outbox, server, session, clock);
        await courier.HandleAsync(new StartupCompleted(), Cancellation);

        return session;
    }

    private Task AppendAsync(int number) => outbox.AppendAsync(Fleets.Scope, Fact(number), Cancellation);

    private async Task PassAsync(TimeSpan time)
    {
        clock.Advance(time);
        await courier!.DrainedAsync(Cancellation);
        await courier.DrainedAsync(Cancellation);
    }
}
