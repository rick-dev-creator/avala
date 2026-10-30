using Avala.Server.Activity.Contracts;
using Avala.Server.Activity.Contracts.Ingestion;
using Avala.Server.Activity.EndPoints.Ingestion;
using static Avala.Server.Activity.Tests.Endpoints.Batches;

namespace Avala.Server.Activity.Tests.Endpoints;

public sealed class IngestEventsMapperTests
{
    public static TheoryData<string> KindNames => new() { "jobSubmitted", "jobModelChosen", "jobProgressed", "jobHeld", "usageReported", "attemptVerified" };

    public static TheoryData<string> Unreadable => new()
    {
        $$"""{ "kind": "jobProgressed", "id": "\ud800", "occurredAt": "{{Time}}", "job": "{{Job}}", "status": "running" }""",
        $$"""{ "kind": "\ud800", "id": "{{Id(1)}}" }""",
        $$"""{ "kind": "jobModelChosen", "id": "{{Id(1)}}", "occurredAt": "{{Time}}", "job": "{{Job}}", "model": "\ud800" }""",
    };

    public static TheoryData<string> StrictEnums => new()
    {
        Progressed(1, "\"Running\""),
        $$"""{ "kind": "jobHeld", "id": "{{Id(1)}}", "occurredAt": "{{Time}}", "job": "{{Job}}", "reason": "Stalled" }""",
        $$"""{ "kind": "attemptVerified", "id": "{{Id(1)}}", "occurredAt": "{{Time}}", "job": "{{Job}}", "attempt": 1, "outcome": "Passed" }""",
        $$"""{ "kind": "usageReported", "id": "{{Id(1)}}", "occurredAt": "{{Time}}", "inputTokens": 1, "outputTokens": 0, "cacheReadTokens": 0, "cacheWriteTokens": 0, "reasoningTokens": 0, "cost": { "amount": 1, "currency": "USD", "basis": "Billed" } }""",
    };

    public static TheoryData<string, string> FirstCopies => new()
    {
        { Progressed(1), "accepted" },
        { $$"""{ "kind": "jobArchived", "id": "{{Id(1)}}" }""", "deferred" },
        { Progressed(1, "3"), "rejected" },
    };

    [Theory]
    [MemberData(nameof(KindNames))]
    public void EveryKindIsRead(string kind)
    {
        var (text, expected) = Example(kind);

        Assert.Equal(expected, Assert.Single(Map(text).Command.Events));
    }

    [Theory]
    [MemberData(nameof(Unreadable))]
    public void AnUnreadableIdOrKindIsRejectedAlone(string text)
    {
        var mapped = Map(text, Progressed(2));

        Assert.Equal((1, 1), (mapped.Rejected.Count, mapped.Command.Events.Count));
    }

    [Fact]
    public void AnUnknownKindIsDeferredAlone()
    {
        var mapped = Map($$"""{ "kind": "jobArchived", "id": "{{Id(1)}}" }""", Progressed(2));

        Assert.Equal(
            (new IngestEventsEntry(Id(1).ToString(), "unknown kind 'jobArchived'"), 1, 0),
            (Assert.Single(mapped.Deferred), mapped.Command.Events.Count, mapped.Rejected.Count));
    }

    [Fact]
    public void AnEventWithoutAKindIsRejectedAlone()
    {
        var mapped = Map($$"""{ "id": "{{Id(1)}}", "occurredAt": "{{Time}}" }""", Progressed(2));

        Assert.Equal((new IngestEventsEntry(Id(1).ToString(), "the event has no kind"), 1), (Assert.Single(mapped.Rejected), mapped.Command.Events.Count));
    }

    [Fact]
    public void AKindInAnotherCaseIsNoKind() =>
        Assert.Equal(
            "the event has no kind",
            Assert.Single(Map(Progressed(1).Replace("\"kind\"", "\"Kind\"", StringComparison.Ordinal)).Rejected).Reason);

    [Fact]
    public void AnElementThatIsNotAnObjectIsRejectedByItsIndex() =>
        Assert.Equal(new IngestEventsEntry("#1", "an event must be an object"), Assert.Single(Map(Progressed(1), "3").Rejected));

