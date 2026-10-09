using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Workbench.Fleet;
using Avala.Workbench.Presenting;
using Avala.Workbench.Sidebar;

namespace Avala.Workbench.Overview;

internal interface IDelegationNodeViewModel
{
    JobId Job { get; }

    string Title { get; }

    int Depth { get; }

    JobStatus Status { get; }

    string Connection { get; }

    string Harness { get; }

    string Activity { get; }

    string Spent { get; }

    string Carve { get; }
}

internal interface IDelegationRefusalViewModel
{
    string Instruction { get; }

    string Reason { get; }
}

internal sealed class DelegationNodeViewModel(DelegationNode node) : IDelegationNodeViewModel
{
    public JobId Job { get; } = node.Job.Job;

    public string Title { get; } = FactPhrases.Title(node.Job.Instruction);

    public int Depth { get; } = node.Depth;

    public JobStatus Status { get; } = node.Job.Status;

    public string Connection { get; } = node.Job.Connection
        .Match(name => name.Value, () => node.Delegation.Bind(record => record.Connection).Match(name => name.Value, () => "not started"));

    public string Harness { get; } = node.Spend.Session.Match(seen => seen.Provider.Name, () => string.Empty);

    public string Activity { get; } = node.Report.Match(
        report => OverviewPhrases.Outcome(report.Outcome),
        () => node.Activity.Match(FactPhrases.Of, () => node.Job.Status.ToString()));

    public string Spent { get; } = Amounts.Costs(node.Spend.Spent);

    public string Carve { get; } = node.Spend.Carve.Match(carve => Amounts.Costs(carve.Cost), () => "no carve");
}

internal sealed class DelegationRefusalViewModel(DelegationRecord record) : IDelegationRefusalViewModel
{
    public string Instruction { get; } = FactPhrases.Title(record.Instruction);

    public string Reason { get; } = record.Refusal.Match(OverviewPhrases.Refusal, () => string.Empty);
}

internal static class OverviewPhrases
{
    public static string Outcome(ChildOutcome outcome) => outcome switch
    {
        ChildOutcome.Integrated => "integrated into its parent",
        ChildOutcome.Conflict => "conflicts with its parent",
        ChildOutcome.NotIntegrated => "not integrated",
        ChildOutcome.Held => "held",
        ChildOutcome.RetriesExhausted => "retries ran out",
        ChildOutcome.Failed => "failed",
        _ => "discarded",
    };

    public static string Refusal(DelegationError error) => error switch
    {
        DelegationError.NotDeclared => "the repository declares no delegation to that connection",
        DelegationError.DepthExceeded => "too deep",
        DelegationError.TooManyChildren => "too many children",
        DelegationError.AutonomyLoosened => "it asked for more autonomy than its parent",
        DelegationError.NotSubmitted => "the child job was refused",
        DelegationError.MalformedInput => "the call was malformed",
        DelegationError.NoJob => "the caller is not a job",
        _ => "the delegation rules cannot be read",
    };
}
