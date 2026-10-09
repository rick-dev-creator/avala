using Avala.Agents.Contracts.Events;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Inspection;
using Avala.Workbench.Review;
using Avala.Workbench.Reviewing;
using Avala.Workbench.Sidebar;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Inspector;

[INotifyPropertyChanged]
internal sealed partial class EvidenceSectionViewModel
{
    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<string> Attempts { get; private set; } = [];

    public void Show(InspectorFacts facts)
    {
        Summary = ReviewPhrases.Verdict(ReviewExceptions.VerdictOf(facts.Audit.Verifications, facts.Record.History.Attempts.Count));
        Attempts = [.. facts.Audit.Verifications.Select(InspectorPhrases.Attempt)];
    }
}

[INotifyPropertyChanged]
internal sealed partial class AuditSectionViewModel
{
    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<string> Decisions { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<string> Assumptions { get; private set; } = [];

    public void Show(InspectorFacts facts)
    {
        var audit = facts.Audit;
        var assumptions = audit.Forms.SelectMany(form => form.Assumptions).ToList();
        Summary = InspectorPhrases.Audit(
            audit.Decisions.Count(decision => decision.Answer == PolicyAnswer.Allow),
            audit.Answers.Count,
            audit.Decisions.Count(decision => decision.Answer == PolicyAnswer.Deny) + audit.Answers.Count(answer => answer.Answer == PermissionAnswer.Deny),
            assumptions.Count);
        Decisions = [.. audit.Decisions.Select(InspectorPhrases.Decision), .. audit.Answers.Select(InspectorPhrases.Answer)];
        Assumptions = [.. assumptions.Select(InspectorPhrases.Assumption)];
    }
}

[INotifyPropertyChanged]
internal sealed partial class UsageSectionViewModel
{
    [ObservableProperty]
    public partial string Spent { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<string> Caps { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<string> Interventions { get; private set; } = [];

    [ObservableProperty]
    public partial string Carve { get; private set; } = string.Empty;

    public void Show(InspectorFacts facts)
    {
        var audit = facts.Audit;
        Spent = audit.Usage.Match(usage => string.Join(" · ", Amounts.Spent(usage)), () => "No usage reported");
        Caps = audit.Budget.Match(budget => InspectorPhrases.Caps(budget.Caps), () => []);
        Interventions = [.. audit.Interventions.Select(InspectorPhrases.Intervention)];
        Carve = audit.Carve.Match(InspectorPhrases.Carve, () => string.Empty);
    }
}

[INotifyPropertyChanged]
internal sealed partial class AutonomySectionViewModel
{
    [ObservableProperty]
    public partial string Autonomy { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Connection { get; private set; } = string.Empty;

    public void Show(InspectorFacts facts)
    {
        var summary = facts.Record.History.Summary;
        Autonomy = facts.Audit.Autonomy.Match(
            InspectorPhrases.Autonomy,
            () => summary.Autonomy.Match(requested => $"{requested}, as asked", () => "Not started yet"));
        Connection = summary.Connection.Match(connection => connection.Value, () => "The default connection");
    }
}

[INotifyPropertyChanged]
internal sealed partial class WorktreeSectionViewModel
{
    [ObservableProperty]
    public partial string Branch { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Base { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Path { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Ports { get; private set; } = string.Empty;

    public void Show(InspectorFacts facts)
    {
        var workspace = facts.Record.Workspace;
        Branch = workspace.Match(found => found.Branch, () => "No worktree");
        Base = workspace.Match(
            found => found.BaseBranch.Match(branch => $"{branch} at {Short(found.BaseCommit)}", () => Short(found.BaseCommit)),
            () => string.Empty);
        Path = workspace.Match(found => found.Path, () => string.Empty);
        Ports = facts.Record.Ports.Match(
            lease => lease.First == lease.Last ? $"Port {lease.First}" : $"Ports {lease.First}–{lease.Last}",
            () => string.Empty);
    }

    private static string Short(string commit) => commit[..Math.Min(7, commit.Length)];
}

[INotifyPropertyChanged]
internal sealed partial class DelegationSectionViewModel
{
    [ObservableProperty]
    public partial string Parent { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<string> Children { get; private set; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; } = true;

    public void Show(InspectorFacts facts)
    {
        var record = facts.Record;
        Parent = record.Parent.Match(parent => $"Delegated by {FactPhrases.Title(parent.Instruction)}", () => string.Empty);
        Children =
        [
            .. record.Children.Select(child => InspectorPhrases.Child(child, record.Delegated.FirstOrDefault(delegation => delegation.Child == child.Job).ToOption())),
            .. record.Delegated.Where(delegation => delegation.Child.IsNone).Select(InspectorPhrases.Refused),
        ];
        IsEmpty = Parent.Length == 0 && Children.Count == 0;
    }
}
