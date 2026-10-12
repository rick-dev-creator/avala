using System.Net;
using System.Text.Json;
using Avala.Forges.Contracts;
using Avala.Forges.Testing;
using Avala.Gitea.Api;
using Avala.Testing;

namespace Avala.Gitea.Tests;

public sealed class GiteaRepositoriesTests
{
    private static readonly Uri Root = new("https://git.example.com");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string> Dialects => ["gitea", "forgejo"];

    [Theory]
    [MemberData(nameof(Dialects))]
    public async Task CreatingARepositoryPostsItsNameAndPrivacyUnderTheApiRootAndReadsWhereItLivesAsync(string dialect)
    {
        var forge = Forge(dialect);
        var server = new FixtureServer().Route("POST", "/api/v1/user/repos", HttpStatusCode.Created, await FixtureServer.FixtureAsync("gitea", "repository", Cancellation));
        var context = new ForgeContext(server.Api(Root, forge.Info.TokenScheme), Targets.Of(Root));

        var created = Outcomes.Succeeds(await forge.CreateAsync(context, new RepositoryDraft("shop", Private: true), Cancellation));

        Assert.Equal(
            new HostedRepository(new RepositoryAddress("git.example.com", "octo", "shop"), new Uri("https://git.example.com/octo/shop"), "https://git.example.com/octo/shop.git", "git@git.example.com:octo/shop.git"),
            created);
        var sent = Assert.Single(server.Sent);
        Assert.Equal(("POST", "/api/v1/user/repos", "token secret-value"), (sent.Method, sent.PathAndQuery, sent.Authorization));
        using var body = JsonDocument.Parse(sent.Body);
        Assert.Equal(("shop", true), (body.RootElement.GetProperty("name").GetString(), body.RootElement.GetProperty("private").GetBoolean()));
    }

    [Theory]
    [InlineData("repository-taken", HttpStatusCode.Conflict, ForgeError.NameTaken)]
    [InlineData("repository-taken", HttpStatusCode.UnprocessableEntity, ForgeError.Rejected)]
    [InlineData("unauthorized", HttpStatusCode.Unauthorized, ForgeError.Unauthorized)]
    public async Task ARefusedRepositorySaysWhyAsync(string fixture, HttpStatusCode status, ForgeError expected)
    {
        var forge = Forge("forgejo");
        var server = new FixtureServer().Route("POST", "/api/v1/user/repos", status, await FixtureServer.FixtureAsync("gitea", fixture, Cancellation));
        var context = new ForgeContext(server.Api(Root, forge.Info.TokenScheme), Targets.Of(Root));

        Assert.Equal(expected, Outcomes.FailsWith(await forge.CreateAsync(context, new RepositoryDraft("shop", Private: false), Cancellation)));
    }

    [Fact]
    public void RepositoriesLiveOnTheHostOfTheConnection() =>
        Assert.Equal("git.example.com", Forge("gitea").GitHost(Root));

    private static GiteaForge Forge(string dialect) => new(dialect == "gitea" ? GiteaDialect.Gitea : GiteaDialect.Forgejo);
}
