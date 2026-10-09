using Avala.Agents.Contracts.Connections;
using Avala.Sdk;

namespace Avala.Agents.Credentials;

internal sealed class ApiKeySource : ICredentialSource
{
    public const string Name = "apiKey";

    public string Source => Name;

    public ValueTask<Result<ConnectionEnvironment, ConnectionError>> ResolveAsync(CredentialRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(request.Reference.Match(
            variable => Environment.GetEnvironmentVariable(variable) is { Length: > 0 } key
                ? Result<ConnectionEnvironment, ConnectionError>.Success(new ConnectionEnvironment { ApiKey = new Secret(key) })
                : ConnectionError.MissingVariable,
            () => ConnectionError.MissingReference));
}
