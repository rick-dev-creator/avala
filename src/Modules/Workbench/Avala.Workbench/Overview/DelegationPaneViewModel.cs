using System.Collections.ObjectModel;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Fleet;
using Avala.Workbench.Following;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Overview;

internal sealed record DelegationPaneRequested(JobId Job);

internal sealed record DelegationPaneChanged(JobId Job, bool IsOpen);

internal sealed record DelegationPaneState(Option<JobId> Viewing, Option<DelegationTree> Tree, IReadOnlyList<string> Running);

internal interface IDelegationPaneViewModel
{
    ISubagentTreeViewModel Tree { get; }

    bool HasTree { get; }

    string Caption { get; }

    string Footer { get; }

    bool IsOpen { get; }

    IRelayCommand CloseCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class DelegationPaneViewModel
    : IDelegationPaneViewModel, IPane, IRegionAware<JobId>, IActivatable, IPresentation, IRecipient<DelegationPaneRequested>, IDisposable
{
    private readonly DelegationReader reader;
    private readonly LiveFeed feed;
    private readonly RowActions actions;
    private readonly IMessenger messenger;
    private readonly SubagentTreeViewModel tree;
    private readonly HashSet<string> started = new(StringComparer.Ordinal);
    private Option<JobId> job;

    public DelegationPaneViewModel(DelegationReader reader, LiveFeed feed, RowActions actions, IMessenger messenger)
    {
        this.reader = reader;
        this.feed = feed;
        this.actions = actions;
        this.messenger = messenger;
        tree = new SubagentTreeViewModel(OpenCommand, actions, feed.Refresh);
        messenger.RegisterAll(this);
    }

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    public Task Following => feed.Following;

    public ISubagentTreeViewModel Tree => tree;

    public SubagentTreeViewModel Shown => tree;

    [ObservableProperty]
    public partial bool HasTree { get; private set; }

    [ObservableProperty]
    public partial string Caption { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Footer { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    public void Activate() => feed.Start(ReadAsync, Show);

    public void Deactivate() => feed.Stop();

    public void Dispose()
    {
        feed.Dispose();
        tree.Dispose();
        messenger.UnregisterAll(this);
    }

    public void OnRegionContextChanged(Option<JobId> context)
    {
        job = context;
        started.UnionWith(context.Match(reader.Running, () => []));
        Toggle(IsOpen && job.Match(reader.Offers, () => false));
        feed.Refresh();
    }

    public void Receive(DelegationPaneRequested message)
    {
        if (job == Option<JobId>.Some(message.Job))
        {
            Toggle(!IsOpen);
            feed.Refresh();
        }
    }

    public void Close() => Toggle(false);

    [RelayCommand]
    private void CloseNow() => Toggle(false);

    IRelayCommand IDelegationPaneViewModel.CloseCommand => CloseNowCommand;

    [RelayCommand]
    private void Open(JobId opened) => actions.Open(opened);

    private void Toggle(bool open)
    {
        if (IsOpen == open)
        {
            return;
        }

        IsOpen = open;

        foreach (var current in job.Match<JobId[]>(found => [found], () => []))
        {
            messenger.Send(new DelegationPaneChanged(current, open));
        }
    }

    private async ValueTask<DelegationPaneState> ReadAsync(CancellationToken cancellationToken)
    {
        var shown = job;
        var running = shown.Match(reader.Running, () => []);

        return IsOpen && shown.Match<JobId?>(found => found, () => null) is { } viewing
            ? new DelegationPaneState(viewing, await reader.TreeAsync(reader.RootOf(viewing), cancellationToken), running)
            : new DelegationPaneState(shown, Option<DelegationTree>.None, running);
    }

    private void Show(DelegationPaneState state)
    {
        var fresh = state.Running.Except(started, StringComparer.Ordinal).ToList();
        started.UnionWith(fresh);

        if (fresh.Count > 0 && !IsOpen && state.Viewing == job)
        {
            Toggle(true);
            feed.Refresh();
        }

        state.Tree.Match(
            shown =>
            {
                tree.Show(shown, state.Viewing);

                return true;
            },
            () =>
            {
                tree.Clear();

                return false;
            });
        HasTree = state.Tree.IsSome;
        Caption = state.Tree.Match(tree => DelegationPanePhrases.Caption(tree), () => string.Empty);
        Footer = state.Tree.Match(DelegationPanePhrases.Footer, () => string.Empty);
    }
}

internal static class DelegationPanePhrases
{
    public static string Caption(DelegationTree tree)
    {
        var jobs = tree.Children.Count + 1;
        var agents = AgentBranches.Of(tree).Descendants.Count(branch => branch.Kind == BranchKind.InProcess);

        return agents == 0 ? OverviewPhrases.Count(jobs, "job") : $"{OverviewPhrases.Count(jobs, "job")} · {OverviewPhrases.Count(agents, "agent")}";
    }

    public static string Footer(DelegationTree tree) =>
        string.Join(
            "   ",
            tree.Children.Select(node => (Connection: node.Job.Connection.Match(name => name.Value, () => "not started"), Context: AgentGlance.CompactsAt(node.Seen)))
                .Prepend((Connection: tree.Root.Connection.Match(name => name.Value, () => "default connection"), Context: AgentGlance.CompactsAt(tree.RootJob)))
                .GroupBy(job => job.Connection, StringComparer.Ordinal)
                .Select(group => Connection(group.Key, group.Count(), [.. group.SelectMany(job => job.Context.Match<long[]>(at => [at], () => [])).Distinct().Order()])));

    private static string Connection(string name, int jobs, IReadOnlyList<long> windows) =>
        windows.Count == 0
            ? $"{name} · {OverviewPhrases.Count(jobs, "job")}"
            : $"{name} · {OverviewPhrases.Count(jobs, "job")} · compacts at {string.Join(" or ", windows.Select(at => AgentGlance.TokensText(at).Replace(" tokens", string.Empty, StringComparison.Ordinal)))}";
}
