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

    public DateTimeOffset LastActivity { get; private init; }

    public bool IsArmed => Running && AwaitingHuman.IsNone;

    public JobWatch Progressed(JobStatus status, DateTimeOffset at) =>
        status == JobStatus.Running
            ? this with { Running = true, AwaitingHuman = Option<ItemId>.None, LastActivity = at }
            : this with { Running = false };

    public JobWatch Joined(SessionId session) => this with { Session = session };

    public JobWatch Saw(IAgentEvent agentEvent, DateTimeOffset at) =>
        Session != Option<SessionId>.Some(agentEvent.Session) ? this
        : agentEvent switch
        {
            PermissionRequested requested => this with { LastActivity = at, AwaitingHuman = requested.Item },
            PermissionResolved or TurnCompleted => this with { LastActivity = at, AwaitingHuman = Option<ItemId>.None },
            _ => this with { LastActivity = at },
        };

    public DateTimeOffset Due(TimeSpan window) => LastActivity + window;

    public Option<TimeSpan> SilenceAt(DateTimeOffset now, TimeSpan window) =>
        IsArmed && now - LastActivity >= window ? now - LastActivity : Option<TimeSpan>.None;
}