    [Fact]
    public void AMalformedFieldIsRejectedAlone()
    {
        var mapped = Map(Progressed(1).Replace(Job.ToString(), "not-a-job", StringComparison.Ordinal), Progressed(2));

        Assert.Equal((new IngestEventsEntry(Id(1).ToString(), "the field 'job' is invalid"), 1), (Assert.Single(mapped.Rejected), mapped.Command.Events.Count));
    }

    [Fact]
    public void AMissingRequiredFieldIsRejectedAlone()
    {
        var mapped = Map($$"""{ "kind": "jobProgressed", "id": "{{Id(1)}}", "occurredAt": "{{Time}}", "job": "{{Job}}" }""", Progressed(2));

        Assert.Equal(("the field 'status' is missing", 1), (Assert.Single(mapped.Rejected).Reason, mapped.Command.Events.Count));
    }

    [Fact]
    public void SeveralMissingFieldsAreNamedTogether() =>
        Assert.EndsWith(
            "are missing",
            Assert.Single(Map($$"""{ "kind": "jobProgressed", "id": "{{Id(1)}}", "occurredAt": "{{Time}}" }""").Rejected).Reason,
            StringComparison.Ordinal);

    [Fact]
    public void ANullInARequiredFieldIsRejectedAlone()
    {
        var mapped = Map(Progressed(1).Replace($"\"{Job}\"", "null", StringComparison.Ordinal), Progressed(2));

        Assert.Equal(("the field 'job' is invalid", 1), (Assert.Single(mapped.Rejected).Reason, mapped.Command.Events.Count));
    }

    [Fact]
    public void ANumericStatusIsRejected() =>
        Assert.Equal("the field 'status' is invalid", Assert.Single(Map(Progressed(1, "3")).Rejected).Reason);

    [Fact]
    public void ACombinedStatusIsRejected() =>
        Assert.Single(Map(Progressed(1, "\"preparing, running\"")).Rejected);

    [Fact]
    public void AnUndefinedEnumValueIsRejected() =>
        Assert.Single(Map(Progressed(1, "\"approved,failed\"")).Rejected);

    [Fact]
    public void AnEnumWithSurroundingWhitespaceIsRejected() =>
        Assert.Single(Map(Progressed(1, "\" running\"")).Rejected);

    [Theory]
    [MemberData(nameof(StrictEnums))]
    public void EveryEnumFieldIsStrict(string text) =>
        Assert.Single(Map(text).Rejected);

    [Fact]
    public void ARepeatedPropertyIsRejected() =>
        Assert.Equal(
            new IngestEventsEntry(Id(1).ToString(), "the event repeats a property"),
            Assert.Single(Map(Progressed(1).Replace("\"status\"", $"\"job\": \"{Id(9)}\", \"status\"", StringComparison.Ordinal)).Rejected));

    [Fact]
    public void ATimeWithoutAnOffsetIsRejected() =>
        Assert.Equal(
            "the field 'occurredAt' is invalid",
            Assert.Single(Map(Progressed(1).Replace(Time, "2026-10-25T12:00:00", StringComparison.Ordinal)).Rejected).Reason);

    [Fact]
    public void AnUnknownFieldIsIgnored() =>
        Assert.Single(Map(Progressed(1).Replace("\"status\"", "\"colour\": \"blue\", \"status\"", StringComparison.Ordinal)).Command.Events);

    [Fact]
    public void AnElementWithoutAReadableIdIsKeyedByItsIndex() =>
        Assert.Equal("#0", Assert.Single(Map(Progressed(1).Replace(Id(1).ToString(), "one", StringComparison.Ordinal)).Rejected).Id);

    [Fact]
    public void KindNeedNotComeFirst() =>
        Assert.Single(Map($$"""{ "id": "{{Id(1)}}", "occurredAt": "{{Time}}", "job": "{{Job}}", "status": "running", "kind": "jobProgressed" }""").Command.Events);

    [Theory]
    [MemberData(nameof(FirstCopies))]
    public void ARepeatedIdTakesTheFirstCopysVerdict(string first, string verdict)
    {
        var mapped = Map(first, Progressed(1, "\"checking\""));

        Assert.Equal(
            (verdict, 1, JobStatus.Running),
            (Verdict(mapped), mapped.Command.Events.Count + mapped.Deferred.Count + mapped.Rejected.Count, KeptStatus(mapped)));
    }

