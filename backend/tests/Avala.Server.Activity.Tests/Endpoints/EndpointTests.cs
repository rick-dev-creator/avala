using Ardalis.Result;
using Avala.Server.Activity.Contracts;
using Avala.Server.Activity.Contracts.Ingestion;
using Avala.Server.Activity.Contracts.Repositories;
using Avala.Server.Activity.Contracts.RepositoryJobs;
using Avala.Server.Activity.Contracts.Usage;
using Avala.Server.Activity.EndPoints;
using Avala.Server.Activity.EndPoints.Ingestion;
using Avala.Server.Activity.EndPoints.Repositories;
using Avala.Server.Activity.EndPoints.RepositoryJobs;
using Avala.Server.Activity.EndPoints.Usage;
using Avala.Server.Testing;
using FastEndpoints;
using static Avala.Server.Activity.Tests.Endpoints.Batches;

namespace Avala.Server.Activity.Tests.Endpoints;

public sealed class EndpointTests
{
    private static readonly TokenSums Tokens = new(10, 20, 30, 40, 50);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string, string[]> Roles => new()
    {
        { nameof(IngestEventsEndpoint), [ActivityRoles.Instance] },
        { nameof(ListRepositoriesEndpoint), [ActivityRoles.Instance, ActivityRoles.Administrator] },
        { nameof(ListRepositoryJobsEndpoint), [ActivityRoles.Instance, ActivityRoles.Administrator] },
        { nameof(ListUsageEndpoint), [ActivityRoles.Instance, ActivityRoles.Administrator] },
    };

    [Fact]
    public async Task IngestingSendsTheCallersIdAndNameAsync()
    {
        var sender = new FakeSender(_ => Result<IngestionReceipt>.Success(new IngestionReceipt([Id(1)], [], [])));
        var endpoint = Factory.Create<IngestEventsEndpoint>(context => context.User = Caller, sender);

        await endpoint.HandleAsync(Request(Progressed(1)), Cancellation);

        var sent = Assert.IsType<IngestEvents>(Assert.Single(sender.Sent));
        Assert.Equal((Instance, "desktop"), (sent.Instance, sent.InstanceName));
    }

    [Fact]
    public async Task TheReceiptJoinsMapperAndHandlerEntriesAsync()
    {
        var sender = new FakeSender(_ => Result<IngestionReceipt>.Success(new IngestionReceipt([], [], [new RejectedEvent(Id(2), "the time is before 2025")])));
        var endpoint = Factory.Create<IngestEventsEndpoint>(context => context.User = Caller, sender);

        await endpoint.HandleAsync(Request($$"""{ "kind": "jobArchived", "id": "{{Id(1)}}" }""", Progressed(2)), Cancellation);

        Assert.Equal(
            (Id(1).ToString(), Id(2).ToString()),
            (Assert.Single(endpoint.Response.Deferred).Id, Assert.Single(endpoint.Response.Rejected).Id));
    }

    [Fact]
    public async Task ListingRepositoriesAnswersTheirActivityAsync()
    {
        var activity = new RepositoryActivity("remote:github.com/rick/avala", 3, 2, Tokens, [new CostSum("USD", CostBasis.ApiEquivalent, 1.5m)], Noon, Noon.AddHours(1));
        var endpoint = Factory.Create<ListRepositoriesEndpoint>(new FakeSender(_ => Result<FleetRepositories>.Success(new FleetRepositories([activity], 4))));

        await endpoint.HandleAsync(Cancellation);

        var repository = Assert.Single(endpoint.Response.Repositories);
        Assert.Equal(
            ("remote:github.com/rick/avala", 3, 2, 10m, "apiEquivalent", 4),
            (repository.Key, repository.Jobs, repository.Instances, repository.Tokens.Input, Assert.Single(repository.Costs).Basis, endpoint.Response.UnattributedJobs));
    }

