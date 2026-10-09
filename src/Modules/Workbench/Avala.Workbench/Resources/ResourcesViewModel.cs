using System.Collections.ObjectModel;
using System.Globalization;
using Avala.Resources.Contracts;
using Avala.Sdk;
using Avala.Workbench.Following;
using Avala.Workbench.Presenting;
using Avala.Workbench.Upkeep;
using Avala.Workspaces.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Resources;

[INotifyPropertyChanged]
internal sealed partial class ResourcesViewModel(ResourceReader reader, Housekeeping housekeeping, LiveFeed feed) : IPage, IActivatable, IDisposable
{
    public string Title => "Resources";

    public ObservableCollection<AgentTreeViewModel> Trees { get; } = [];

    public ObservableCollection<OrphanViewModel> Orphans { get; } = [];

    public ObservableCollection<StaleWorktreeViewModel> StaleWorktrees { get; } = [];

    public ObservableCollection<string> Leases { get; } = [];

    public ObservableCollection<string> Conflicts { get; } = [];

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

    public Task Following => feed.Following;

    public void Activate() => feed.Start(_ => ValueTask.FromResult(reader.Read()), Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    [RelayCommand]
    private async Task ReapAsync(OrphanViewModel orphan, CancellationToken cancellationToken)
    {
        var reaped = await orphan.Job.Match(
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

    private void Show(ResourceState state)
    {
        Memory = Amounts.Megabytes(state.Global.MemoryBytes);
        Cpu = Amounts.Percent(state.Global.CpuLoad);
        Processes = state.Global.Processes;
        Disk = Amounts.Megabytes(state.Global.DiskBytes);
        Ports = string.Join(", ", state.Global.Ports.Select(port => port.ToString(CultureInfo.InvariantCulture)));
        Trees.ShowOnly(state.Trees.Select(tree => new AgentTreeViewModel(tree)));
        Orphans.ShowOnly(state.Orphans.Select(report => new OrphanViewModel(report)));
        StaleWorktrees.ShowOnly(
            state.Stale.Strays.Select(path => new StaleWorktreeViewModel(path, "not known to any job"))
                .Concat(state.Stale.Missing.Select(workspace => new StaleWorktreeViewModel(workspace.Path, "missing from the disk"))));
        Leases.ShowOnly(state.Leases.Select(lease => string.Create(CultureInfo.InvariantCulture, $"{lease.First}-{lease.Last} for {lease.Worktree}")));
        Conflicts.ShowOnly(state.Conflicts.Select(conflict => string.Create(CultureInfo.InvariantCulture, $"port {conflict.Port} held outside {conflict.Lease.Worktree}")));
    }
}
