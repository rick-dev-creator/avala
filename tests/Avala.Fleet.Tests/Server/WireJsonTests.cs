using System.Text.Json.Nodes;
using Avala.Fleet.Contracts;
using Avala.Fleet.Facts;
using Avala.Fleet.Server;
using Avala.Sdk;

namespace Avala.Fleet.Tests.Server;

public sealed class WireJsonTests
{
    public static readonly DateTimeOffset Noon = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    public static readonly Guid Job = Guid.Parse("0199f6a0-0000-7000-8000-000000000100");

    public static readonly Guid Parent = Guid.Parse("0199f6a0-0000-7000-8000-000000000200");

    public static TheoryData<string> Events => [.. Cases.Keys];

    private static Dictionary<string, Fact> Cases { get; } = new(StringComparer.Ordinal)
    {
        ["job-submitted"] = Fact(1, new JobSubmittedFact(Job, "remote:github.com/rick/avala", Parent, "opus", "high")),
        ["job-submitted-minimal"] = Fact(2, new JobSubmittedFact(Job, "root:0123456789abcdef0123456789abcdef01234567", Option<Guid>.None, Option<string>.None, Option<string>.None)),
        ["job-model-chosen"] = Fact(3, new JobModelChosenFact(Job, "sonnet", "low")),
        ["job-model-chosen-minimal"] = Fact(4, new JobModelChosenFact(Job, Option<string>.None, Option<string>.None)),
        ["job-progressed"] = Fact(5, new JobProgressedFact(Job, "needsHelp")),
        ["job-held"] = Fact(6, new JobHeldFact(Job, "budgetExceeded")),
        ["usage-reported"] = Fact(7, new UsageReportedFact(Job, new FactTokens(1000, 200, 30000, 4000, 50), new FactCost(0.002279m, "USD", "apiEquivalent"), "opus")),
        ["usage-reported-minimal"] = Fact(8, new UsageReportedFact(Option<Guid>.None, new FactTokens(1, 0, 0, 0, 0), Option<FactCost>.None, Option<string>.None)),
        ["attempt-verified"] = Fact(9, new AttemptVerifiedFact(Job, 2, "noChecksDeclared")),
    };

    [Theory]
    [MemberData(nameof(Events))]
    public async Task EveryEventCaseIsWrittenAsItsGoldenFileAsync(string name) =>
        Assert.Equal((await GoldenAsync($"events/{name}.json")).Trim(), WireJson.Write(Cases[name]));

    [Fact]
    public async Task TheReceiptIsReadFromItsGoldenFileAsync()
    {
        var receipt = WireJson.ReadReceipt(await GoldenAsync("responses/receipt.json")).Match(found => found, () => throw new InvalidOperationException("unread"));

        Assert.Equal(
            (9, 0, "unknown kind 'jobArchived'", "the field 'status' is invalid"),
            (receipt.Accepted.Count, receipt.Duplicates.Count, Assert.Single(receipt.Deferred).Reason, Assert.Single(receipt.Rejected).Reason));
    }

    [Fact]
    public async Task TheUsageQueryIsWrittenAsItsGoldenFileAsync() =>
        Assert.Equal(
            (await GoldenAsync("requests/usage.query")).Trim(),
            WireJson.UsageQuery(new FleetUsageQuery(new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 26), FleetStep.Day, "remote:github.com/rick/avala"), "Europe/Madrid"));

    [Fact]
    public async Task TheUsageIsReadFromItsGoldenFileAsync()
    {
        var usage = WireJson.ReadUsage(await GoldenAsync("responses/usage.json")).Match(found => found, () => FleetUsage.Empty);

        Assert.Equal(
            ("USD apiEquivalent", 3000m, 1.25m, 3, 2, 7, 0.75m),
            (usage.Reference.Match(kind => $"{kind.Currency} {kind.Basis}", () => string.Empty), usage.Totals.Tokens.Input, usage.Totals.Costs.Single().Amount, usage.Totals.Jobs, usage.Totals.Instances, usage.Series.Count, usage.Series[^1].Costs.Single().Amount));
        Assert.Equal(
            ["Repository remote:github.com/rick/avala 2", "Unknown  1", "OutsideAnyJob  0", "Instance desktop 2", "Instance laptop 1", "Model opus 0", "Model  0"],
            usage.Repositories.Concat(usage.Instances).Concat(usage.Models).Select(share => $"{share.Kind} {share.Name} {share.Jobs}"));
    }

    [Fact]
    public void AnUnknownKindIsNeverTakenForARepository() =>
        Assert.Equal(
            FleetShareKind.Unknown,
            WireJson.ReadUsage("""{"reference":null,"totals":{"tokens":{"input":0,"output":0,"cacheRead":0,"cacheWrite":0,"reasoning":0},"costs":[],"jobs":0,"instances":0},"series":[],"repositories":[{"key":"x","kind":"archived","tokens":{"input":0,"output":0,"cacheRead":0,"cacheWrite":0,"reasoning":0},"costs":[],"jobs":0}],"instances":[],"models":[]}""")
                .Match(usage => usage.Repositories[0].Kind, () => FleetShareKind.Repository));

    [Fact]
    public async Task AUsageWithoutCostHasNoReferenceAsync()
    {
        var usage = WireJson.ReadUsage(await GoldenAsync("responses/usage-without-cost.json")).Match(found => found, () => FleetUsage.Empty);

        Assert.Equal((true, 10m, 1), (usage.Reference.IsNone, usage.Totals.Tokens.Input, usage.Series.Count));
    }

    [Fact]
    public async Task ARepositorysJobsAreReadFromTheirGoldenFileWithMissingStatesEmptyAsync()
    {
        var view = WireJson.ReadJobs(await GoldenAsync("responses/repository-jobs.json")).Match(found => found, () => FleetJobsView.Of(FleetReadState.Unreachable));

        Assert.Equal(
            [("desktop", "needsHelp", "budgetExceeded", "failed"), ("laptop", string.Empty, string.Empty, string.Empty)],
            view.Jobs.Select(job => (job.InstanceName, job.Status, job.Hold, job.LastOutcome)));
    }

    [Fact]
    public async Task TheIdentityIsReadFromItsGoldenFileAsync() =>
        Assert.Equal("desktop", WireJson.ReadIdentity(await GoldenAsync("responses/identity.json")).Match(identity => identity.Name, () => string.Empty));

    [Fact]
    public void AnUnreadableReceiptIsRefused() =>
        Assert.True(WireJson.ReadReceipt("{\"accepted\": 3}").IsNone);

    [Fact]
    public void TimesAreWrittenInUtcWithAnOffset() =>
        Assert.Equal(
            "2026-10-25T03:00:00.0000000Z",
            JsonNode.Parse(WireJson.Write(new Fact(Guid.NewGuid(), new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.FromHours(9)), new JobProgressedFact(Job, "running"))))!["occurredAt"]!.GetValue<string>());

    [Fact]
    public void ABatchHoldsTheBodiesAsTheyAre() =>
        Assert.Equal("{\"events\":[{\"a\":1},{\"b\":2}]}", WireJson.Batch(["{\"a\":1}", "{\"b\":2}"]));

    public static Task<string> GoldenAsync(string path) =>
        File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "FleetWire", "v1", path), TestContext.Current.CancellationToken);

    private static Fact Fact(int number, IFactBody body) => new(Guid.Parse($"0199f6a0-0000-7000-8000-{number:D12}"), Noon, body);
}