    [Fact]
    public async Task ListingARepositorysJobsAnswersThemAsync()
    {
        var job = new RepositoryJob(Instance, "desktop", Job, JobStatus.NeedsHelp, HoldReason.BudgetExceeded, 2, VerificationOutcome.Failed, Tokens, [], Noon, Noon);
        var endpoint = Factory.Create<ListRepositoryJobsEndpoint>(new FakeSender(_ => Result<IReadOnlyList<RepositoryJob>>.Success([job])));

        await endpoint.HandleAsync(new ListRepositoryJobsRequest { Key = "remote:github.com/rick/avala" }, Cancellation);

        var listed = Assert.Single(endpoint.Response.Jobs);
        Assert.Equal(("desktop", "needsHelp", "budgetExceeded", "failed"), (listed.InstanceName, listed.Status, listed.Hold, listed.LastOutcome));
    }

    [Fact]
    public async Task ARefusedRepositoryKeyIsABadRequestAsync()
    {
        var endpoint = Factory.Create<ListRepositoryJobsEndpoint>(
            new FakeSender(_ => Result<IReadOnlyList<RepositoryJob>>.Invalid(new ValidationError("Key", "the repository key carries a port"))));

        await endpoint.HandleAsync(new ListRepositoryJobsRequest { Key = "remote:github.com:22/rick/avala" }, Cancellation);

        Assert.Equal(
            (400, "the repository key carries a port"),
            (endpoint.HttpContext.Response.StatusCode, Assert.Single(endpoint.ValidationFailures).ErrorMessage));
    }

    [Fact]
    public async Task ListingUsageAnswersTheReportAsync()
    {
        var day = new DateOnly(2026, 10, 19);
        var report = new UsageReport(
            day,
            day.AddDays(1),
            "Europe/Madrid",
            "day",
            new CostKind("USD", CostBasis.ApiEquivalent),
            new UsageTotals(Tokens, [new CostSum("USD", CostBasis.ApiEquivalent, 1.5m)], 1, 1),
            [new UsagePeriodSum(day, Tokens, [])],
            [new UsageShare(UsageShareKind.OutsideAnyJob, string.Empty, string.Empty, Tokens, [], 0)],
            [new UsageShare(UsageShareKind.Instance, Instance.ToString(), "desktop", Tokens, [], 1)],
            [new UsageShare(UsageShareKind.Model, "opus", "opus", Tokens, [], 0)]);
        var endpoint = Factory.Create<ListUsageEndpoint>(new FakeSender(_ => Result<UsageReport>.Success(report)));

        await endpoint.HandleAsync(new ListUsageRequest { From = "2026-10-19", To = "2026-10-20", Zone = "Europe/Madrid", Step = "day" }, Cancellation);

        var usage = endpoint.Response;
        Assert.Equal(
            ("2026-10-20", "apiEquivalent", "2026-10-19", "outsideAnyJob", "desktop", "opus"),
            (usage.To, usage.Reference?.Basis, Assert.Single(usage.Series).Start, Assert.Single(usage.Repositories).Kind, Assert.Single(usage.Instances).Name, Assert.Single(usage.Models).Model));
    }

    [Theory]
    [MemberData(nameof(Roles))]
    public void EachActivityEndpointDeclaresItsRoles(string endpoint, string[] roles) =>
        Assert.Equal(roles, Created(endpoint).Definition.AllowedRoles);

    private static BaseEndpoint Created(string endpoint) => endpoint switch
    {
        nameof(IngestEventsEndpoint) => Factory.Create<IngestEventsEndpoint>(Answering()),
        nameof(ListRepositoriesEndpoint) => Factory.Create<ListRepositoriesEndpoint>(Answering()),
        nameof(ListRepositoryJobsEndpoint) => Factory.Create<ListRepositoryJobsEndpoint>(Answering()),
        nameof(ListUsageEndpoint) => Factory.Create<ListUsageEndpoint>(Answering()),
        _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, "No such endpoint."),
    };

    private static FakeSender Answering() => new(_ => Result.Success());
}
