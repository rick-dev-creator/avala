using Avala.Fleet.Connection;
using Avala.Fleet.Delivery;
using Avala.Fleet.Facts;
using Avala.Fleet.Origins;
using Avala.Sdk;

namespace Avala.Fleet.Tests.Domain;

public sealed class FleetRepositoryKeyTests
{
    private static readonly string Root = new('a', 40);

    [Fact]
    public void OriginWinsOverOtherRemotes() =>
        Assert.Equal("remote:github.com/rick/avala", Key([("upstream", "https://github.com/octo/avala.git"), ("origin", "git@github.com:rick/avala.git")]));

    [Fact]
    public void WithoutOriginTheFirstRemoteByNameIsUsed() =>
        Assert.Equal("remote:gitea.example/team/shop", Key([("zeta", "https://github.com/octo/shop.git"), ("alpha", "https://gitea.example/team/shop")]));

    [Fact]
    public void AnScpRemoteBecomesHostAndPath() =>
        Assert.Equal("remote:github.com/rick/avala", Key([("origin", "git@GitHub.com:Rick/Avala.git")]));

    [Fact]
    public void AnScpRemoteWithoutAUserBecomesHostAndPath() =>
        Assert.Equal("remote:forge.example/rick/avala", Key([("origin", "forge.example:rick/avala")]));

    [Fact]
    public void SchemeUserInformationAndPortAreRemoved() =>
        Assert.Equal("remote:forge.example/rick/avala", Key([("origin", "ssh://git:secret@forge.example:2222/rick/avala.git/")]));

    [Fact]
    public void ALocalRemoteFallsBackToTheRootCommit() =>
        Assert.Equal($"root:{Root}", Key([("origin", "/home/rick/remotes/avala.git"), ("backup", "file:///srv/avala.git")], [Root]));

    [Fact]
    public void TheSmallestRootCommitIsUsed() =>
        Assert.Equal($"root:{new string('1', 40)}", Key([], [new string('f', 40), new string('1', 40)]));

    [Fact]
    public void AnEmptyRepositoryHasNoKey() =>
        Assert.True(FleetRepositoryKey.From([], []).IsNone);

    [Theory]
    [InlineData("https://localhost/rick/avala")]
    [InlineData("https://github.com/avala")]
    [InlineData("https://github.com/rick/ava%20la")]
    public void ARemoteWithoutTheServersShapeIsSkipped(string url) =>
        Assert.Equal($"root:{Root}", Key([("origin", url)], [Root]));

    private static string Key(IReadOnlyList<(string Name, string Url)> remotes, IReadOnlyList<string>? roots = null) =>
        FleetRepositoryKey.From(remotes, roots ?? []).Match(key => key, () => "none");
}

public sealed class FactRulesTests
{
    [Fact]
    public void ACostIsRoundedToSixDecimals() =>
        Assert.Equal(0.002279m, Cost(0.0022790500000000003m, "USD").Match(cost => cost.Amount, () => -1m));

    [Fact]
    public void ACostAboveAMillionIsLeftOut() =>
        Assert.True(Cost(1_000_000.01m, "USD").IsNone);

    [Fact]
    public void AnInvalidCurrencyLeavesTheCostOut() =>
        Assert.True(Cost(1m, "usd").IsNone);

    [Fact]
    public void ALongModelIsLeftOut() =>
        Assert.True(FactRules.Model(new string('m', 101)).IsNone);

    [Fact]
    public void ALongEffortIsLeftOut() =>
        Assert.True(FactRules.Effort(new string('e', 21)).IsNone);

    [Theory]
    [InlineData("op\0us")]
    [InlineData("op\u0007us")]
    public void ControlCharactersLeaveTheFieldOut(string model) =>
        Assert.True(FactRules.Model(model).IsNone);

    [Fact]
    public void AModelIsTrimmed() =>
        Assert.Equal("opus", FactRules.Model("  opus ").Match(model => model, () => "none"));

