using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Triggers.Contracts;

namespace Avala.Triggers.Declarations;

internal sealed record TriggerSchedule(TriggerKind Kind)
{
    public int EveryMinutes { get; init; }

    public TimeOnly At { get; init; }

    public IReadOnlyList<DayOfWeek> Days { get; init; } = [];

    public static TriggerSchedule Every(int minutes) => new(TriggerKind.Interval) { EveryMinutes = minutes };

    public static TriggerSchedule Daily(TimeOnly at, IReadOnlyList<DayOfWeek> days) => new(TriggerKind.FixedTime) { At = at, Days = days };

    public bool Allows(DayOfWeek day) => Days.Count == 0 || Days.Contains(day);
}

internal sealed record WebhookRule(string SecretVariable, int RatePerHour)
{
    public const int DefaultRate = 60;
}

internal sealed record TriggerDeclaration(TriggerId Id, string Repository, InstructionTemplate Instruction)
{
    public const int DefaultAttempts = 3;

    public Option<TriggerSchedule> Schedule { get; init; }

    public Option<WebhookRule> Webhook { get; init; }

    public bool Enabled { get; init; } = true;

    public CatchUp CatchUp { get; init; }

    public TriggerTarget Target { get; init; }

    public Option<ConnectionName> Connection { get; init; }

    public Autonomy Autonomy { get; init; } = Autonomy.Supervised;

    public int Attempts { get; init; } = DefaultAttempts;

    public int Concurrency { get; init; } = 1;

    public TriggerKind Kind => Schedule.Match(schedule => schedule.Kind, () => TriggerKind.Webhook);

    public Option<string> Hook => Webhook.Map(_ => $"/hooks/{Id.Name}");
}
