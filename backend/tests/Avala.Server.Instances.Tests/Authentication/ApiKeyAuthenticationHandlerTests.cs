using System.Security.Claims;
using Avala.Server.Instances.Domain;
using Avala.Server.Instances.EndPoints.Authentication;

namespace Avala.Server.Instances.Tests.Authentication;

public sealed class ApiKeyAuthenticationHandlerTests
{
    private static readonly string AKey = InstanceKey.Issue();

    [Fact]
    public async Task NoAuthorizationHeaderAuthenticatesNobodyAsync() =>
        Assert.True((await Authenticating.With().AuthenticateAsync()).None);

    [Fact]
    public async Task ASchemeOtherThanBearerAuthenticatesNobodyAsync() =>
        Assert.True((await Authenticating.With($"Basic {AKey}").AuthenticateAsync()).None);

    [Fact]
    public async Task TheBearerSchemeIsMatchedWhateverItsCaseAsync() =>
        Assert.True((await Authenticating.Knowing(Authenticating.Desktop, $"bEaReR {AKey}").AuthenticateAsync()).Succeeded);

    [Fact]
    public async Task SeveralAuthorizationHeadersFailAsync() =>
        Assert.NotNull((await Authenticating.With($"Bearer {AKey}", $"Bearer {AKey}").AuthenticateAsync()).Failure);

    [Fact]
    public async Task AnEmptyTokenFailsAsync() =>
        Assert.NotNull((await Authenticating.With("Bearer ").AuthenticateAsync()).Failure);

    [Fact]
    public async Task TheAdministratorKeyAuthenticatesTheAdministratorAsync()
    {
        var result = await Authenticating.With($"Bearer {Authenticating.AdministratorKey}").AuthenticateAsync();

        Assert.True(result.Principal?.IsInRole(ApiKeyAuthentication.Administrator));
    }

    [Fact]
    public async Task AnInstanceKeyAuthenticatesItsInstanceAsync()
    {
        var principal = (await Authenticating.Knowing(Authenticating.Desktop, $"Bearer {AKey}").AuthenticateAsync()).Principal;

        Assert.Equal(
            (true, Authenticating.Desktop.Id.ToString(), "desktop", true),
            (principal?.Identity?.IsAuthenticated, principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, principal?.Identity?.Name, principal?.IsInRole(ApiKeyAuthentication.Instance)));
    }

    [Fact]
    public async Task AnUnknownKeyFailsAsync() =>
        Assert.NotNull((await Authenticating.With($"Bearer {AKey}").AuthenticateAsync()).Failure);

    [Fact]
    public async Task ATokenThatIsNotAnInstanceKeyNeverReachesTheSenderAsync()
    {
        var authenticating = Authenticating.With("Bearer not-an-instance-key");

        await authenticating.AuthenticateAsync();

        Assert.Empty(authenticating.Sender.Sent);
    }

    [Fact]
    public async Task TheChallengeAsksForABearerTokenAsync()
    {
        var authenticating = Authenticating.With();

        await authenticating.ChallengeAsync();

        Assert.Equal((401, "Bearer"), (authenticating.Context.Response.StatusCode, authenticating.Context.Response.Headers.WWWAuthenticate.ToString()));
    }
}
