using Avala.Agents.ConnectionFiles;
using Avala.Agents.Connections;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Agents.Sessions;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;
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
        Starter(registry, tools, decorators, new RecordingProcessTrees());

    public static SessionStarter Starter(
        ConnectionRegistry registry,
        IEnumerable<HarnessTool> tools,
        IEnumerable<IAgentProviderDecorator> decorators,
        IProcessTrees trees) =>
        new(registry, new ProviderChain(tools, decorators), trees, NullLogger<SessionStarter>.Instance);

    public static ConnectionRegistry Registry(IEnumerable<IAgentProvider> providers, params ICredentialSource[] sources) =>
        new(new DeclaredFile(Option<ConnectionDeclarations>.None), providers, sources, []);

    public static ConnectionRegistry Registry(string file, IEnumerable<IAgentProvider> providers, params ICredentialSource[] sources) =>
        new(new DeclaredFile(ConnectionFileParser.Parse(file).Map(Option<ConnectionDeclarations>.Some)), providers, sources, []);

    public static ConnectionRegistry Discovering(
        Result<Option<ConnectionDeclarations>, ConnectionError> file,
        IEnumerable<IAgentProvider> providers,
        IEnumerable<IConnectionDiscovery> discoveries,
        params ICredentialSource[] sources) =>
        new(new DeclaredFile(file), providers, sources, discoveries);

    private sealed class DeclaredFile(Result<Option<ConnectionDeclarations>, ConnectionError> declared) : IConnectionFile
    {
        public ValueTask<Result<Option<ConnectionDeclarations>, ConnectionError>> LoadAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(declared);

        public ValueTask<Result<ConnectionDeclarations, ConnectionError>> ChangeAsync(IConnectionChange change, CancellationToken cancellationToken)
        {
            var changed = declared.Bind(found => change.ApplyTo(found.Match(declarations => declarations, () => ConnectionDeclarations.Nothing)));
            declared = changed.Map(Option<ConnectionDeclarations>.Some);

            return ValueTask.FromResult(changed);
        }
    }
}
