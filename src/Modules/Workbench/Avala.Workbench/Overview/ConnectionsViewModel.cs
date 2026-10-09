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

internal interface IConnectionsViewModel : IActivatable, IPresentation
{
    IReadOnlyList<IConnectionCardViewModel> Connections { get; }

    string FileNote { get; }

    string Caption { get; }

    IRelayCommand<JobId> OpenCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ConnectionsViewModel(FleetReader reader, LiveFeed feed, JobFocus focus, JobSpending spending) : IConnectionsViewModel, IDisposable
{
    private readonly ObservableCollection<ConnectionCardViewModel> connections = [];

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

    public Task Following => feed.Following;

    public void Activate() => feed.Start(reader.ReadAsync, Show);

    public void Deactivate() => feed.Stop();

    public void Dispose() => feed.Dispose();

    [RelayCommand]
    private void Open(JobId job) => focus.Select(job);

    private void Show(FleetState fleet)
    {
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

    public static string Hub(string account, string cost, bool isDefault) =>
        isDefault ? $"{account} · {cost} · the default connection" : $"{account} · {cost}";

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
