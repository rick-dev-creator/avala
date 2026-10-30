using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Inspection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Inspector;

internal interface IAuditSectionViewModel
{
    bool IsLoaded { get; }

    string Summary { get; }

    string Fact { get; }

    bool IsAttention { get; }

    IReadOnlyList<DecisionRow> Decisions { get; }

    string More { get; }

    IRelayCommand ShowAllCommand { get; }

    IReadOnlyList<AssumptionRow> Assumptions { get; }
}

internal enum DecisionVerdict
{
    Waiting,
    Denied,
    Unanswered,
    Allowed,
}

internal sealed record DecisionRow(DecisionVerdict Verdict, string Action, string Target, string By)
{
    public int Times { get; init; } = 1;

    public string Count => Times > 1 ? string.Create(CultureInfo.InvariantCulture, $"×{Times}") : string.Empty;

    public string Caption => $"{Action} · {By}";

    public bool IsWaiting => Verdict == DecisionVerdict.Waiting;

    public bool IsDenied => Verdict == DecisionVerdict.Denied;

    public bool IsUnanswered => Verdict == DecisionVerdict.Unanswered;

    public bool IsAllowed => Verdict == DecisionVerdict.Allowed;
}

internal sealed record AssumptionRow(string Question, string Chosen);

[INotifyPropertyChanged]
internal sealed partial class AuditSectionViewModel : IAuditSectionViewModel, IRegionAware<JobId>, IActivatable, IPresentation, IDisposable
{
    private const int Few = 5;

    private readonly InspectedJob inspected;
    private IReadOnlyList<DecisionRow> all = [];
    private Option<JobId> shown;
    private bool expanded;

    public AuditSectionViewModel(InspectedJob inspected)
    {
        this.inspected = inspected;
        inspected.Showing(Show);
    }

    public event EventHandler<Presented>? Presented
    {
        add => inspected.Presented += value;
        remove => inspected.Presented -= value;
    }

    public long Revision => inspected.Revision;

    public Task Loading => inspected.Loading;

    [ObservableProperty]
    public partial bool IsLoaded { get; private set; }

    [ObservableProperty]
    public partial string Fact { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsAttention { get; private set; }

    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<DecisionRow> Decisions { get; private set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowAllCommand))]
    public partial string More { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<AssumptionRow> Assumptions { get; private set; } = [];

    private bool HasMore => More.Length > 0;

    public void OnRegionContextChanged(Option<JobId> context) => inspected.Focus(context);

    public void Activate() => inspected.Activate();

    public void Deactivate() => inspected.Deactivate();

    public void Dispose() => inspected.Dispose();

    [RelayCommand(CanExecute = nameof(HasMore))]
    private void ShowAll()
    {
        expanded = true;
        ShowRows();
    }

    private void ShowRows()
    {
        var trimmed = !expanded && all.Count > Few + 1;
        Decisions = trimmed ? [.. all.Take(Few)] : all;
        More = trimmed ? string.Create(CultureInfo.InvariantCulture, $"Show {all.Count - Few} more") : string.Empty;
    }

    private static Option<SessionId> Latest(InspectorFacts facts) =>
        facts.Record.History.Sessions.Count > 0 ? facts.Record.History.Sessions[^1].Session : Option<SessionId>.None;

    private static IReadOnlyList<DecisionRow> Rows(AuditFacts audit, Option<SessionId> latest) =>
        InspectorPhrases.Grouped(
        [
            .. audit.Answers.Select(InspectorPhrases.Answer),
            .. audit.Given.Select(InspectorPhrases.Given),
            .. audit.Decisions.SelectMany(decision => InspectorPhrases.Decision(decision, audit.Answers, latest).Match<DecisionRow[]>(row => [row], () => [])),
        ]);

    private void Show(Option<InspectorFacts> facts)
    {
        IsLoaded = facts.IsSome;
        var audit = facts.Match(found => found.Audit, () => new AuditFacts([], [], []));
        var assumptions = audit.Forms.SelectMany(form => form.Assumptions).ToList();
        Tell(facts.IsSome, audit, assumptions.Count);
        var job = facts.Map(found => found.Record.History.Summary.Job);
        expanded = expanded && job == shown;
        shown = job;
        all = Rows(audit, facts.Bind(Latest));
        ShowRows();
        Assumptions = [.. assumptions.Select(InspectorPhrases.Assumption)];
    }

    private void Tell(bool loaded, AuditFacts audit, int assumptions)
    {
        var allowed = audit.Decisions.Count(decision => decision.Answer == PolicyAnswer.Allow);
        var denied = audit.Decisions.Count(decision => decision.Answer == PolicyAnswer.Deny) + audit.Answers.Count(answer => answer.Answer == PermissionAnswer.Deny);
        var byYou = audit.Answers.Count(answer => answer.Parent.IsNone);
        Fact = loaded ? InspectorPhrases.Decided(allowed, byYou, denied, assumptions) : string.Empty;
        IsAttention = denied > 0;
        var summary = loaded ? InspectorPhrases.Audit(allowed, byYou, denied, assumptions) + InspectorPhrases.ByParent(audit.Answers.Count - byYou, audit.Given.Count) : string.Empty;
        Summary = summary == Fact ? string.Empty : summary;
    }
}
