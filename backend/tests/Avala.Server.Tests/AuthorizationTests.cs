using System.Net;
using System.Net.Http.Headers;

namespace Avala.Server.Tests;

public sealed class AuthorizationTests : IClassFixture<ServerUnderTest>
{
    private const string AnInstanceKey = "avk_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private static readonly Guid Id = Guid.Parse("0199f6a0-0000-7000-8000-000000000004");

    private readonly ServerUnderTest server;

    public AuthorizationTests(ServerUnderTest server) => this.server = server;

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Endpoints => new()
    {
        { "POST", "/instances" },
        { "GET", $"/instances/{Id}" },
        { "POST", $"/instances/{Id}/revocation" },
        { "GET", "/instances/me" },
    };

    public static TheoryData<string, string> AdministratorEndpoints => new()
    {
        { "POST", "/instances" },
        { "GET", $"/instances/{Id}" },
        { "POST", $"/instances/{Id}/revocation" },
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task AnonymousRequestsAreChallengedAsync(string method, string path)
    {
        using var response = await SendAsync(method, path, key: null);

        Assert.Equal((HttpStatusCode.Unauthorized, "Bearer"), (response.StatusCode, response.Headers.WwwAuthenticate.ToString()));
    }

    [Theory]
    [MemberData(nameof(AdministratorEndpoints))]
    public async Task AnInstanceKeyIsForbiddenFromAdministeringAsync(string method, string path)
    {
        using var response = await SendAsync(method, path, AnInstanceKey);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TheAdministratorKeyIsForbiddenFromWhoAmIAsync()
    {
        using var response = await SendAsync("GET", "/instances/me", ServerUnderTest.AdministratorKey);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/alive")]
    [InlineData("/health")]
    public async Task HealthNeedsNoKeyAsync(string path)
    {
        using var response = await SendAsync("GET", path, key: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TheOpenApiDocumentDeclaresTheBearerSchemeAsync()
    {
        var document = await server.CreateClient().GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), Cancellation);

        Assert.Contains("\"bearer\"", document, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<HttpResponseMessage> SendAsync(string method, string path, string? key)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative));

        if (method == "POST")
        {
            request.Content = new StringContent("""{ "name": "desktop" }""", System.Text.Encoding.UTF8, "application/json");
        }

        if (key is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        return await server.CreateClient().SendAsync(request, Cancellation);
    }
}
