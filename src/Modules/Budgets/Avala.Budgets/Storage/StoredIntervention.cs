using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Budgets.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Budgets.Storage;

internal sealed class StoredIntervention
{
    public int Key { get; init; }

    public Guid Job { get; init; }

    public Guid Session { get; init; }

    public string Reason { get; init; } = string.Empty;

    public string Halt { get; init; } = string.Empty;

    public int Attempt { get; init; }

    public string Measure { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public decimal Measured { get; init; }

    public decimal Cap { get; init; }

    public string Error { get; init; } = string.Empty;

    public string Connection { get; init; } = string.Empty;

    public long At { get; init; }

    public static StoredIntervention Of(BudgetIntervention intervention) => new()
    {
        Job = intervention.Hold.Job.Value,
        Session = intervention.Hold.Session.Value,
        Reason = intervention.Hold.Reason.ToString(),
        Halt = intervention.Hold.Halt.ToString(),
        Attempt = intervention.Hold.Attempt,
        Measure = intervention.Breach.Measure.ToString(),
        Subject = intervention.Breach.Subject,
        Measured = intervention.Breach.Measured,
        Cap = intervention.Breach.Cap,
        Error = intervention.Breach.Error.Match(error => error.ToString(), () => string.Empty),
        Connection = intervention.Breach.Connection.Match(connection => connection.Value, () => string.Empty),
        At = intervention.At.UtcTicks,
    };

    public BudgetIntervention Intervention() => new(
        new JobHold(new JobId(Job), new SessionId(Session), Enum.Parse<HoldReason>(Reason), Enum.Parse<SessionHalt>(Halt)) { Attempt = Attempt },
        new BudgetBreach(
            Enum.Parse<BudgetMeasure>(Measure),
            Subject,
            Measured,
            Cap,
            Error.Length == 0 ? Option<BudgetError>.None : Enum.Parse<BudgetError>(Error))
        {
            Connection = Connection.Length == 0 ? Option<ConnectionName>.None : new ConnectionName(Connection),
        },
        new DateTimeOffset(At, TimeSpan.Zero));
}
