using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Inspection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Inspector;

internal interface IAuditSectionViewModel
{
    bool IsLoaded { get; }

    string Summary { get; }

    string Fact { get; }

    bool IsAttention { get; }

    IReadOnlyList<string> Decisions { get; }

    IReadOnlyList<string> Assumptions { get; }
}

[INotifyPropertyChanged]
internal sealed partial class AuditSectionViewModel : IAuditSectionViewModel, IRegionAware<JobId>, IActivatable, IPresentation, IDisposable
{
    private readonly InspectedJob inspected;

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
    public partial IReadOnlyList<string> Decisions { get; private set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<string> Assumptions { get; private set; } = [];

    public void OnRegionContextChanged(Option<JobId> context) => inspected.Focus(context);

    public void Activate() => inspected.Activate();

    public void Deactivate() => inspected.Deactivate();

    public void Dispose() => inspected.Dispose();

    private void Show(Option<InspectorFacts> facts)
    {
        IsLoaded = facts.IsSome;
        var audit = facts.Match(found => found.Audit, () => new AuditFacts([], [], []));
        var assumptions = audit.Forms.SelectMany(form => form.Assumptions).ToList();
        var denied = audit.Decisions.Count(decision => decision.Answer == PolicyAnswer.Deny) + audit.Answers.Count(answer => answer.Answer == PermissionAnswer.Deny);
        Fact = facts.IsNone ? string.Empty : InspectorPhrases.Decided(audit.Decisions.Count(decision => decision.Answer == PolicyAnswer.Allow), audit.Answers.Count, denied, assumptions.Count);
        IsAttention = denied > 0;
        Summary = facts.IsNone
            ? string.Empty
            : InspectorPhrases.Audit(
                audit.Decisions.Count(decision => decision.Answer == PolicyAnswer.Allow),
                audit.Answers.Count,
                audit.Decisions.Count(decision => decision.Answer == PolicyAnswer.Deny) + audit.Answers.Count(answer => answer.Answer == PermissionAnswer.Deny),
                assumptions.Count);
        Decisions = [.. audit.Decisions.Select(InspectorPhrases.Decision), .. audit.Answers.Select(InspectorPhrases.Answer)];
        Assumptions = [.. assumptions.Select(InspectorPhrases.Assumption)];
    }
}
