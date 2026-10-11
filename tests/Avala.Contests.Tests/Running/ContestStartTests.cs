using Avala.Agents.Contracts.Connections;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;

namespace Avala.Contests.Tests.Running;

public sealed class ContestStartTests
{
    private static readonly ConnectionName Work = new("work");
    private static readonly ConnectionName Personal = new("personal");
    private static readonly ConnectionName Spare = new("spare");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACandidateThatCannotBeSubmittedWithdrawsTheCandidatesAlreadyEnteredAsync()
    {
        await using var arena = new Arena();
        arena.Jobs.Accepted = 2;

        var refused = await arena.Contests.StartAsync(Request(3), Cancellation);

        Assert.Equal(ContestRejection.NotSubmitted, Outcomes.FailsWith(refused));
        Assert.Equal(2, arena.Jobs.Discarded.Distinct().Count());
        Assert.Empty(arena.Contests.All());
        Assert.Empty(arena.Bus.Published.OfType<ContestStarted>());
    }

    [Fact]
    public async Task AJobNamingAConnectionRunsEveryCandidateOnItAsync()
    {
        await using var arena = new Arena();
        arena.Connections.Previewed = Work;

        var started = Outcomes.Succeeds(await arena.Contests.StartAsync(Request(2, Personal) with { Spread = true }, Cancellation));

        Assert.Equal([Personal, Personal], Connections(arena));
        Assert.All(started.Candidates, candidate => Assert.Equal(Option<ConnectionName>.Some(Personal), candidate.Connection));
    }

    [Fact]
    public async Task ASpreadContestDealsItsCandidatesInTurnOverTheUsableConnectionsAsync()
    {
        await using var arena = new Arena();
        arena.Connections.Declared.AddRange([Work, Personal, Spare]);
        arena.Connections.Usable.UnionWith([Work, Spare]);

        var started = Outcomes.Succeeds(await arena.Contests.StartAsync(Request(3) with { Spread = true }, Cancellation));

        Assert.Equal([Work, Spare, Work], Connections(arena));
        Assert.True(started.Spread);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithoutAConnectionToSpreadOverEveryCandidateRunsOnTheConnectionTheRepositoryWouldChooseAsync(bool spread)
    {
        await using var arena = new Arena();
        arena.Connections.Declared.AddRange([Work, Personal]);
        arena.Connections.Usable.UnionWith(spread ? [] : [Personal]);
        arena.Connections.Previewed = Work;

        _ = Outcomes.Succeeds(await arena.Contests.StartAsync(Request(2) with { Spread = spread }, Cancellation));

        Assert.Equal([Work, Work], Connections(arena));
    }

    private static ContestRequest Request(int candidates, params ConnectionName[] connection) =>
        new(new JobRequest(Arena.Repository, "Fix the calculator") { Connection = connection.Length > 0 ? connection[0] : Option<ConnectionName>.None }, candidates);

    private static ConnectionName[] Connections(Arena arena) =>
        [.. arena.Jobs.Submitted.Select(request => Outcomes.Present(request.Connection))];
}
