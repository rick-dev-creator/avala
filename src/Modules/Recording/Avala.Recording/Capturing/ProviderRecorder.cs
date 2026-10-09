using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Recording.Recordings;
using Avala.Sdk;

namespace Avala.Recording.Capturing;

internal sealed class ProviderRecorder(
    IRecordingSettings settings,
    IRecordingStore store,
    IEditedFiles files,
    TimeProvider clock) : IAgentProviderDecorator
{
    public IAgentProvider Decorate(IAgentProvider provider) => new RecordedProvider(provider, this);

    public async ValueTask<Result<IAgentSession, AgentError>> StartAsync(
        IAgentProvider provider,
        SessionOptions options,
        CancellationToken cancellationToken)
    {
        var current = await settings.LoadAsync(cancellationToken);
        var started = await provider.StartAsync(options, cancellationToken);

        return current.Enabled ? started.Map<IAgentSession>(session => Record(provider, options, current, session)) : started;
    }

    private RecordedSession Record(IAgentProvider provider, SessionOptions options, RecordingSettings current, IAgentSession session)
    {
        store.Begin(session.Id, new RecordingHeader(clock.GetUtcNow(), provider.Info, provider.CapabilitiesOn(options.Connection), session.Account, options), current);

        return new RecordedSession(session, options.WorkingDirectory, new Journal(session.Id, store, clock), files);
    }
}

internal sealed class RecordedProvider(IAgentProvider inner, ProviderRecorder recorder) : IAgentProvider
{
    public ProviderInfo Info => inner.Info;

    public CapabilitySet CapabilitiesOn(ConnectionEnvironment connection) => inner.CapabilitiesOn(connection);

    public ValueTask<Result<IAgentSession, AgentError>> StartAsync(SessionOptions options, CancellationToken cancellationToken) =>
        recorder.StartAsync(inner, options, cancellationToken);
}
