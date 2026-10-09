using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Inspection;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Inspector;

[INotifyPropertyChanged]
internal sealed partial class InspectorViewModel(JobId job, JobInspection inspection, IUiDispatcher ui)
{
    public JobId Job { get; } = job;

    public int Requested { get; private set; } = -1;

    public EvidenceSectionViewModel Evidence { get; } = new();

    public AuditSectionViewModel Audit { get; } = new();

    public UsageSectionViewModel Usage { get; } = new();

    public AutonomySectionViewModel Autonomy { get; } = new();

    public WorktreeSectionViewModel Worktree { get; } = new();

    public DelegationSectionViewModel Delegation { get; } = new();

    [ObservableProperty]
    public partial bool IsLoaded { get; private set; }

    public Func<CancellationToken, Task> Request(int revision)
    {
        Requested = revision;

        return cancellationToken => LoadAsync(revision, cancellationToken);
    }

    private async Task LoadAsync(int revision, CancellationToken cancellationToken)
    {
        var facts = await inspection.ReadAsync(Job, cancellationToken);
        await ui.InvokeAsync(
            () =>
            {
                if (!cancellationToken.IsCancellationRequested && revision == Requested)
                {
                    facts.Match(Show, () => false);
                }
            },
            cancellationToken);
    }

    private bool Show(InspectorFacts facts)
    {
        Evidence.Show(facts);
        Audit.Show(facts);
        Usage.Show(facts);
        Autonomy.Show(facts);
        Worktree.Show(facts);
        Delegation.Show(facts);
        IsLoaded = true;

        return true;
    }
}
