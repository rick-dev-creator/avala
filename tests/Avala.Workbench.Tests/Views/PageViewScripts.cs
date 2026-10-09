using Avala.Testing.UI;
using Avala.Workbench.NewJob;
using Avala.Workbench.Overview;
using Avala.Workbench.Resources;
using Avala.Workbench.Settings;
using Avala.Workbench.Usage;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Avala.Workbench.Tests.Views;

public sealed class OverviewViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheOverviewOpensOnTheConnectionsAndSwitchesToTheTreeAsync() =>
        ui.RunAsync(() =>
        {
            var overview = new OverviewViewModel(new DesignConnectionsViewModel(), new DesignDelegationViewModel());
            var view = Screen.Show(overview);
            var opened = (view.Shows("Connections"), view.Shows("Delegation"));

            view.Click("ShowDelegation");

            Assert.Equal((true, false), opened);
            Assert.Equal((false, true), (view.Shows("Connections"), view.Shows("Delegation")));
            Assert.True(view.HasClass("ShowDelegation", "selected"));
        }, TestContext.Current.CancellationToken);
}

public sealed class ConnectionsViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task EachConnectionIsACardAndNoFileNoteShowsWhenTheFileIsFineAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignConnectionsViewModel());

            Assert.Equal((2, false, false), (view.Find<ItemsControl>("Connections").ItemCount, view.Shows("FileNote"), view.Shows("NoConnections")));
        }, TestContext.Current.CancellationToken);
}

public sealed class ConnectionCardViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ACardShowsItsNameDefaultAccountCostLimitAndAgentsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignConnectionCardViewModel());

            Assert.Equal(("claude-work", true, "rick@acme.dev", "3.2140 USD"), (view.TextOf("ConnectionName"), view.Shows("Default"), view.TextOf("Account"), view.TextOf("Cost")));
            Assert.Equal((1, 2), (view.Find<ItemsControl>("Limits").ItemCount, view.Find<ItemsControl>("Agents").ItemCount));
        }, TestContext.Current.CancellationToken);
}

public sealed class AgentViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AnAgentShowsItsJobAndFactAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignAgentViewModel());

            Assert.Equal(("Fix JPY rounding in invoice totals", "2 of 4"), (view.TextOf("Title"), view.TextOf("Fact")));
        }, TestContext.Current.CancellationToken);
}

public sealed class LimitViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ALimitShowsHowMuchIsUsedAndWhereJobsAreHeldAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignLimitViewModel());

            Assert.Equal(("5h", "88% used", "jobs are held at 90%"), (view.TextOf("Window"), view.TextOf("UsedText"), view.TextOf("HoldAt")));
            Assert.Equal(0.88, view.Find<ProgressBar>("Used").Value);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ALimitThatReachedItsHoldTurnsAmberAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignLimitViewModel("5h", 0.93, "93% used", "resets 16:20", "jobs are held at 90%", true));

            Assert.Equal(Color.Parse("#E5A13A"), Assert.IsAssignableFrom<ISolidColorBrush>(view.Find<ProgressBar>("Used").Foreground).Color);
            Assert.True(view.HasClass("UsedText", "attention"));
        }, TestContext.Current.CancellationToken);
}

public sealed class DelegationViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheTreeShowsItsOrchestratorsChildrenAndRefusalsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDelegationViewModel());

            Assert.Equal((1, 3, 1), (view.Find<ItemsControl>("Orchestrators").ItemCount, view.Find<ItemsControl>("Children").ItemCount, view.Find<ItemsControl>("Refused").ItemCount));
            Assert.False(view.Shows("NoOrchestrators"));
        }, TestContext.Current.CancellationToken);
}

public sealed class OrchestratorViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AnOrchestratorShowsItsTitleAndStatusAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignOrchestratorViewModel());

            Assert.Equal(("Split CheckoutPage into steps", "Running"), (view.TextOf("Title"), view.TextOf("Status")));
        }, TestContext.Current.CancellationToken);
}

public sealed class DelegationNodeViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AChildShowsItsDepthConnectionActivityAndSpendAgainstItsCarveAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDelegationNodeViewModel());

            Assert.Equal(("L1", "claude-work", "integrated into its parent", "of 1 USD"), (view.TextOf("Depth"), view.TextOf("Connection"), view.TextOf("Activity"), view.TextOf("Carve")));
        }, TestContext.Current.CancellationToken);
}

public sealed class DelegationRefusalViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ARefusalShowsWhatWasAskedAndWhyInTheFailureColorAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignDelegationRefusalViewModel());

            Assert.Equal("Rewrite the payment step in Svelte", view.TextOf("Instruction"));
            Assert.True(view.HasClass("Reason", "failure"));
        }, TestContext.Current.CancellationToken);
}

public sealed class UsageViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ThePageShowsConnectionsWindowsJobsAndInterventionsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageViewModel());

            Assert.Equal((2, 1, 2, 1), (view.Find<ItemsControl>("Connections").ItemCount, view.Find<ItemsControl>("Windows").ItemCount, view.Find<ItemsControl>("Jobs").ItemCount, view.Find<ItemsControl>("InterventionList").ItemCount));
            Assert.Equal((false, false, true), (view.Shows("NoConnections"), view.Shows("NoJobs"), view.Shows("InterventionsGroup")));
        }, TestContext.Current.CancellationToken);
}

