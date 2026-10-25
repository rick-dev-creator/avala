using System.Net;

namespace Avala.Server.Tests;

public sealed class HealthTests(ServerUnderTest server) : IClassFixture<ServerUnderTest>
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task HealthIsUnhealthyWithoutTheDatabaseAsync()
    {
        using var response = await server.CreateClient().GetAsync(new Uri("/health", UriKind.Relative), Cancellation);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task AliveAnswersWithoutTheDatabaseAsync()
    {
        using var response = await server.CreateClient().GetAsync(new Uri("/alive", UriKind.Relative), Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
