using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Avala.Server.Activity.Contracts;
using Avala.Server.Activity.Contracts.Repositories;
using Avala.Server.Activity.Contracts.RepositoryJobs;
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
        };

    private static async Task SameAsync(string golden, string written)
    {
        var expected = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(Folder, "responses", $"{golden}.json"), Cancellation));
        var actual = JsonNode.Parse(written);

        Assert.True(JsonNode.DeepEquals(expected, actual), $"The server wrote {actual?.ToJsonString()}");
    }
}
