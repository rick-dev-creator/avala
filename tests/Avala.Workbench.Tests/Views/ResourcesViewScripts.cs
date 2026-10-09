using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing.UI;
using Avala.Workbench.Resources;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Views;

public sealed class ResourcesViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ThePageShowsTheMachineWhatIsLeftBehindTheRunningAgentsAndThePortLeasesAsync() =>
        ui.RunAsync(() =>
        {
            var view = Wide(new DesignResourcesViewModel());

            Assert.Equal(("7,168.0 MB", "34%", "23 processes", "4 leased", "Left behind · 5"), (view.TextOf("Memory"), view.TextOf("Cpu"), view.TextOf("Processes"), view.TextOf("LeaseCount"), view.TextOf("LeftBehind")));
            Assert.Equal((4, 2, 2, 1, 4), (view.Find<ItemsControl>("Trees").ItemCount, view.Find<ItemsControl>("Orphans").ItemCount, view.Find<ItemsControl>("StaleWorktrees").ItemCount, view.Find<ItemsControl>("Conflicts").ItemCount, view.Find<ItemsControl>("Leases").ItemCount));
            Assert.Equal((false, true, false), (view.Shows("NothingLeft"), view.Shows("Clean"), view.Shows("NoAgents")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task StoppingAProcessLeftRunningReapsItsJobAsync() =>
        ui.RunAsync(() =>
        {
            var page = new Recording();
            var view = Wide(page);

            view.Click("Stop");

            Assert.Equal([Presenting.SampleJobs.SyncQueue], page.Reaped);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task WhenNothingIsLeftBehindTheQuietEmptyStateShowsAndCleaningIsNotOfferedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Wide(new Recording { Orphans = [], StaleWorktrees = [], Conflicts = [], LeftBehind = 0, Trees = [] });

            Assert.Equal((true, false, true), (view.Shows("NothingLeft"), view.Shows("Clean"), view.Shows("NoAgents")));
        }, TestContext.Current.CancellationToken);

    private static ViewScript Wide(object viewModel)
    {
        var view = Screen.Show(viewModel);
        view.Window.Width = 1200;
        view.Window.Height = 1400;

        return view.Settle();
    }

    private sealed class Recording : IResourcesViewModel
    {
        private readonly DesignResourcesViewModel design = new();

        public Recording() => ReapCommand = new AsyncRelayCommand<IOrphanViewModel>(orphan =>
        {
            _ = orphan!.Job.Match(job => { Reaped.Add(job); return true; }, () => false);
            return Task.CompletedTask;
        });

        public List<JobId> Reaped { get; } = [];

        public string Title => design.Title;

        public IReadOnlyList<IAgentTreeViewModel> Trees { get; init; } = new DesignResourcesViewModel().Trees;

        public IReadOnlyList<IOrphanViewModel> Orphans { get; init; } = [new DesignOrphanViewModel()];

        public IReadOnlyList<IStaleWorktreeViewModel> StaleWorktrees { get; init; } = [];

        public IReadOnlyList<string> Leases => design.Leases;

        public IReadOnlyList<string> Conflicts { get; init; } = [];

        public string Memory => design.Memory;

        public string Cpu => design.Cpu;

        public int Processes => design.Processes;

        public string Disk => design.Disk;

        public string Ports => design.Ports;

        public string Error => string.Empty;

        public int LeftBehind { get; init; } = 1;

        public IAsyncRelayCommand<IOrphanViewModel> ReapCommand { get; }

        public IAsyncRelayCommand ReconcileCommand => design.ReconcileCommand;

        public IAsyncRelayCommand CleanCommand => design.CleanCommand;
    }
}

public sealed class AgentTreeViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task ATreeShowsItsJobConnectionMemoryCpuProcessesAndPortsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignAgentTreeViewModel("Fix flaky CheckoutForm test", "claude-personal", 7, "1,638.4 MB", "3%", "41010, 41011", StatusKind.NeedsYou));

            Assert.Equal(("Fix flaky CheckoutForm test", "claude-personal", "1,638.4 MB", "3%", "7", "41010, 41011"), (view.TextOf("Job"), view.TextOf("Connection"), view.TextOf("Memory"), view.TextOf("Cpu"), view.TextOf("Processes"), view.TextOf("Ports")));
            Assert.Contains(view.All<UserControl>(), control => control.GetType().Name == "StatusDotView" && control.Classes.Contains("needsYou"));
        }, TestContext.Current.CancellationToken);
}

