using System.Collections.ObjectModel;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Fleet;
using Avala.Workbench.Following;
using Avala.Workbench.Navigation;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Overview;

internal sealed record DelegationPaneRequested(JobId Job);

internal sealed record DelegationPaneChanged(JobId Job, bool IsOpen);

internal interface IDelegationPaneViewModel
{
    IOrchestratorCardViewModel? Root { get; }

    IReadOnlyList<IDelegationNodeViewModel> Children { get; }

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
    private readonly JobFocus focus;
    private readonly IMessenger messenger;
    private readonly ObservableCollection<DelegationNodeViewModel> children = [];
    private Option<JobId> job;

    public DelegationPaneViewModel(DelegationReader reader, LiveFeed feed, JobFocus focus, IMessenger messenger)
    {
        this.reader = reader;
        this.feed = feed;
        this.focus = focus;
        this.messenger = messenger;
        messenger.RegisterAll(this);
    }

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    public Task Following => feed.Following;

    public IReadOnlyList<IDelegationNodeViewModel> Children => children;

    [ObservableProperty]
    public partial IOrchestratorCardViewModel? Root { get; private set; }

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
        messenger.UnregisterAll(this);
    }

    public void OnRegionContextChanged(Option<JobId> context)
    {
        job = context;
        Toggle(IsOpen && job.Match(reader.InTree, () => false));
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
    private void Open(JobId opened) => focus.Select(opened);

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

    private async ValueTask<(Option<JobId> Viewing, Option<DelegationTree> Tree)> ReadAsync(CancellationToken cancellationToken) =>
        IsOpen && job.Match<JobId?>(found => found, () => null) is { } viewing
            ? (viewing, await reader.TreeAsync(reader.RootOf(viewing), cancellationToken))
            : (job, Option<DelegationTree>.None);

    private void Show((Option<JobId> Viewing, Option<DelegationTree> Tree) state)
    {
        var viewing = state.Viewing;
        children.ShowOnly(state.Tree.Match(
            tree => tree.Children.Select(node => new DelegationNodeViewModel(node) { OpenCommand = OpenCommand, IsViewing = viewing == Option<JobId>.Some(node.Job.Job) }),
            () => []));
        Root = state.Tree.Match<IOrchestratorCardViewModel?>(
            tree => new OrchestratorCardViewModel(tree) { OpenCommand = OpenCommand, IsViewing = viewing == Option<JobId>.Some(tree.Root.Job) },
            () => null);
        Caption = state.Tree.Match(tree => DelegationPanePhrases.Caption(tree), () => string.Empty);
        Footer = state.Tree.Match(DelegationPanePhrases.Footer, () => string.Empty);
    }
}

internal static class DelegationPanePhrases
{
    public static string Caption(DelegationTree tree)
    {
        var jobs = tree.Children.Count + 1;
        var agents = SubagentViewModel.Of(tree.RootJob).Count + tree.Children.Sum(node => SubagentViewModel.Of(node.Seen).Count);

        return agents == 0 ? OverviewPhrases.Count(jobs, "job") : $"{OverviewPhrases.Count(jobs, "job")} · {OverviewPhrases.Count(agents, "agent")}";
    }

    public static string Footer(DelegationTree tree) =>
        string.Join(
            "   ",
            tree.Children.Select(node => node.Job.Connection.Match(name => name.Value, () => "not started"))
                .Prepend(tree.Root.Connection.Match(name => name.Value, () => "default connection"))
                .GroupBy(connection => connection, StringComparer.Ordinal)
                .Select(group => $"{group.Key} · {OverviewPhrases.Count(group.Count(), "job")}"));
}
