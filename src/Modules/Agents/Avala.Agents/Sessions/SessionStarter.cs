using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Agents.Sessions;

internal sealed class SessionStarter(
    IEnumerable<IAgentProvider> providers,
    IEnumerable<HarnessTool> tools,
    IEnumerable<IAgentProviderDecorator> decorators)
{
    public async Task<Result<StartedSession, AgentError>> StartAsync(AgentRequest request, CancellationToken cancellationToken)
    {
        if (providers.FirstOrDefault() is not { } registered)
        {
            return AgentError.ProviderUnavailable;
        }

        var provider = decorators.Aggregate(registered, (inner, decorator) => decorator.Decorate(inner));

        var fresh = new SessionOptions(request.WorkingDirectory, PermissionMode.AskEveryTime)
        {
            Tools = provider.Capabilities.AcceptsTools ? [.. tools] : [],
        };
        var resumed = provider.Capabilities.CanResume
            ? await request.Resume.Match(
                token => ResumeAsync(provider, fresh with { Resume = token }, cancellationToken),
                () => Task.FromResult(Option<StartedSession>.None))
            : Option<StartedSession>.None;

        return await resumed.Match(
            started => Task.FromResult(Result<StartedSession, AgentError>.Success(started)),
            async () => (await provider.StartAsync(fresh, cancellationToken)).Map(session => new StartedSession(provider, session, Resumed: false)));
    }

    private static async Task<Option<StartedSession>> ResumeAsync(IAgentProvider provider, SessionOptions options, CancellationToken cancellationToken) =>
        (await provider.StartAsync(options, cancellationToken)).Match(
            session => Option<StartedSession>.Some(new StartedSession(provider, session, Resumed: true)),
            _ => Option<StartedSession>.None);
}

internal sealed record StartedSession(IAgentProvider Provider, IAgentSession Session, bool Resumed);
