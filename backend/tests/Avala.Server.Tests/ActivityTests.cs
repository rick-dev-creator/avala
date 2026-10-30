using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Avala.Server.Activity.Contracts.RepositoryJobs;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Server.Tests;

public sealed class ActivityTests(ServerUnderTest server) : IClassFixture<ServerUnderTest>
{
    private const string AnInstanceKey = "avk_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private const string Events = "/activity/v1/events";

    private const string Time = "2026-10-25T12:00:00Z";

    private const string Usage = "/activity/v1/usage?from=2026-10-19&to=2026-10-26&zone=UTC&step=day";

    private static readonly Guid Job = Guid.Parse("0199f6a0-0000-7000-8000-000000000010");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string?, HttpStatusCode> Senders => new()
    {
        { null, HttpStatusCode.Unauthorized },
        { ServerUnderTest.AdministratorKey, HttpStatusCode.Forbidden },
        { AnInstanceKey, HttpStatusCode.OK },
    };

    public static TheoryData<string, string?, HttpStatusCode> Readers => new()
    {
        { "/activity/v1/repositories", ServerUnderTest.AdministratorKey, HttpStatusCode.OK },
        { "/activity/v1/repositories", AnInstanceKey, HttpStatusCode.OK },
        { "/activity/v1/repositories", null, HttpStatusCode.Unauthorized },
        { "/activity/v1/repositories/jobs?key=remote:github.com/rick/avala", ServerUnderTest.AdministratorKey, HttpStatusCode.OK },
        { "/activity/v1/repositories/jobs?key=remote:github.com/rick/avala", AnInstanceKey, HttpStatusCode.OK },
        { "/activity/v1/repositories/jobs?key=remote:github.com/rick/avala", null, HttpStatusCode.Unauthorized },
        { Usage, ServerUnderTest.AdministratorKey, HttpStatusCode.OK },
        { Usage, AnInstanceKey, HttpStatusCode.OK },
        { Usage, null, HttpStatusCode.Unauthorized },
    };

    [Theory]
    [MemberData(nameof(Senders))]
    public async Task OnlyInstancesSendActivityAsync(string? key, HttpStatusCode expected)
    {
        using var response = await PostAsync(Batch(Progressed(1)), key);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Readers))]
    public async Task TheAdministratorAndInstancesReadActivityAsync(string path, string? key, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        Authorize(request, key);

        using var response = await server.CreateClient().SendAsync(request, Cancellation);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task ABatchOfMoreThanFiveHundredEventsIsABadRequestAsync()
    {
        using var response = await PostAsync(Batch([.. Enumerable.Range(1, 501).Select(Progressed)]), AnInstanceKey);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ABodyWithoutEventsIsABadRequestAsync()
    {
        using var response = await PostAsync("{}", AnInstanceKey);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AnUnknownKindInALiteralBodyIsDeferredAloneAsync()
    {
        using var response = await PostAsync(Batch($$"""{ "kind": "jobArchived", "id": "{{Id(1)}}" }""", Progressed(2)), AnInstanceKey);

        using var receipt = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(
            (Id(1).ToString(), "unknown kind 'jobArchived'", Id(2).ToString()),
            (receipt.RootElement.GetProperty("deferred")[0].GetProperty("id").GetString(),
                receipt.RootElement.GetProperty("deferred")[0].GetProperty("reason").GetString(),
                receipt.RootElement.GetProperty("accepted")[0].GetString()));
    }

    [Fact]
    public async Task ARepositoryKeyWithSlashesReachesTheQueryAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/activity/v1/repositories/jobs?key=remote:github.com/owner/name", UriKind.Relative));
        Authorize(request, AnInstanceKey);

        using var response = await server.CreateClient().SendAsync(request, Cancellation);

        Assert.Contains(server.Sender.Sent.OfType<ListRepositoryJobs>(), query => query.Key == "remote:github.com/owner/name");
    }

    [Fact]
    public async Task AUsageQueryTheModuleRefusesIsABadRequestAsync()
    {
        await using var refusing = new ServerUnderTest
        {
            Usage = _ => Ardalis.Result.Result<Activity.Contracts.Usage.UsageReport>.Invalid(new Ardalis.Result.ValidationError("To", "the range must end after it starts")),
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/activity/v1/usage?from=2026-10-26&to=2026-10-19&zone=UTC&step=day", UriKind.Relative));
        Authorize(request, AnInstanceKey);

        using var response = await refusing.CreateClient().SendAsync(request, Cancellation);

        Assert.Equal((HttpStatusCode.BadRequest, true), (response.StatusCode, (await response.Content.ReadAsStringAsync(Cancellation)).Contains("the range must end after it starts", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TheGoldenUsageRequestReachesTheQueryAsItWasSentAsync()
    {
        var query = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "FleetWire", "v1", "requests", "usage.query"), Cancellation);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/activity/v1/usage?{query}", UriKind.Relative));
        Authorize(request, AnInstanceKey);

        using var response = await server.CreateClient().SendAsync(request, Cancellation);

        Assert.Contains(
            new Activity.Contracts.Usage.ListUsage("2026-10-19", "2026-10-26", "Europe/Madrid", "day", "remote:github.com/rick/avala"),
            server.Sender.Sent.OfType<Activity.Contracts.Usage.ListUsage>());
    }

    [Fact]
    public void TheActivityValidatorsAreRegistered()
    {
        using var scope = server.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<IValidator<ListRepositoryJobs>>());
    }

    private static Guid Id(int number) => Guid.Parse($"0199f6a0-0000-7000-8000-{number:D12}");

    private static string Progressed(int id) =>
        $$"""{ "kind": "jobProgressed", "id": "{{Id(id)}}", "occurredAt": "{{Time}}", "job": "{{Job}}", "status": "running" }""";

    private static string Batch(params string[] events) => $$"""{ "events": [{{string.Join(", ", events)}}] }""";

    private static void Authorize(HttpRequestMessage request, string? key)
    {
        if (key is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }
    }

    private async Task<HttpResponseMessage> PostAsync(string body, string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(Events, UriKind.Relative))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        Authorize(request, key);

        return await server.CreateClient().SendAsync(request, Cancellation);
    }
}
