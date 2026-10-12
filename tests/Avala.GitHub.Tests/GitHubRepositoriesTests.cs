using System.Net;
using System.Text.Json;
using Avala.Forges.Contracts;
using Avala.Forges.Testing;
using Avala.GitHub.Api;
using Avala.Testing;

namespace Avala.GitHub.Tests;

public sealed class GitHubRepositoriesTests
{
    private static readonly Uri Root = new("https://api.github.com");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreatingARepositoryPostsItsNameAndPrivacyForTheTokensUserAndReadsWhereItLivesAsync()
    {
        var server = new FixtureServer().Route("POST", "/user/repos", HttpStatusCode.Created, await FixtureServer.FixtureAsync("github", "repository", Cancellation));
        var context = new ForgeContext(server.Api(Root), Targets.Of(Root));

        var created = Outcomes.Succeeds(await new GitHubForge().CreateAsync(context, new RepositoryDraft("shop", Private: true), Cancellation));

        Assert.Equal(
            new HostedRepository(new RepositoryAddress("github.com", "octo", "shop"), new Uri("https://github.com/octo/shop"), "https://github.com/octo/shop.git", "git@github.com:octo/shop.git"),
            created);
        var sent = Assert.Single(server.Sent);
        Assert.Equal(("POST", "/user/repos", "Bearer secret-value"), (sent.Method, sent.PathAndQuery, sent.Authorization));
        using var body = JsonDocument.Parse(sent.Body);
        Assert.Equal(("shop", true), (body.RootElement.GetProperty("name").GetString(), body.RootElement.GetProperty("private").GetBoolean()));
    }

    [Theory]
    [InlineData("repository-taken", HttpStatusCode.UnprocessableEntity, ForgeError.NameTaken)]
    [InlineData("repository-invalid", HttpStatusCode.UnprocessableEntity, ForgeError.Rejected)]
    [InlineData("unauthorized", HttpStatusCode.Unauthorized, ForgeError.Unauthorized)]
    public async Task ARefusedRepositorySaysWhyAsync(string fixture, HttpStatusCode status, ForgeError expected)
    {
        var server = new FixtureServer().Route("POST", "/user/repos", status, await FixtureServer.FixtureAsync("github", fixture, Cancellation));
        var context = new ForgeContext(server.Api(Root), Targets.Of(Root));

        Assert.Equal(expected, Outcomes.FailsWith(await new GitHubForge().CreateAsync(context, new RepositoryDraft("shop", Private: false), Cancellation)));
    }

    [Theory]
    [InlineData("https://api.github.com", "github.com")]
    [InlineData("https://ghe.example.com/api/v3", "ghe.example.com")]
    public void RepositoriesLiveOnTheWebHostOfTheApi(string api, string host) =>
        Assert.Equal(host, new GitHubForge().GitHost(new Uri(api)));
}