public sealed class ConnectionMeterViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AMeterShowsTheConnectionsSpendCapsAndLimitAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignConnectionMeterViewModel());

            Assert.Equal(("claude-work", "3.2140 USD", "412,880 tokens"), (view.TextOf("ConnectionName"), view.TextOf("Cost"), view.TextOf("Tokens")));
            Assert.False(view.Shows("Unpriced"));
        }, TestContext.Current.CancellationToken);
}

public sealed class UsageWindowViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AWindowShowsItsCostTokensByTypeAndTurnsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignUsageWindowViewModel());

            Assert.Equal(("Today", "4.0540 USD"), (view.TextOf("Label"), view.TextOf("Cost")));
            Assert.Superset(new HashSet<string>(["input 310,400", "cache read 1,204,300", "37 turns finished, 2 interrupted, 0 failed"]), view.VisibleTexts.ToHashSet());
        }, TestContext.Current.CancellationToken);
}

public sealed class JobMeterViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AJobHeldOnceSaysSoInAmberAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignJobMeterViewModel(Presenting.SampleJobs.SyncQueue, "Extract sync queue into a module", Jobs.Contracts.JobStatus.NeedsHelp, "1.92 USD", "240,410 tokens", 1));

            Assert.Equal("held 1 times", view.TextOf("Interventions"));
            Assert.True(view.HasClass("Interventions", "attention"));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AJobNeverHeldShowsNoInterventionsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignJobMeterViewModel());

            Assert.Equal(("Rate-limit POST /login", false, false), (view.TextOf("Title"), view.Shows("Interventions"), view.Shows("Carve")));
        }, TestContext.Current.CancellationToken);
}

public sealed class InterventionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AnInterventionShowsWhenWhatAndWhyAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignInterventionViewModel());

            Assert.Equal(("2026-10-09 14:52", "Stalled", "silent for 600s, window 600s"), (view.TextOf("At"), view.TextOf("Reason"), view.TextOf("Detail")));
        }, TestContext.Current.CancellationToken);
}

public sealed class SettingsViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ThePageShowsTheRepositoryAndTheMachineAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignSettingsViewModel());

            Assert.Contains(view.All<UserControl>(), control => control.GetType().Name == "RepositorySettingsView");
            Assert.Contains(view.All<UserControl>(), control => control.GetType().Name == "MachineSettingsView");
        }, TestContext.Current.CancellationToken);
}

public sealed class RepositorySettingsViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheRulesOfTheRepositoryReadAreShownWithEachFileAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignRepositorySettingsViewModel());

            Assert.Equal((4, 3, 2, 1), (view.Find<ItemsControl>("Files").ItemCount, view.Find<ItemsControl>("RuleList").ItemCount, view.Find<ItemsControl>("Caps").ItemCount, view.Find<ItemsControl>("Checks").ItemCount));
            Assert.Equal((false, false), (view.Shows("JobsFile"), view.Shows("Error")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task BeforeARepositoryIsReadNoRulesShowAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var view = Screen.Show(new RepositorySettingsViewModel(
                new RepositoryRules.RulesReader(new FakeRules(), new FakeRules(), new FakeRules(), new Testing.CommittedFiles()),
                new Machine.SettingsFiles(new FakeOpener(), new Sdk.AvalaPaths("/data")),
                bench.Board));

            Assert.False(view.Shows("Rules"));
            Assert.False(view.Find<Button>("Read").IsEffectivelyEnabled);
        }, TestContext.Current.CancellationToken);
}

public sealed class MachineSettingsViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheMachineShowsItsFilesConnectionsAndSilenceWindowAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignMachineSettingsViewModel());

            Assert.Equal(("connections.json: Applied", "Silence window: 600s"), (view.TextOf("ConnectionsFile"), view.TextOf("Silence")));
            Assert.Equal(2, view.Find<ItemsControl>("Connections").ItemCount);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task PressingEnterInTheWindowSavesItAsync() =>
        ui.RunAsync(async () =>
        {
            var supervision = new FakeSupervision();
            var machine = new MachineSettingsViewModel(new Machine.MachineSettings(new FakeConnections(), supervision, new FakeResources()), new Machine.SettingsFiles(new FakeOpener(), new Sdk.AvalaPaths("/data")));
            var view = Screen.Show(machine);

            view.Type("SilenceDraft", "120");
            view.Press(Key.Enter);
            await (machine.SaveSilenceCommand.ExecutionTask ?? Task.CompletedTask);

            Assert.Equal([TimeSpan.FromSeconds(120)], supervision.Changes);
        }, TestContext.Current.CancellationToken);
}

public sealed class RuleFileViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AFileEditedInTheCheckoutSaysSoAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignRuleFileViewModel());

            Assert.Equal((".avala/permissions.json", "4f2c9e1", true), (view.TextOf("Path"), view.TextOf("Commit"), view.Shows("Edited")));
        }, TestContext.Current.CancellationToken);
}

