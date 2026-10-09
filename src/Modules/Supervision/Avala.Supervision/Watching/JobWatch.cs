using System.Collections.Immutable;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Supervision.Watching;

internal sealed record JobWatch(JobId Job)
{
    public static TimeSpan DefaultSilence { get; } = TimeSpan.FromMinutes(15);

    public static TimeSpan LongestSilence { get; } = TimeSpan.FromDays(1);

    public Option<SessionId> Session { get; private init; }

    public bool Running { get; private init; }

    public Option<ItemId> AwaitingHuman { get; private init; }

    public ImmutableHashSet<ItemId> PendingCalls { get; private init; } = [];

    public DateTimeOffset LastActivity { get; private init; }

    public bool IsArmed => Running && AwaitingHuman.IsNone && PendingCalls.IsEmpty;

    public JobWatch Progressed(JobStatus status, DateTimeOffset at) =>
        status == JobStatus.Running
            ? this with { Running = true, AwaitingHuman = Option<ItemId>.None, PendingCalls = [], LastActivity = at }
            : this with { Running = false };

    public JobWatch Joined(SessionId session) => this with { Session = session };

    public JobWatch Saw(IAgentEvent agentEvent, DateTimeOffset at) =>
        Session != Option<SessionId>.Some(agentEvent.Session) ? this
        : agentEvent switch
        {
            PermissionRequested requested => this with { LastActivity = at, AwaitingHuman = requested.Item },
            FormRequested requested => this with { LastActivity = at, AwaitingHuman = requested.Item },
            ToolCalled called => this with { LastActivity = at, PendingCalls = PendingCalls.Add(called.Item) },
            ToolReturned returned => this with { LastActivity = at, PendingCalls = PendingCalls.Remove(returned.Item) },
            ItemCompleted completed => this with { LastActivity = at, PendingCalls = PendingCalls.Remove(completed.Item) },
            PermissionResolved or FormAnswered => this with { LastActivity = at, AwaitingHuman = Option<ItemId>.None },
            TurnCompleted => this with { LastActivity = at, AwaitingHuman = Option<ItemId>.None, PendingCalls = [] },
            _ => this with { LastActivity = at },
        };

    public DateTimeOffset Due(TimeSpan window) => LastActivity + window;

    public Option<TimeSpan> SilenceAt(DateTimeOffset now, TimeSpan window) =>
        IsArmed && now - LastActivity >= window ? now - LastActivity : Option<TimeSpan>.None;
}
