using Avala.Agents.ConnectionFiles;
using Avala.Agents.Connections;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Agents.Sessions;
using Avala.Sdk;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Agents.Tests.Connections;

internal static class Connected
{
    public static SessionStarter Starter(
        IEnumerable<IAgentProvider> providers,
        IEnumerable<HarnessTool> tools,
        IEnumerable<IAgentProviderDecorator> decorators) =>
        Starter(Registry(providers), tools, decorators);

    public static SessionStarter Starter(
        ConnectionRegistry registry,
        IEnumerable<HarnessTool> tools,
        IEnumerable<IAgentProviderDecorator> decorators) =>
        new(registry, tools, decorators, NullLogger<SessionStarter>.Instance);

    public static ConnectionRegistry Registry(IEnumerable<IAgentProvider> providers, params ICredentialSource[] sources) =>
        new(new DeclaredFile(Option<ConnectionDeclarations>.None), providers, sources);

    public static ConnectionRegistry Registry(string file, IEnumerable<IAgentProvider> providers, params ICredentialSource[] sources) =>
        new(new DeclaredFile(ConnectionFileParser.Parse(file).Map(Option<ConnectionDeclarations>.Some)), providers, sources);

    private sealed class DeclaredFile(Result<Option<ConnectionDeclarations>, ConnectionError> declared) : IConnectionFile
    {
        public ValueTask<Result<Option<ConnectionDeclarations>, ConnectionError>> LoadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(declared);
    }
}