public sealed class OrphanViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AProcessLeftRunningSaysSoInAmberAndCanBeStoppedAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignOrphanViewModel());

            Assert.Equal((true, 1, "1,126.4 MB"), (view.HasClass("Disposal", "attention"), view.Find<ItemsControl>("Processes").ItemCount, view.TextOf("Memory")));
            Assert.True(view.Find<Button>("Stop").IsVisible);
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task AProcessWithoutAJobCannotBeStoppedFromHereAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignOrphanViewModel(Option<JobId>.None, ["postgres (39004)"], true, "180.0 MB"));

            Assert.False(view.Find<Button>("Stop").IsVisible);
        }, TestContext.Current.CancellationToken);
}

public sealed class StaleWorktreeViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task AStaleWorktreeShowsItsNamePathAndReasonAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignStaleWorktreeViewModel());

            Assert.Equal(("Worktree · switch-queue-fifo", "~/.avala/worktrees/ledger-api/switch-queue-fifo", "not known to any job"), (view.TextOf("Worktree"), view.TextOf("Path"), view.TextOf("Reason")));
        }, TestContext.Current.CancellationToken);
}

public sealed class ResourceIndicatorViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheIndicatorShowsMemoryAndLeftoversInAmberAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new DesignResourceIndicatorViewModel());

            Assert.Equal(("Memory 7,168.0 MB", "2 left over"), (view.TextOf("Memory"), view.TextOf("Leftovers")));
            Assert.True(view.HasClass("Leftovers", "attention"));
        }, TestContext.Current.CancellationToken);
}

public sealed class NewJobViewScripts(HeadlessUi ui)
{
    [Fact]
    public Task TheFormProposesTheRepositoryAndOffersTheConnectionsAsync() =>
        ui.RunAsync(() =>
        {
            var view = Screen.Show(new Avala.Workbench.NewJob.DesignNewJobViewModel());

            Assert.Equal(3, view.Find<ComboBox>("Connection").ItemCount);
            Assert.Equal("claude-work", view.Find<ComboBox>("Connection").SelectedItem);
            Assert.Equal(("Submitted: Fix JPY rounding in invoice totals", false), (view.TextOf("Submitted"), view.Shows("Error")));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task WithoutAnInstructionTheJobCannotBeSubmittedAsync() =>
        ui.RunAsync(() =>
        {
            using var bench = new Bench();
            var page = new Avala.Workbench.NewJob.NewJobViewModel(new Submitting.JobLaunch(new SubmittingJobs(), new FakeConnections("claude-work")), bench.Board) { Repository = "~/code/shop-api" };
            var view = Screen.Show(page);

            var empty = view.Find<Button>("Submit").IsEffectivelyEnabled;
            view.Type("Instruction", "Add invoice PDF endpoint");

            Assert.Equal((false, true), (empty, view.Find<Button>("Submit").IsEffectivelyEnabled));
        }, TestContext.Current.CancellationToken);

    [Fact]
    public Task ARejectedJobShowsWhyInPlaceAndKeepsTheInstructionAsync() =>
        ui.RunAsync(async () =>
        {
            using var bench = new Bench();
            var jobs = new SubmittingJobs { Refusal = JobRejection.UnknownConnection };
            var page = new Avala.Workbench.NewJob.NewJobViewModel(new Submitting.JobLaunch(jobs, new FakeConnections("claude-work")), bench.Board) { Repository = "~/code/shop-api" };
            var view = Screen.Show(page);

            view.Type("Instruction", "Add invoice PDF endpoint");
            view.Press(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.Control);
            await (page.SubmitCommand.ExecutionTask ?? Task.CompletedTask);
            view.Settle();

            Assert.Equal((true, "No connection has that name.", "Add invoice PDF endpoint"), (view.Shows("Error"), view.TextOf("ErrorText"), page.Instruction));
            Assert.Single(jobs.Requests);
        }, TestContext.Current.CancellationToken);
}
