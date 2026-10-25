using Avala.Server.Activity.Contracts;
using Avala.Server.Activity.Contracts.Ingestion;
using Avala.Server.Activity.EndPoints.Ingestion;
using static Avala.Server.Activity.Tests.Endpoints.Batches;

namespace Avala.Server.Activity.Tests.Endpoints;

public sealed class GoldenEventTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "FleetWire", "v1", "events");

    private static readonly Guid GoldenJob = Id(100);

    private static readonly Dictionary<string, IIncomingEvent> Expected = new(StringComparer.Ordinal)
    {
        ["job-submitted"] = new JobSubmittedEvent(Id(1), Noon, GoldenJob, "remote:github.com/rick/avala", Id(200), "opus", "high"),
        ["job-submitted-minimal"] = new JobSubmittedEvent(Id(2), Noon, GoldenJob, "root:0123456789abcdef0123456789abcdef01234567", null, null, null),
        ["job-model-chosen"] = new JobModelChosenEvent(Id(3), Noon, GoldenJob, "sonnet", "low"),
        ["job-model-chosen-minimal"] = new JobModelChosenEvent(Id(4), Noon, GoldenJob, null, null),
        ["job-progressed"] = new JobProgressedEvent(Id(5), Noon, GoldenJob, JobStatus.NeedsHelp),
        ["job-held"] = new JobHeldEvent(Id(6), Noon, GoldenJob, HoldReason.BudgetExceeded),
        ["usage-reported"] = new UsageReportedEvent(Id(7), Noon, GoldenJob, 1000, 200, 30000, 4000, 50, new IncomingCost(0.002279m, "USD", CostBasis.ApiEquivalent), "opus"),
        ["usage-reported-minimal"] = new UsageReportedEvent(Id(8), Noon, null, 1, 0, 0, 0, 0, null, null),
        ["attempt-verified"] = new AttemptVerifiedEvent(Id(9), Noon, GoldenJob, 2, VerificationOutcome.NoChecksDeclared),
    };

    public static TheoryData<string> Cases => [.. Directory.EnumerateFiles(Folder, "*.json").Select(file => Path.GetFileNameWithoutExtension(file)).Order(StringComparer.Ordinal)];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task EveryGoldenEventIsReadAsync(string name)
    {
        var mapped = IngestEventsMapper.ToCommand(Caller, Request(await File.ReadAllTextAsync(Path.Combine(Folder, $"{name}.json"), TestContext.Current.CancellationToken)));

        Assert.Equal(Expected[name], Assert.Single(mapped.Command.Events));
    }

    [Fact]
    public void EveryExpectedEventHasAGoldenFile() =>
        Assert.Equal(Expected.Keys.Order(StringComparer.Ordinal), Cases.Select(row => row.Data));
}
