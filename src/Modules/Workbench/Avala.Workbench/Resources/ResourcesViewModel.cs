using System.Collections.ObjectModel;
using System.Globalization;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Following;
using Avala.Workbench.Presenting;
using Avala.Workbench.Upkeep;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Resources;

internal interface IResourcesViewModel
{
    string Title { get; }

    IReadOnlyList<IAgentTreeViewModel> Trees { get; }

    IReadOnlyList<IOrphanViewModel> Orphans { get; }

    IReadOnlyList<IStaleWorktreeViewModel> StaleWorktrees { get; }

    IReadOnlyList<string> Leases { get; }

    IReadOnlyList<string> Conflicts { get; }

    string Memory { get; }

    string Cpu { get; }

    int Processes { get; }

    string Disk { get; }

    string Ports { get; }

    string Error { get; }

    int LeftBehind { get; }

    IAsyncRelayCommand<IOrphanViewModel> ReapCommand { get; }

    IAsyncRelayCommand ReconcileCommand { get; }

    IAsyncRelayCommand CleanCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ResourcesViewModel(ResourceReader reader, Housekeeping housekeeping, LiveFeed feed) : IResourcesViewModel, IPage, IActivatable, IPresentation, IDisposable
{
    private readonly ObservableCollection<AgentTreeViewModel> trees = [];
    private readonly ObservableCollection<OrphanViewModel> orphans = [];
    private readonly ObservableCollection<StaleWorktreeViewModel> staleWorktrees = [];
    private readonly ObservableCollection<string> leases = [];
    private readonly ObservableCollection<string> conflicts = [];

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    public string Title => "Resources";

    public PagePlacement Placement => PagePlacement.Hidden;

    public IReadOnlyList<IAgentTreeViewModel> Trees => trees;

    public IReadOnlyList<IOrphanViewModel> Orphans => orphans;

    public IReadOnlyList<IStaleWorktreeViewModel> StaleWorktrees => staleWorktrees;

    public IReadOnlyList<string> Leases => leases;

    public IReadOnlyList<string> Conflicts => conflicts;

    [ObservableProperty]
    public partial string Memory { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Cpu { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial int Processes { get; private set; }

    [ObservableProperty]
    public partial string Disk { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Ports { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial int LeftBehind { get; private set; }

    public Task Following => feed.Following;

    public void Activate() => feed.Start(_ => ValueTask.FromResult(reader.Read()), Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    [RelayCommand(CanExecute = nameof(CanReap))]
    private async Task ReapAsync(IOrphanViewModel? orphan, CancellationToken cancellationToken)
    {
        var reaped = await (orphan?.Job ?? Option<Jobs.Contracts.JobId>.None).Match(
            job => housekeeping.ReapAsync(job, cancellationToken).AsTask(),
            () => Task.FromResult(Result<int, ResourceError>.Failure(ResourceError.NothingToReap)));
        Error = reaped.Match(_ => string.Empty, _ => "Nothing of that job is left running.");
    }

    [RelayCommand]
    private async Task ReconcileAsync(CancellationToken cancellationToken)
    {
        _ = await housekeeping.ReconcileAsync(cancellationToken);
        feed.Refresh();
    }

    [RelayCommand]
    private async Task CleanAsync(CancellationToken cancellationToken)
    {
        _ = await housekeeping.CleanAsync(cancellationToken);
        feed.Refresh();
    }

    private static bool CanReap(IOrphanViewModel? orphan) => orphan is { CanReap: true };

    private void Show(ResourceState state)
    {
        Memory = Amounts.Megabytes(state.Global.MemoryBytes);
        Cpu = Amounts.Percent(state.Global.CpuLoad);
        Processes = state.Global.Processes;
        Disk = Amounts.Megabytes(state.Global.DiskBytes);
        Ports = string.Join(", ", state.Global.Ports.Select(port => port.ToString(CultureInfo.InvariantCulture)));
        trees.ShowOnly(state.Trees.Select(tree => new AgentTreeViewModel(tree)));
        orphans.ShowOnly(state.Orphans.Select(report => new OrphanViewModel(report)));
        staleWorktrees.ShowOnly(
            state.Stale.Strays.Select(path => new StaleWorktreeViewModel(path, "not known to any job"))
                .Concat(state.Stale.Missing.Select(workspace => new StaleWorktreeViewModel(workspace.Path, "missing from the disk"))));
        leases.ShowOnly(state.Leases.Select(lease => string.Create(CultureInfo.InvariantCulture, $"{lease.First}-{lease.Last} for {lease.Worktree}")));
        LeftBehind = state.LeftRunning.Count + state.Stale.Strays.Count + state.Stale.Missing.Count + state.Conflicts.Count;
        conflicts.ShowOnly(state.Conflicts.Select(conflict => string.Create(CultureInfo.InvariantCulture, $"port {conflict.Port} held outside {conflict.Lease.Worktree}")));
    }
}
