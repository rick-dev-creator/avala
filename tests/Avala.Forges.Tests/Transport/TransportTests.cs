using System.Net;
using Avala.Forges.Contracts;
using Avala.Forges.Policy;
using Avala.Forges.Testing;
using Avala.Forges.Transport;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;

namespace Avala.Forges.Tests.Transport;

public sealed class TransportTests
{
    private static readonly Uri Root = new("https://ghe.example.com/api/v3");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARequestGoesUnderTheConnectionsUrlWithItsFieldsAsAJsonObjectAsync()
    {
        var server = new FixtureServer().Route("POST", "/api/v3/repos/octo/shop/pulls", HttpStatusCode.Created, "{}");

        var response = Outcomes.Succeeds(await Api(server).SendAsync(new ForgeRequest(ForgeMethod.Post, "/repos/octo/shop/pulls") { Fields = [new("title", "Say \"hi\"")] }, Cancellation));

        Assert.Equal((201, "{}"), (response.Status, response.Body));
        Assert.Equal(new SentRequest("POST", "/api/v3/repos/octo/shop/pulls", "Bearer secret-value", "{\"title\":\"Say \\u0022hi\\u0022\"}"), server.Sent[0]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task AnUnsetOrEmptyTokenVariableIsAMissingCredentialAndSendsNothingAsync(string? value)
    {
        var server = new FixtureServer();

        Assert.Equal(ForgeError.MissingCredential, Outcomes.FailsWith(await Api(server, value).SendAsync(new ForgeRequest(ForgeMethod.Get, "/user"), Cancellation)));
        Assert.Empty(server.Sent);
    }

    [Theory]
    [InlineData(401, false, "Unauthorized")]
    [InlineData(403, false, "Unauthorized")]
    [InlineData(403, true, "RateLimited")]
    [InlineData(404, false, "NotFound")]
    [InlineData(409, false, "Rejected")]
    [InlineData(422, false, "Rejected")]
    [InlineData(429, false, "RateLimited")]
    [InlineData(500, false, "Unreachable")]
    [InlineData(502, false, "Unreachable")]
    public void AReplyThatIsNotASuccessIsATypedError(int status, bool exhausted, string expected) =>
        Assert.Equal(Enum.Parse<ForgeError>(expected), Outcomes.FailsWith(Statuses.Of(status, "{}", exhausted)));

    [Fact]
    public async Task AnExhaustedRateLimitHeaderMakesA403RateLimitedAsync()
    {
        var handler = new LimitedServer();
        var api = new HttpForgeApi(new HttpClient(handler), Root, Option<ForgeToken>.None, _ => Option<string>.None);

        Assert.Equal(ForgeError.RateLimited, Outcomes.FailsWith(await api.SendAsync(new ForgeRequest(ForgeMethod.Get, "/user"), Cancellation)));
    }

    [Fact]
    public async Task TheGhCliRunsThroughTheProcessRunnerAndItsIncludedStatusLineIsReadAsync()
    {
        var runner = new ScriptedRunner(new ProcessOutcome(0, "HTTP/2.0 201 Created\nContent-Type: application/json\n\n{\"number\":7}", string.Empty));
        var api = new CliForgeApi(runner, new CliForge(new CliCall("gh", ["api", "--include", "-f", "title=Hi", "repos/octo/shop/pulls"], CliStatusChannel.Output)), Targets.Of(new Uri("https://api.github.com")));

        var response = Outcomes.Succeeds(await api.SendAsync(new ForgeRequest(ForgeMethod.Post, "/repos/octo/shop/pulls") { Fields = [new("title", "Hi")] }, Cancellation));

        Assert.Equal((201, "{\"number\":7}"), (response.Status, response.Body));
        Assert.Equal("gh", runner.Requests[0].FileName);
        Assert.Contains("title=Hi", runner.Requests[0].Arguments);
    }

    [Fact]
    public async Task TheTeaCliStatusIsReadFromStandardErrorAndAFailureIsTypedAsync()
    {
        var runner = new ScriptedRunner(new ProcessOutcome(1, "{\"message\":\"not found\"}", "HTTP/1.1 404 Not Found\n"));
        var api = new CliForgeApi(runner, new CliForge(new CliCall("tea", ["api", "--include", "repos/octo/shop/pulls/9"], CliStatusChannel.Error)), Targets.Of(new Uri("https://git.example.com")));

        Assert.Equal(ForgeError.NotFound, Outcomes.FailsWith(await api.SendAsync(new ForgeRequest(ForgeMethod.Get, "/api/v1/repos/octo/shop/pulls/9"), Cancellation)));
    }

    [Fact]
    public async Task ACliThatIsNotInstalledIsCliUnavailableAsync()
    {
        var api = new CliForgeApi(new ScriptedRunner(ProcessError.NotFound), new CliForge(new CliCall("gh", ["api", "user"], CliStatusChannel.Output)), Targets.Of(new Uri("https://api.github.com")));

        Assert.Equal(ForgeError.CliUnavailable, Outcomes.FailsWith(await api.SendAsync(new ForgeRequest(ForgeMethod.Get, "/user"), Cancellation)));
    }

    [Fact]
    public async Task AForgeThatDeclaresNoCliIsCliUnavailableAsync()
    {
        var api = new CliForgeApi(new ScriptedRunner(new ProcessOutcome(0, string.Empty, string.Empty)), new CliForge(Option<CliCall>.None), Targets.Of(new Uri("https://forge.invalid")));

        Assert.Equal(ForgeError.CliUnavailable, Outcomes.FailsWith(await api.SendAsync(new ForgeRequest(ForgeMethod.Get, "/user"), Cancellation)));
    }

    [Theory]
    [InlineData("Environment", "FORGE_TOKEN", "secret-value", false)]
    [InlineData("Environment", "FORGE_TOKEN", "", true)]
    [InlineData("Environment", "FORGE_TOKEN", null, true)]
    [InlineData("Environment", null, null, true)]
    [InlineData("Cli", null, null, false)]
    [InlineData("None", null, null, false)]
    public void OnlyAnEnvironmentCredentialWithoutAValueIsAMissingCredential(string source, string? reference, string? value, bool missing)
    {
        using var http = new ForgeHttp();
        var transports = new ForgeTransports(new ScriptedRunner(ProcessError.NotFound), http) { Environment = name => name == "FORGE_TOKEN" ? value.ToOption() : Option<string>.None };
        var declaration = new ForgeDeclaration(new ForgeName("work"), "github", Option<Uri>.None, Enum.Parse<CredentialSource>(source), reference.ToOption());

        Assert.Equal(missing ? Option<ForgeError>.Some(ForgeError.MissingCredential) : Option<ForgeError>.None, transports.Problem(declaration));
    }

    private static HttpForgeApi Api(FixtureServer server, string? value = "secret-value") =>
        new(new HttpClient(server), Root, new ForgeToken("Bearer", "FORGE_TOKEN"), name => name == "FORGE_TOKEN" ? value.ToOption() : Option<string>.None);

    private sealed class CliForge(Option<CliCall> call) : IForge
    {
        public ForgeInfo Info { get; } = new("cli", "Command line");

        public Option<CliCall> CliFor(ForgeRequest request, ForgeTarget target) => call;

        public ValueTask<Result<Option<PullRequestRef>, ForgeError>> FindAsync(ForgeContext context, string head, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<PullRequestRef, ForgeError>> OpenAsync(ForgeContext context, PullRequestDraft draft, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<PullRequestState, ForgeError>> ReadAsync(ForgeContext context, PullRequestRef pullRequest, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<CommentRef, ForgeError>> CommentAsync(ForgeContext context, PullRequestRef pullRequest, string body, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class LimitedServer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{}") };
            response.Headers.Add("x-ratelimit-remaining", "0");

            return Task.FromResult(response);
        }
    }
}

internal sealed class ScriptedRunner(Result<ProcessOutcome, ProcessError> outcome) : IProcessRunner
{
    public List<ProcessRequest> Requests { get; } = [];

    public ValueTask<Result<ProcessOutcome, ProcessError>> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        return ValueTask.FromResult(outcome);
    }
}
