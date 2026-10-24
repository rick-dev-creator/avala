using Avala.Components.Privacy;
using System.Collections.ObjectModel;
using System.Globalization;
using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Fleet;
using Avala.Workbench.Following;
using Avala.Workbench.Navigation;
using Avala.Workbench.Presenting;
using Avala.Workbench.Spending;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Overview;

internal enum FleetScope
{
    InProgress,
    ForReview,
    All,
}

internal interface IConnectionsViewModel : IActivatable, IPresentation
{
    IReadProblemViewModel ReadProblem { get; }

    IReadOnlyList<IConnectionCardViewModel> Connections { get; }

    string FileNote { get; }

    string Caption { get; }

    FleetScope Scope { get; }

    string InProgressLabel { get; }

    string ForReviewLabel { get; }

    string AllLabel { get; }

    IRelayCommand<FleetScope> ScopeCommand { get; }

    IRelayCommand<JobId> OpenCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ConnectionsViewModel(FleetReader reader, LiveFeed feed, JobFocus focus, JobSpending spending) : IConnectionsViewModel, IDisposable
{
    private readonly ObservableCollection<ConnectionCardViewModel> connections = [];
    private FleetState shown = new(ConnectionFileStatus.Absent, Option<ConnectionError>.None, []);

    public event EventHandler<Presented>? Presented
    {
        add => feed.Presented += value;
        remove => feed.Presented -= value;
    }

    public long Revision => feed.Revision;

    public IReadOnlyList<IConnectionCardViewModel> Connections => connections;

    [ObservableProperty]
    public partial string FileNote { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Caption { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial FleetScope Scope { get; private set; }

    [ObservableProperty]
    public partial string InProgressLabel { get; private set; } = "In progress";

    [ObservableProperty]
    public partial string ForReviewLabel { get; private set; } = "Ready for review";

    [ObservableProperty]
    public partial string AllLabel { get; private set; } = "All";

    public Task Following => feed.Following;

    public IReadProblemViewModel ReadProblem { get; } = new ReadProblemViewModel(feed);

    public void Activate() => feed.Start(reader.ReadAsync, Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    [RelayCommand]
    private void Open(JobId job) => focus.Select(job);

    [RelayCommand]
    private void ChooseScope(FleetScope scope)
    {
        Scope = scope;
        Filter();
    }

    IRelayCommand<FleetScope> IConnectionsViewModel.ScopeCommand => ChooseScopeCommand;

    private void Show(FleetState fleet)
    {
        shown = fleet;
        var agents = fleet.Connections.SelectMany(connection => connection.Agents).ToList();
        InProgressLabel = $"In progress · {agents.Count(agent => In(FleetScope.InProgress, agent))}";
        ForReviewLabel = $"Ready for review · {agents.Count(agent => In(FleetScope.ForReview, agent))}";
        AllLabel = $"All · {agents.Count}";
        Filter();
    }

    private void Filter()
    {
        var fleet = shown with { Connections = [.. shown.Connections.Select(connection => connection with { Agents = [.. connection.Agents.Where(agent => In(Scope, agent))] })] };
        FileNote = fleet.File == ConnectionFileStatus.Rejected
            ? $"connections.json is rejected: {fleet.Error.Match(error => error.ToString(), () => "invalid")}"
            : string.Empty;
        connections.Reconcile(
            fleet.Connections,
            card => card.Connection,
            connection => connection.Name,
            connection => new ConnectionCardViewModel(connection, Hold(connection)),
            (card, connection) => card.Update(connection, Hold(connection)));
        Caption = OverviewPhrases.Fleet(connections.Sum(card => card.Agents.Count), connections.Count, connections.Select(card => card.Provider).Where(name => name.Length > 0).Distinct().Count());
    }

    private static bool In(FleetScope scope, Board.BoardJob agent) => scope switch
    {
        FleetScope.InProgress => agent.Group is Board.JobGroup.NeedsYou or Board.JobGroup.Running or Board.JobGroup.Waiting,
        FleetScope.ForReview => agent.Group is Board.JobGroup.ReadyForReview or Board.JobGroup.Delivered,
        _ => true,
    };

    private Option<double> Hold(ConnectionState connection) => spending.CapsOn(connection.Name).Bind(caps => caps.HoldAtLimit);
}

internal static partial class OverviewPhrases
{
    public static string Fleet(int agents, int connections, int harnesses) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Count(agents, "agent")} · {Count(connections, "connection")} on {Count(harnesses, "harness", "harnesses")} · hover for a summary, click to open");

    public static string Count(int count, string one) => Count(count, one, one + "s");

    public static string Count(int count, string one, string many) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? one : many)}");

    public static PersonalPhrase Hub(Personal account, string cost, bool isDefault) =>
        isDefault ? new PersonalPhrase(account, $" · {cost} · the default connection") : new PersonalPhrase(account, $" · {cost}");

    public static string State(Components.Status.StatusKind kind) => kind switch
    {
        Components.Status.StatusKind.Working => "Running",
        Components.Status.StatusKind.Checking => "Checking",
        Components.Status.StatusKind.NeedsYou => "Needs you",
        Components.Status.StatusKind.Held => "Held",
        Components.Status.StatusKind.ReadyForReview => "Ready for review",
        Components.Status.StatusKind.Failed => "Failed",
        _ => "Done",
    };
}
