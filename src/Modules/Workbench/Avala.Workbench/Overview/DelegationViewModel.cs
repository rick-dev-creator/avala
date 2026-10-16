using System.Collections.ObjectModel;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Fleet;
using Avala.Workbench.Following;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Overview;

internal enum DelegationScope
{
    Active,
    Finished,
    All,
}

internal sealed record DelegationState(IReadOnlyList<DelegationTree> Trees);

internal interface IDelegationViewModel : IActivatable, IPresentation
{
    IReadOnlyList<IOrchestrationViewModel> Trees { get; }

    DelegationScope Scope { get; }

    string ActiveLabel { get; }

    string FinishedLabel { get; }

    IReadOnlyList<string> Repositories { get; }

    string Repository { get; set; }

    IReadOnlyList<string> Connections { get; }

    string Connection { get; set; }

    string Caption { get; }

    string Empty { get; }

    IRelayCommand<DelegationScope> ScopeCommand { get; }

    IRelayCommand<JobId> OpenCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class DelegationViewModel(DelegationReader reader, LiveFeed feed, RowActions actions) : IDelegationViewModel, IDisposable
{
    public const string AllRepositories = "All repositories";

    public const string AllConnections = "All connections";

    private readonly ObservableCollection<OrchestrationViewModel> trees = [];
    private readonly Dictionary<JobId, OrchestrationViewModel> known = [];
    private DelegationState shown = new([]);

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    public IReadOnlyList<IOrchestrationViewModel> Trees => trees;

    [ObservableProperty]
    public partial DelegationScope Scope { get; private set; }

    [ObservableProperty]
    public partial string ActiveLabel { get; private set; } = "Active";

    [ObservableProperty]
    public partial string FinishedLabel { get; private set; } = "Finished";

    [ObservableProperty]
    public partial IReadOnlyList<string> Repositories { get; private set; } = [AllRepositories];

    [ObservableProperty]
    public partial string Repository { get; set; } = AllRepositories;

    [ObservableProperty]
    public partial IReadOnlyList<string> Connections { get; private set; } = [AllConnections];

    [ObservableProperty]
    public partial string Connection { get; set; } = AllConnections;

    [ObservableProperty]
    public partial string Caption { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Empty { get; private set; } = DelegationPhrases.NoneYet;

    public Task Following => feed.Following;

    public void Activate() => feed.Start(ReadAsync, Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    partial void OnRepositoryChanged(string value) => Filter();

    partial void OnConnectionChanged(string value) => Filter();

    [RelayCommand]
    private void ChooseScope(DelegationScope scope)
    {
        Scope = scope;
        Filter();
    }

    IRelayCommand<DelegationScope> IDelegationViewModel.ScopeCommand => ChooseScopeCommand;

    [RelayCommand]
    private void Open(JobId job) => actions.Open(job);

    private async ValueTask<DelegationState> ReadAsync(CancellationToken cancellationToken)
    {
        var found = await reader.OrchestratorsAsync(cancellationToken);
        var read = new List<DelegationTree>();

        foreach (var root in found)
        {
            read.AddRange((await reader.TreeAsync(root.Job, cancellationToken)).Match<DelegationTree[]>(tree => [tree], () => []));
        }

        return new DelegationState(read);
    }

    private void Show(DelegationState state)
    {
        shown = state;

        foreach (var tree in state.Trees)
        {
            if (known.TryGetValue(tree.Root.Job, out var existing))
            {
                existing.Show(tree);
            }
            else
            {
                known[tree.Root.Job] = new OrchestrationViewModel(tree, OpenCommand, actions);
            }
        }

        var all = state.Trees.Select(tree => known[tree.Root.Job]).ToList();
        Repositories = Options(AllRepositories, all.Select(tree => tree.Repository), Repositories);
        Connections = Options(AllConnections, all.SelectMany(tree => tree.Connections), Connections);
        ActiveLabel = $"Active · {all.Count(tree => tree.IsActive)}";
        FinishedLabel = $"Finished · {all.Count(tree => !tree.IsActive)}";
        Filter();
    }

    private void Filter()
    {
        var chosen = shown.Trees.Select(tree => known[tree.Root.Job])
            .Where(tree => Scope == DelegationScope.All || tree.IsActive == (Scope == DelegationScope.Active))
            .Where(tree => Repository == AllRepositories || tree.Repository == Repository)
            .Where(tree => Connection == AllConnections || tree.Connections.Contains(Connection))
            .OrderByDescending(tree => tree.LastActive)
            .ToList();
        trees.ShowInPlace(chosen, ReferenceEquals, tree => tree, (_, _) => { });

        while (trees.Count > chosen.Count)
        {
            trees.RemoveAt(trees.Count - 1);
        }

        Caption = shown.Trees.Count == 0 ? string.Empty : DelegationPhrases.Caption(chosen);
        Empty = shown.Trees.Count == 0 ? DelegationPhrases.NoneYet : DelegationPhrases.NoneShown;
    }

    private static IReadOnlyList<string> Options(string all, IEnumerable<string> found, IReadOnlyList<string> shown)
    {
        var next = found.Where(name => name.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Prepend(all).ToList();

        return next.SequenceEqual(shown) ? shown : next;
    }
}
