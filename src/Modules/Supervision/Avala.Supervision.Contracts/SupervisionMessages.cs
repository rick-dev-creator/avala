using Avala.Agents.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Supervision.Contracts;

public enum SupervisionError
{
    Unreadable,
    TooLarge,
    Malformed,
    UnknownField,
    InvalidSilence,
}

public enum SettingsFileStatus
{
    Absent,
    Applied,
    Rejected,
}

public sealed record SupervisionSettings(TimeSpan Silence, SettingsFileStatus File, Option<SupervisionError> Error);

public readonly record struct SilenceMeasure(TimeSpan Silent, TimeSpan Window);

public sealed record SupervisionIntervention(
    JobHold Hold,
    Option<SilenceMeasure> Silence,
    Option<SessionEnding> Ending,
    DateTimeOffset At);

public sealed record SilenceNoticed(JobId Job) : IIntegrationEvent;

public sealed record SupervisorIntervened(SupervisionIntervention Intervention) : IIntegrationEvent;
