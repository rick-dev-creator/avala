using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Connections;
using Avala.Autopilot.Contracts;
using Avala.Budgets.Contracts;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Observability.Contracts;
using Avala.Permissions.Contracts;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Regions;
using Avala.Supervision.Contracts;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Workbench.Inspector;
using Avala.Workbench.Navigation;
using Avala.Workbench.Resources;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Tests.Updates;
using Avala.Workbench.UI;
using Avala.Workbench.Updates;
using Avala.Workspaces.Contracts;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workbench.Tests.Navigation;

public sealed class WorkbenchPluginTests : IDisposable
{
    private readonly TestUiDispatcher ui = new();

    [Fact]
    public async Task TheRegistrationResolvesEveryServiceItRegistersAsync()
    {
        await using var data = new TemporaryFolder();
        await using var composition = Compose(data);

        Assert.Equal(composition.Registered, composition.ResolveEveryRegistration());
    }

    [Fact]
    public async Task TheJobListFillsTheSidebarTheToolbarAndTheSectionsFillTheInspectorInOrderAsync()
    {
        await using var data = new TemporaryFolder();
        await using var composition = Compose(data);

        var regions = composition.All<RegionContribution>().OrderBy(contribution => contribution.Order).ToLookup(contribution => contribution.Region, contribution => contribution.ViewModel.GetType());

        Assert.Equal([typeof(SidebarViewModel)], regions[ShellRegions.Sidebar]);
        Assert.Equal([typeof(ToolbarViewModel)], regions[ShellRegions.Toolbar]);
        Assert.Equal([typeof(ResourceIndicatorViewModel), typeof(UpdateNoticeViewModel)], regions[ShellRegions.SidebarFooter]);
        Assert.Equal(
            [typeof(EvidenceSectionViewModel), typeof(AuditSectionViewModel), typeof(UsageSectionViewModel), typeof(AutonomySectionViewModel), typeof(WorktreeSectionViewModel), typeof(PullRequestSectionViewModel), typeof(DelegationSectionViewModel)],
            regions[ShellRegions.Inspector]);
        Assert.Equal(["Jobs", "New job", "Overview", "Usage", "Resources", "Settings"], composition.All<IPage>().Select(page => page.Title));
        Assert.IsType<WorkbenchViewModel>(composition.All<IPage>()[0]);
        Assert.Equal(
            [("Overview", "IconOverview"), ("Usage", "IconUsage"), ("Settings", "IconSettings")],
            composition.All<IPage>().Where(page => page.Placement == PagePlacement.Navigation).Select(page => (page.Title, page.Icon)));
    }

    public void Dispose() => ui.Dispose();

    private PluginComposition Compose(TemporaryFolder data)
    {
        var audit = new FakeAudit();
        var sources = new FakeRecordSources();
        var rules = new FakeRules();

        return PluginComposition.Of(new WorkbenchPlugin(), new AvalaPaths(data.Path), services => services
            .AddSingleton<IUiDispatcher>(ui)
            .AddSingleton<IRegions>(new TestRegions())
            .AddSingleton<IMessenger>(new StrongReferenceMessenger())
            .AddSingleton<IFileOpener>(new FakeOpener())
            .AddSingleton<ILinkOpener>(new FakeLinks())
            .AddSingleton(new AvalaBuild("1.0.0", Option<string>.None))
            .AddSingleton<Sdk.Updates.IUpdates>(new FakeUpdates())
            .AddSingleton<Sdk.Appearance.IAppearance>(new FakeAppearance())
            .AddSingleton<IJobCatalog>(new FakeCatalog())
            .AddSingleton<IJobs>(new FakeJobs())
            .AddSingleton<IAgents>(new FakeAgents())
            .AddSingleton<IPermissionAnswers>(new FakePermissionAnswers())
            .AddSingleton<IRunEvidence>(new FakeRunEvidence())
            .AddSingleton<IWorkspaceChanges>(new FakeChanges())
            .AddSingleton<IWorkspaces>(new FakeWorkspaces())
            .AddSingleton<IWorkingFiles>(new FakeWorkingFiles())
            .AddSingleton<IUsage>(new FakeUsage())
            .AddSingleton<IUsageSessions>(new FakeUsage())
            .AddSingleton<IUsageHistory>(new FakeUsageHistory())
            .AddSingleton<IVerifications>(audit)
            .AddSingleton<Transcripts.Contracts.ITranscripts>(new FakeTranscripts())
            .AddSingleton<Handoffs.Contracts.IHandoffs>(new FakeHandoffs())
            .AddSingleton<Forges.Contracts.IPullRequests>(new FakePullRequests())
            .AddSingleton<Forges.Contracts.IForgeCatalog>(new FakeForgeCatalog())
            .AddSingleton<IOpenDeliveries>(new FakeJobs())
            .AddSingleton<IPermissionAudit>(audit)
            .AddSingleton<IBudgets>(audit)
            .AddSingleton<IResources>(sources)
            .AddSingleton<IDelegations>(sources)
            .AddSingleton<IConnections>(new FakeConnections("claude-work"))
            .AddSingleton<IConnectionPreview>(new FakePreview())
            .AddSingleton<ISupervision>(new FakeSupervision())
            .AddSingleton<IRepositoryPolicies>(rules)
            .AddSingleton<IRepositoryBudgets>(rules)
            .AddSingleton<IRepositoryChecks>(rules)
            .AddSingleton<IBaseFiles>(new CommittedFiles())
            .AddSingleton<IOrphans>(new FakeOrphans())
            .AddSingleton<IWorktreeHousekeeping>(new FakeHousekeeping()));
    }
}
