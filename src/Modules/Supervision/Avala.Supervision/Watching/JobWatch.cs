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

    public static TimeSpan BackgroundPatience { get; } = TimeSpan.FromHours(2);

    public Option<SessionId> Session { get; private init; }

    public bool Running { get; private init; }

    public bool BetweenTurns { get; private init; }

    public Option<ItemId> AwaitingHuman { get; private init; }

    public ImmutableHashSet<ItemId> PendingCalls { get; private init; } = [];

    public ImmutableHashSet<ItemId> Background { get; private init; } = [];

    public DateTimeOffset LastActivity { get; private init; }

    public bool IsArmed => Running && !BetweenTurns && AwaitingHuman.IsNone && PendingCalls.IsEmpty;

    public JobWatch Progressed(JobStatus status, DateTimeOffset at) =>
        status == JobStatus.Running
            ? this with { Running = true, BetweenTurns = false, LastActivity = at }
            : this with { Running = false };

    public JobWatch Joined(SessionId session) =>
        Session == Option<SessionId>.Some(session) ? this : this with { Session = session, AwaitingHuman = Option<ItemId>.None, PendingCalls = [], Background = [] };

    public JobWatch Saw(IAgentEvent agentEvent, DateTimeOffset at) =>
        Session != Option<SessionId>.Some(agentEvent.Session) ? this
        : agentEvent switch
        {
            PermissionRequested requested => this with { LastActivity = at, AwaitingHuman = requested.Item },
            FormRequested requested => this with { LastActivity = at, AwaitingHuman = requested.Item },
            ToolCalled called => this with { LastActivity = at, PendingCalls = PendingCalls.Add(called.Item) },
            ItemBackgrounded backgrounded => this with { LastActivity = at, Background = Background.Add(backgrounded.Item) },
            ToolReturned returned => this with { LastActivity = at, PendingCalls = PendingCalls.Remove(returned.Item) },
            ItemCompleted completed => this with { LastActivity = at, PendingCalls = PendingCalls.Remove(completed.Item), Background = Background.Remove(completed.Item) },
            PermissionResolved or FormAnswered or RequestWithdrawn => this with { LastActivity = at, AwaitingHuman = Option<ItemId>.None },
            TurnStarted => this with { LastActivity = at, BetweenTurns = false },
            TurnCompleted => this with { LastActivity = at, BetweenTurns = true, AwaitingHuman = Option<ItemId>.None, PendingCalls = [], Background = [] },
            _ => this with { LastActivity = at },
        };

    public TimeSpan Window(TimeSpan silence) => Background.IsEmpty || silence >= BackgroundPatience ? silence : BackgroundPatience;

    public DateTimeOffset Due(TimeSpan silence) => LastActivity + Window(silence);

    public Option<TimeSpan> SilenceAt(DateTimeOffset now, TimeSpan silence) =>
        IsArmed && now - LastActivity >= Window(silence) ? now - LastActivity : Option<TimeSpan>.None;
}