    [Fact]
    public void ACopyRepeatingAPropertyTakesTheFirstCopysVerdict()
    {
        var mapped = Map(Progressed(1), Progressed(1).Replace("\"status\"", $"\"job\": \"{Id(9)}\", \"status\"", StringComparison.Ordinal));

        Assert.Equal((1, 0), (mapped.Command.Events.Count, mapped.Rejected.Count));
    }

    [Fact]
    public void TheReceiptListsEveryDistinctIdExactlyOnce()
    {
        var mapped = Map(
            Progressed(1),
            Progressed(2),
            Progressed(1),
            $$"""{ "kind": "jobArchived", "id": "{{Id(3)}}" }""",
            Progressed(4, "3"),
            "7",
            Progressed(5));
        var receipt = new IngestionReceipt([Id(1)], [Id(2)], [new RejectedEvent(Id(5), "the time is before 2025")]);

        var response = IngestEventsMapper.ToResponse(receipt, mapped);

        Assert.Equal(
            ["#5", .. Enumerable.Range(1, 5).Select(number => Id(number).ToString())],
            [
                .. response.Accepted.Select(id => id.ToString())
                    .Concat(response.Duplicates.Select(id => id.ToString()))
                    .Concat(response.Deferred.Select(entry => entry.Id))
                    .Concat(response.Rejected.Select(entry => entry.Id))
                    .Order(StringComparer.Ordinal),
            ]);
    }

    private static JobStatus KeptStatus(MappedBatch mapped) =>
        mapped.Command.Events is [JobProgressedEvent { Status: var status }] ? status : JobStatus.Running;

    private static (string Text, IIncomingEvent Expected) Example(string kind) => kind switch
    {
        "jobSubmitted" => (
            $$"""{ "kind": "jobSubmitted", "id": "{{Id(1)}}", "occurredAt": "{{Time}}", "job": "{{Job}}", "repository": "remote:github.com/rick/avala", "parent": "{{Id(7)}}", "model": "opus", "effort": "high" }""",
            new JobSubmittedEvent(Id(1), Noon, Job, "remote:github.com/rick/avala", Id(7), "opus", "high")),
        "jobModelChosen" => (
            $$"""{ "kind": "jobModelChosen", "id": "{{Id(1)}}", "occurredAt": "{{Time}}", "job": "{{Job}}", "model": "sonnet", "effort": "low" }""",
            new JobModelChosenEvent(Id(1), Noon, Job, "sonnet", "low")),
        "jobProgressed" => (Progressed(1), new JobProgressedEvent(Id(1), Noon, Job, JobStatus.Running)),
        "jobHeld" => (
            $$"""{ "kind": "jobHeld", "id": "{{Id(1)}}", "occurredAt": "{{Time}}", "job": "{{Job}}", "reason": "budgetExceeded" }""",
            new JobHeldEvent(Id(1), Noon, Job, HoldReason.BudgetExceeded)),
        "usageReported" => (
            $$"""{ "kind": "usageReported", "id": "{{Id(1)}}", "occurredAt": "{{Time}}", "job": "{{Job}}", "inputTokens": 1, "outputTokens": 2, "cacheReadTokens": 3, "cacheWriteTokens": 4, "reasoningTokens": 5, "cost": { "amount": 0.25, "currency": "USD", "basis": "apiEquivalent" }, "model": "opus" }""",
            new UsageReportedEvent(Id(1), Noon, Job, 1, 2, 3, 4, 5, new IncomingCost(0.25m, "USD", CostBasis.ApiEquivalent), "opus")),
        "attemptVerified" => (
            $$"""{ "kind": "attemptVerified", "id": "{{Id(1)}}", "occurredAt": "{{Time}}", "job": "{{Job}}", "attempt": 2, "outcome": "noChecksDeclared" }""",
            new AttemptVerifiedEvent(Id(1), Noon, Job, 2, VerificationOutcome.NoChecksDeclared)),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No such kind."),
    };

    private static string Verdict(MappedBatch mapped) =>
        mapped.Command.Events.Count == 1 ? "accepted"
        : mapped.Deferred.Count == 1 ? "deferred"
        : "rejected";
}
