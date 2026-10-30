using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Avala.Server.Activity.Contracts;
using Avala.Server.Activity.Contracts.Repositories;
using Avala.Server.Activity.Contracts.RepositoryJobs;
using Avala.Server.Activity.Contracts.Usage;
using Avala.Server.Instances.Contracts.Authentication;

namespace Avala.Server.Tests;

public sealed class GoldenResponseTests
{
    private const string AnInstanceKey = "avk_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "FleetWire", "v1");

    private static readonly DateTimeOffset Eleven = new(2026, 10, 25, 11, 0, 0, TimeSpan.Zero);

    private static readonly Guid Desktop = Id(10);

    private static readonly Guid Laptop = Id(11);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Reads => new()
    {
        { "/instances/me", "identity" },
        { "/activity/v1/repositories", "repositories" },
        { "/activity/v1/repositories/jobs?key=remote:github.com/rick/avala", "repository-jobs" },
        { "/activity/v1/usage?from=2026-10-19&to=2026-10-26&zone=Europe/Madrid&step=day", "usage" },
        { "/activity/v1/usage?from=2026-10-25&to=2026-10-26&zone=UTC&step=day", "usage-without-cost" },
    };

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task EveryGoldenResponseIsWhatTheServerWritesAsync(string path, string golden)
    {
        await using var server = GoldenServer();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AnInstanceKey);

        using var response = await server.CreateClient().SendAsync(request, Cancellation);

        await SameAsync(golden, await response.Content.ReadAsStringAsync(Cancellation));
    }

    [Fact]
    public async Task TheGoldenReceiptIsWhatTheServerWritesForTheGoldenEventsAsync()
    {
        await using var server = GoldenServer();
        List<string> events = [];

        foreach (var file in Directory.EnumerateFiles(Path.Combine(Folder, "events"), "*.json"))
        {
            events.Add(await File.ReadAllTextAsync(file, Cancellation));
        }

        events.Sort((one, other) => string.CompareOrdinal(JsonNode.Parse(one)!["id"]!.GetValue<string>(), JsonNode.Parse(other)!["id"]!.GetValue<string>()));

        events.Add($$"""{"kind":"jobArchived","id":"{{Id(16)}}"}""");
        events.Add($$"""{"kind":"jobProgressed","id":"{{Id(17)}}","occurredAt":"2026-10-25T12:00:00Z","job":"{{Id(256)}}","status":3}""");
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/activity/v1/events", UriKind.Relative))
        {
            Content = new StringContent($$"""{"events":[{{string.Join(",", events)}}]}""", Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AnInstanceKey);

        using var response = await server.CreateClient().SendAsync(request, Cancellation);

        await SameAsync("receipt", await response.Content.ReadAsStringAsync(Cancellation));
    }

    private static Guid Id(int number) => Guid.Parse($"0199f6a0-0000-7000-8000-{number:X12}".ToLowerInvariant());

    private static ServerUnderTest GoldenServer() =>
        new()
        {
            Caller = new AuthenticatedInstance(Desktop, "desktop"),
            Repositories = new FleetRepositories(
                [new RepositoryActivity("remote:github.com/rick/avala", 3, 2, new TokenSums(1500, 150, 9000, 400, 20), [new CostSum("USD", CostBasis.ApiEquivalent, 0.75m)], Eleven, Eleven.AddHours(1))],
                1),
            Jobs =
            [
                new RepositoryJob(Desktop, "desktop", Id(256), JobStatus.NeedsHelp, HoldReason.BudgetExceeded, 2, VerificationOutcome.Failed, new TokenSums(1000, 100, 9000, 400, 20), [new CostSum("USD", CostBasis.Billed, 0.5m)], Eleven, Eleven.AddHours(1)),
                new RepositoryJob(Laptop, "laptop", Id(257), null, null, 0, null, new TokenSums(500, 50, 0, 0, 0), [], Eleven.AddMinutes(30), Eleven.AddMinutes(30)),
            ],
            Usage = query => query.Zone == "UTC" ? WithoutCost : Costed,
        };

    private static UsageReport Costed
    {
        get
        {
            var day = new DateOnly(2026, 10, 19);
            var avala = new TokenSums(2000, 200, 15000, 1000, 50);
            var laptop = new TokenSums(1000, 100, 5000, 0, 0);
            var none = new TokenSums(0, 0, 0, 0, 0);

            return new UsageReport(
                day,
                day.AddDays(7),
                "Europe/Madrid",
                "day",
                new CostKind("USD", CostBasis.ApiEquivalent),
                new UsageTotals(new TokenSums(3000, 300, 20000, 1000, 50), Usd(1.25m), 3, 2),
                [
                    .. Enumerable.Range(0, 5).Select(offset => new UsagePeriodSum(day.AddDays(offset), none, [])),
                    new UsagePeriodSum(day.AddDays(5), laptop, Usd(0.5m)),
                    new UsagePeriodSum(day.AddDays(6), avala, Usd(0.75m)),
                ],
                [
                    new UsageShare(UsageShareKind.Repository, "remote:github.com/rick/avala", "remote:github.com/rick/avala", avala, Usd(0.75m), 2),
                    new UsageShare(UsageShareKind.Unknown, string.Empty, string.Empty, new TokenSums(600, 60, 3000, 0, 0), Usd(0.3m), 1),
                    new UsageShare(UsageShareKind.OutsideAnyJob, string.Empty, string.Empty, new TokenSums(400, 40, 2000, 0, 0), Usd(0.2m), 0),
                ],
                [
                    new UsageShare(UsageShareKind.Instance, Desktop.ToString(), "desktop", avala, Usd(0.75m), 2),
                    new UsageShare(UsageShareKind.Instance, Laptop.ToString(), "laptop", laptop, Usd(0.5m), 1),
                ],
                [
                    new UsageShare(UsageShareKind.Model, "opus", "opus", new TokenSums(2600, 260, 18000, 1000, 50), Usd(1.05m), 0),
                    new UsageShare(UsageShareKind.Model, string.Empty, string.Empty, new TokenSums(400, 40, 2000, 0, 0), Usd(0.2m), 0),
                ]);
        }
    }

    private static UsageReport WithoutCost
    {
        get
        {
            var day = new DateOnly(2026, 10, 25);
            var tokens = new TokenSums(10, 1, 0, 0, 0);

            return new UsageReport(
                day,
                day.AddDays(1),
                "UTC",
                "day",
                null,
                new UsageTotals(tokens, [], 1, 1),
                [new UsagePeriodSum(day, tokens, [])],
                [new UsageShare(UsageShareKind.Repository, "root:0123456789abcdef0123456789abcdef01234567", "root:0123456789abcdef0123456789abcdef01234567", tokens, [], 1)],
                [new UsageShare(UsageShareKind.Instance, Desktop.ToString(), "desktop", tokens, [], 1)],
                [new UsageShare(UsageShareKind.Model, "sonnet", "sonnet", tokens, [], 0)]);
        }
    }

    private static IReadOnlyList<CostSum> Usd(decimal amount) => [new CostSum("USD", CostBasis.ApiEquivalent, amount)];

    private static async Task SameAsync(string golden, string written)
    {
        var expected = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(Folder, "responses", $"{golden}.json"), Cancellation));
        var actual = JsonNode.Parse(written);

        Assert.True(JsonNode.DeepEquals(expected, actual), $"The server wrote {actual?.ToJsonString()}");
    }
}
