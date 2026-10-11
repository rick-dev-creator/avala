using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Delivering;
using Avala.Sdk;
using Avala.Triggers.Contracts;

namespace Avala.Forges.Polling;

internal sealed record EventReader(ResolvedForge Forge, IForgeEvents Events)
{
    public async Task<Result<ForgeEventPage, ForgeError>> ReadAsync(ForgeEventQuery query, CancellationToken cancellationToken) =>
        await Events.ReadSinceAsync(Forge.Context, query, cancellationToken);

    public async Task AcknowledgeAsync(ForgeEvent happened, string body, CancellationToken cancellationToken)
    {
        if (happened.Number > 0)
        {
            var issue = new PullRequestRef(happened.Number, happened.Link.Match(link => link, () => Forge.Context.Target.Api), string.Empty, string.Empty);
            _ = await Forge.Forge.CommentAsync(Forge.Context, issue, body, cancellationToken);
        }
    }
}

internal sealed class EventSource(ForgeConnections connections, IGitRemote git)
{
    public ValueTask<TimeSpan> PollAsync(CancellationToken cancellationToken) => connections.PollAsync(cancellationToken);

    public async Task<string> TitleAsync(ForgeRule rule, CancellationToken cancellationToken) =>
        (await connections.UsableAsync(new ForgeName(rule.Connection), cancellationToken)).Match(forge => forge.Info.Name, _ => rule.Connection);

    public async Task<Result<EventReader, ForgeError>> ReaderAsync(TriggerState trigger, ForgeRule rule, CancellationToken cancellationToken)
    {
        if (!(await git.UrlAsync(trigger.Repository, rule.Remote, cancellationToken)).TryGetValue(out var url, out var error))
        {
            return error == ForgeError.GitFailed ? ForgeError.InvalidRemote : error;
        }

        return (await connections.ResolveAsync(new ForgeName(rule.Connection), url, cancellationToken))
            .Bind(resolved => resolved.Forge is IForgeEvents events
                ? Result<EventReader, ForgeError>.Success(new EventReader(resolved, events))
                : ForgeError.EventsUnsupported);
    }
}