public sealed class RuleViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ARuleShowsItsOriginNameTargetAndAnswerAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignRuleViewModel());

            Assert.Equal(("Repository", "tests", "npm test*", "Allow"), (view.TextOf("Origin"), view.TextOf("RuleName"), view.TextOf("Target"), view.TextOf("Answer")));
        }, TestContext.Current.CancellationToken);
}

public sealed class CapsViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task CapsShowTheirScopeAndLimitsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignCapsViewModel());

            Assert.Equal(("Every connection", "5 USD per job, holds at 90% of a limit"), (view.TextOf("Scope"), view.TextOf("Caps")));
        }, TestContext.Current.CancellationToken);
}

public sealed class CheckViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ACheckShowsItsNameCommandAndTimeoutAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignCheckViewModel());

            Assert.Equal(("tests", "npm test", "300s"), (view.TextOf("CheckName"), view.TextOf("Command"), view.TextOf("Timeout")));
        }, TestContext.Current.CancellationToken);
}

public sealed class JobSectionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ASectionShowsItsNameAndValueAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignJobSectionViewModel());

            Assert.Equal(("approval", "merge"), (view.TextOf("SectionName"), view.Find<SelectableTextBlock>("Value").Text));
        }, TestContext.Current.CancellationToken);
}

public sealed class MachineConnectionViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AConnectionShowsItsNameWhetherItIsTheDefaultAndItsSourceAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignMachineConnectionViewModel("claude-personal", "the provider's own login", false));

            Assert.Equal(("claude-personal", false, "the provider's own login"), (view.TextOf("ConnectionName"), view.Shows("Default"), view.TextOf("Source")));
        }, TestContext.Current.CancellationToken);
}

public sealed class ResourcesViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ThePageShowsTheMachineAgentsLeftoversAndLeasesAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignResourcesViewModel());

            Assert.Equal(("Memory 1,204.3 MB", "CPU 60%", "11 processes"), (view.TextOf("Memory"), view.TextOf("Cpu"), view.TextOf("Processes")));
            Assert.Equal((2, 1, 1, 2), (view.Find<ItemsControl>("Trees").ItemCount, view.Find<ItemsControl>("Orphans").ItemCount, view.Find<ItemsControl>("StaleWorktrees").ItemCount, view.Find<ItemsControl>("Leases").ItemCount));
            Assert.Contains("Clean up", view.VisibleTexts);
        }, TestContext.Current.CancellationToken);
}

public sealed class AgentTreeViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ATreeShowsItsJobConnectionProcessesMemoryAndCpuAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignAgentTreeViewModel());

            Assert.Equal(("Fix JPY rounding in invoice totals", "claude-work", "4 processes", "412.6 MB", "18%"), (view.TextOf("Job"), view.TextOf("Connection"), view.TextOf("Processes"), view.TextOf("Memory"), view.TextOf("Cpu")));
        }, TestContext.Current.CancellationToken);
}

public sealed class OrphanViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AnOrphanLeftRunningSaysSoInAmberWithItsProcessesAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignOrphanViewModel());

            Assert.True(view.HasClass("Disposal", "attention"));
            Assert.Equal(2, view.Find<ItemsControl>("Processes").ItemCount);
        }, TestContext.Current.CancellationToken);
}

public sealed class StaleWorktreeViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AStaleWorktreeShowsItsPathAndReasonAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignStaleWorktreeViewModel());

            Assert.Equal(("~/.avala/worktrees/shop-web/old-checkout-spike", "not known to any job"), (view.TextOf("Path"), view.TextOf("Reason")));
        }, TestContext.Current.CancellationToken);
}

public sealed class ResourceIndicatorViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheIndicatorShowsMemoryAndLeftoversInAmberAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignResourceIndicatorViewModel());

            Assert.Equal(("Memory 1,204.3 MB", "2 left over"), (view.TextOf("Memory"), view.TextOf("Leftovers")));
            Assert.True(view.HasClass("Leftovers", "attention"));
        }, TestContext.Current.CancellationToken);
}

public sealed class NewJobViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheFormProposesTheRepositoryAndOffersTheConnectionsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignNewJobViewModel());

            Assert.Equal(3, view.Find<ComboBox>("Connection").ItemCount);
            Assert.Equal("claude-work", view.Find<ComboBox>("Connection").SelectedItem);
            Assert.Equal(("Submitted: Fix JPY rounding in invoice totals", false), (view.TextOf("Submitted"), view.Shows("Error")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task WithoutAnInstructionTheJobCannotBeSubmittedAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var page = new NewJobViewModel(new Submitting.JobLaunch(new SubmittingJobs(), new FakeConnections("claude-work")), bench.Board) { Repository = "~/code/shop-api" };
            var view = Screen.Show(page);

            var empty = view.Find<Button>("Submit").IsEffectivelyEnabled;
            view.Type("Instruction", "Add invoice PDF endpoint");

            Assert.Equal((false, true), (empty, view.Find<Button>("Submit").IsEffectivelyEnabled));
        }, TestContext.Current.CancellationToken);
}
