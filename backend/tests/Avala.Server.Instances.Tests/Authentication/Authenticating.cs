using System.Text.Encodings.Web;
using Ardalis.Result;
using Avala.Server.Instances.Contracts.Authentication;
using Avala.Server.Instances.EndPoints.Authentication;
using Avala.Server.Instances.Tests.Fakes;
using Avala.Server.Testing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Avala.Server.Instances.Tests.Authentication;

internal sealed class Authenticating
{
    public const string AdministratorKey = "an-administrator-key-of-forty-characters";

    public static readonly AuthenticatedInstance Desktop = new(Guid.Parse("0199f6a0-0000-7000-8000-000000000001"), "desktop");

    private Authenticating(Func<object, object> answer, string[] authorization)
    {
        Sender = new FakeSender(answer);
        Context = new DefaultHttpContext();

        if (authorization.Length > 0)
        {
            Context.Request.Headers.Authorization = authorization;
        }
    }

    public FakeSender Sender { get; }

    public DefaultHttpContext Context { get; }

    public static Authenticating With(params string[] authorization) =>
        new(_ => Result<AuthenticatedInstance>.Unauthorized(), authorization);

    public static Authenticating Knowing(AuthenticatedInstance instance, params string[] authorization) =>
        new(_ => Result<AuthenticatedInstance>.Success(instance), authorization);

    public async Task<AuthenticateResult> AuthenticateAsync() => await (await HandlerAsync()).AuthenticateAsync();

    public async Task ChallengeAsync() => await (await HandlerAsync()).ChallengeAsync(new AuthenticationProperties());

    private async Task<ApiKeyAuthenticationHandler> HandlerAsync()
    {
        var handler = new ApiKeyAuthenticationHandler(
            new FakeSchemeOptions(),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            Sender,
            Options.Create(new AdministratorSettings { AdministratorKey = AdministratorKey }));
        await handler.InitializeAsync(new AuthenticationScheme(ApiKeyAuthentication.Scheme, null, typeof(ApiKeyAuthenticationHandler)), Context);

        return handler;
    }
}
