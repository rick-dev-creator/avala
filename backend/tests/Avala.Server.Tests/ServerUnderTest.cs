using Ardalis.Result;
using Avala.Server.Activity.Contracts.Ingestion;
using Avala.Server.Activity.Contracts.Repositories;
using Avala.Server.Activity.Contracts.RepositoryJobs;
using Avala.Server.Activity.Contracts.Usage;
using Avala.Server.Instances.Contracts.Authentication;
using Avala.Server.Testing;
using Mediator;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avala.Server.Tests;

public sealed class ServerUnderTest : WebApplicationFactory<Program>
{
    public const string AdministratorKey = "an-administrator-key-for-the-host-tests";

    public ServerUnderTest() => Sender = new FakeSender(Answer);

    public string ConfiguredKey { get; init; } = AdministratorKey;

    public bool AppliesMigrations { get; init; }

    internal AuthenticatedInstance Caller { get; init; } = new(Guid.Parse("0199f6a0-0000-7000-8000-000000000003"), "desktop");

    internal FleetRepositories Repositories { get; init; } = new([], 0);

    internal IReadOnlyList<RepositoryJob> Jobs { get; init; } = [];

    internal Func<ListUsage, Result<UsageReport>> Usage { get; init; } = query => Result<UsageReport>.Success(new UsageReport(
        DateOnly.FromDayNumber(0),
        DateOnly.FromDayNumber(1),
        query.Zone ?? string.Empty,
        query.Step ?? string.Empty,
        null,
        new UsageTotals(new Activity.Contracts.TokenSums(0, 0, 0, 0, 0), [], 0, 0),
        [],
        [],
        [],
        []));

    public FakeSender Sender { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Authentication:AdministratorKey", ConfiguredKey);
        builder.UseSetting("Database:ApplyMigrations", AppliesMigrations ? "true" : "false");
        builder.UseSetting("ConnectionStrings:fleet", "Host=127.0.0.1;Port=1;Timeout=1");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISender>();
            services.AddScoped<ISender>(_ => Sender);
        });
    }

    private object Answer(object message) => message switch
    {
        AuthenticateInstance => Result<AuthenticatedInstance>.Success(Caller),
        IngestEvents ingest => Result<IngestionReceipt>.Success(new IngestionReceipt([.. ingest.Events.Select(incoming => incoming.Id)], [], [])),
        ListRepositories => Result<FleetRepositories>.Success(Repositories),
        ListRepositoryJobs => Result<IReadOnlyList<RepositoryJob>>.Success(Jobs),
        ListUsage usage => Usage(usage),
        _ => Result.Success(),
    };
}
