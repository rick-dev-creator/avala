using System.Globalization;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Sdk;
using Avala.Workbench.Presenting;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Spending;

namespace Avala.Workbench.Usage;

internal sealed class ConnectionMeterViewModel(ConnectionSpend connection)
{
    public string Name { get; } = connection.Usage.Connection.Value;

    public string Provider { get; } = connection.Usage.Provider.Name;

    public string Cost { get; } = Amounts.Costs(connection.Usage.Usage.Costs);

    public string Tokens { get; } = Amounts.Tokens(connection.Usage.Usage.Tokens.Total());

    public string Unpriced { get; } = Amounts.Unpriced(connection.Usage.Usage.UnpricedReports);

    public string Caps { get; } = connection.Caps.Match(Amounts.Caps, () => "no caps known yet");

    public IReadOnlyList<LimitViewModel> Limits { get; } =
        [.. connection.Usage.Usage.Limits.Select(limit => new LimitViewModel(limit, connection.Caps.Bind(caps => caps.HoldAtLimit)))];
}

internal sealed class UsageWindowViewModel(UsageWindow window)
{
    public string Label { get; } = window.Span == UsageSpan.Today ? "Today" : "Last 7 days";

    public string Cost { get; } = Amounts.Costs(window.Period.Usage.Costs);

    public long Input { get; } = window.Period.Usage.Tokens.Input;

    public long Output { get; } = window.Period.Usage.Tokens.Output;

    public long CacheRead { get; } = window.Period.Usage.Tokens.CacheRead;

    public long CacheWrite { get; } = window.Period.Usage.Tokens.CacheWrite;

    public long Reasoning { get; } = window.Period.Usage.Tokens.Reasoning;

    public string Tokens { get; } = Amounts.Tokens(window.Period.Usage.Tokens.Total());

    public string Unpriced { get; } = Amounts.Unpriced(window.Period.Usage.UnpricedReports);

    public string Turns { get; } = Phrase(window.Period.Usage.Turns);

    private static string Phrase(TurnTally turns) =>
        string.Create(CultureInfo.InvariantCulture, $"{turns.Finished} turns finished, {turns.Interrupted} interrupted, {turns.Failed} failed");
}

internal sealed class JobMeterViewModel(JobCost cost)
{
    public JobId Job { get; } = cost.Job.Job;

    public string Title { get; } = FactPhrases.Title(cost.Job.Summary.Instruction);

    public JobStatus Status { get; } = cost.Job.Status;

    public string Cost { get; } = Amounts.Costs(cost.Spend.Spent);

    public string Tokens { get; } = Amounts.Tokens(cost.Spend.Tokens);

    public string Unpriced { get; } = cost.Spend.Usage.Match(usage => Amounts.Unpriced(usage.UnpricedReports), () => string.Empty);

    public string Caps { get; } = cost.Spend.Caps.Match(Amounts.Caps, () => "no caps known");

    public string Carve { get; } = cost.Spend.Carve.Match(carve => $"carved {Amounts.Costs(carve.Cost)}", () => string.Empty);

    public int Interventions { get; } = cost.Interventions.Count;
}

internal sealed class InterventionViewModel(JobIntervention intervention, string instruction)
{
    public JobId Job { get; } = intervention.Job;

    public string Title { get; } = FactPhrases.Title(instruction);

    public string At { get; } = Amounts.Time(intervention.At);

    public HoldReason Reason { get; } = intervention.Reason;

    public string Detail { get; } = intervention.Breach.Match(
        breach => string.Create(CultureInfo.InvariantCulture, $"{breach.Measure} {breach.Subject}: {breach.Measured:0.####} against {breach.Cap:0.####}"),
        () => intervention.Silence.Match(
            silence => $"silent for {Amounts.Seconds(silence.Silent)}s, window {Amounts.Seconds(silence.Window)}s",
            () => string.Empty));
}