    [Fact]
    public void TokensOutOfBoundsDropTheFact() =>
        Assert.True(FactRules.Tokens(1, 0, -1, 0, 0).IsFailure);

    private static Option<FactCost> Cost(decimal amount, string currency) => FactRules.Cost(amount, currency, "billed");
}

public sealed class DeliveryTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid[] Sent = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

    [Fact]
    public void AStampNeverGoesBackwards() =>
        Assert.Equal(Noon, Stamps.Next(Noon.AddSeconds(-5), Noon));

    [Fact]
    public void TheWaitGrowsThenStaysAtThirtyMinutes() =>
        Assert.Equal(
            [5, 30, 120, 600, 1800, 1800],
            Enumerable.Range(1, 6).Select(failures => (int)Backoff.After(failures).TotalSeconds));

    [Fact]
    public void ADeferredFactExpiresAfterThirtyDays() =>
        Assert.Equal((false, true), (DeferredExpiry.Expired(Noon, Noon.AddDays(30)), DeferredExpiry.Expired(Noon, Noon.AddDays(30).AddSeconds(1))));

    [Fact]
    public void AcceptedAndDuplicatesAreDeleted() =>
        Assert.Equal([Sent[0], Sent[1]], ReceiptPlan.Of(Sent[..2], new Receipt([Sent[0]], [Sent[1]], [], [])).Delete);

    [Fact]
    public void RejectedAreDeletedAndKept()
    {
        var plan = ReceiptPlan.Of(Sent[..1], new Receipt([], [], [], [new ReceiptEntry(Sent[0].ToString(), "the job id is empty")]));

        Assert.Equal((Sent[0], "the job id is empty"), (Assert.Single(plan.Delete), Assert.Single(plan.Rejected).Reason));
    }

    [Fact]
    public void DeferredAreRescheduledAndAnIndexKeyIsResolved()
    {
        var plan = ReceiptPlan.Of(Sent[..2], new Receipt([Sent[1]], [], [new ReceiptEntry("#0", "unknown kind 'x'")], []));

        Assert.Equal((Sent[0], 1), (Assert.Single(plan.Deferred).Id, plan.Delete.Count));
    }

    [Fact]
    public void UnlistedFactsAreDeferred() =>
        Assert.Equal(ReceiptPlan.Unanswered, Assert.Single(ReceiptPlan.Of(Sent[..1], new Receipt([], [], [], [])).Deferred).Reason);
}

public sealed class ConnectionTests
{
    [Fact]
    public void HttpsIsRequired() =>
        Assert.Equal(FleetError.InsecureAddress, Error("http://fleet.example"));

    [Theory]
    [InlineData("http://localhost:5280")]
    [InlineData("http://127.0.0.1:5280/")]
    [InlineData("http://[::1]:5280")]
    public void HttpIsAcceptedOnlyForLoopback(string address) =>
        Assert.True(ServerAddress.Create(address).IsSuccess);

    [Theory]
    [InlineData("https://rick:secret@fleet.example")]
    [InlineData("https://fleet.example/api")]
    [InlineData("https://fleet.example/?a=b")]
    [InlineData("https://fleet.example/#top")]
    [InlineData("fleet.example")]
    public void UserInformationPathsQueriesAndFragmentsAreRefused(string address) =>
        Assert.Equal(FleetError.InvalidAddress, Error(address));

    [Fact]
    public void TheOriginIsKeptWithoutATrailingSlash() =>
        Assert.Equal("https://fleet.example:8443", ServerAddress.Create("https://Fleet.Example:8443/").Match(address => address.Origin, _ => string.Empty));

    [Theory]
    [InlineData("avk_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true)]
    [InlineData("avk_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("avk_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+", false)]
    [InlineData("abc_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    public void OnlyAnInstanceKeyIsAccepted(string key, bool accepted) =>
        Assert.Equal(accepted, KeyShape.Matches(key));

    private static FleetError Error(string address) => ServerAddress.Create(address).Match(_ => (FleetError)(-1), error => error);
}
