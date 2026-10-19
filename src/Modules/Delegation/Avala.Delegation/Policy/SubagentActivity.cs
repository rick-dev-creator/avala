using System.Collections.Immutable;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Delegation.Policy;

internal sealed record InProcessSubagent(SessionId Session, ItemId Item, string Name)
{
    public Option<ItemOutcome> Outcome { get; init; }
}

internal sealed record SubagentActivity
{
    private const string Titled = "Subagent: ";

    public static SubagentActivity None { get; } = new();

    public ImmutableList<InProcessSubagent> Subagents { get; private init; } = [];

    private ImmutableDictionary<(SessionId Session, ItemId Item), ItemId> Steps { get; init; } = ImmutableDictionary<(SessionId, ItemId), ItemId>.Empty;

    private ImmutableDictionary<(SessionId Session, ItemId Item), Asking> Requests { get; init; } = ImmutableDictionary<(SessionId, ItemId), Asking>.Empty;

    public Asking Asking => Strongest(Requests.Values);

    public SubagentActivity Apply(IAgentEvent activity) => activity switch
    {
        ItemStarted { Parent.IsSome: true } step => this with { Steps = Steps.SetItem((step.Session, step.Item), step.Parent.Match(parent => parent, () => step.Item)) },
        ItemStarted { Kind: ItemKind.Subagent } started => this with { Subagents = Subagents.Add(new InProcessSubagent(started.Session, started.Item, Named(started.Title))) },
        ItemCompleted completed => Completed(completed),
        PermissionRequested requested => this with { Requests = Requests.SetItem((requested.Session, requested.Item), Asking.Permission) },
        FormRequested requested => this with { Requests = Requests.SetItem((requested.Session, requested.Item), requested.Form.Purpose == FormPurpose.Permission ? Asking.Permission : Asking.Answer) },
        PermissionResolved resolved => this with { Requests = Requests.Remove((resolved.Session, resolved.Item)) },
        FormAnswered answered => this with { Requests = Requests.Remove((answered.Session, answered.Item)) },
        RequestWithdrawn withdrawn => this with { Requests = Requests.Remove((withdrawn.Session, withdrawn.Item)) },
        TurnCompleted ended => this with { Requests = Requests.RemoveRange(Requests.Keys.Where(key => key.Session == ended.Session)) },
        _ => this,
    };

    public SubagentActivity Ended(SessionId session) =>
        this with
        {
            Subagents = [.. Subagents.Select(agent => agent.Session == session && agent.Outcome.IsNone ? agent with { Outcome = ItemOutcome.Abandoned } : agent)],
            Requests = Requests.RemoveRange(Requests.Keys.Where(key => key.Session == session)),
        };

    public SubagentActivity Ended() =>
        this with { Subagents = [.. Subagents.Select(agent => agent.Outcome.IsNone ? agent with { Outcome = ItemOutcome.Abandoned } : agent)], Requests = Requests.Clear() };

    public Asking AskingOf(InProcessSubagent agent) =>
        agent.Outcome.IsSome
            ? Asking.Nothing
            : Strongest(Requests.Where(request => request.Key.Session == agent.Session && OwnerOf(request.Key) == agent.Item).Select(request => request.Value));

    private ItemId OwnerOf((SessionId Session, ItemId Item) key) => Steps.TryGetValue(key, out var parent) ? parent : key.Item;

    private SubagentActivity Completed(ItemCompleted completed) =>
        this with
        {
            Subagents = [.. Subagents.Select(agent => agent.Session == completed.Session && agent.Item == completed.Item && agent.Outcome.IsNone ? agent with { Outcome = completed.Outcome } : agent)],
            Requests = Requests.Remove((completed.Session, completed.Item)),
        };

    private static Asking Strongest(IEnumerable<Asking> asked)
    {
        var all = asked.ToHashSet();

        return all.Contains(Asking.Permission) ? Asking.Permission : all.Contains(Asking.Answer) ? Asking.Answer : Asking.Nothing;
    }

    private static string Named(string title) => title.StartsWith(Titled, StringComparison.Ordinal) ? title[Titled.Length..] : title;
}
