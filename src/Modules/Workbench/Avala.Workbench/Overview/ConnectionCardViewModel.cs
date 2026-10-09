using System.Collections.ObjectModel;
using Avala.Agents.Contracts.Connections;
using Avala.Components.Meters;
using Avala.Components.Status;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Fleet;
using Avala.Workbench.Presenting;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Usage;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Overview;

internal interface IConnectionCardViewModel
{
    string Name { get; }

    string Provider { get; }

    string Account { get; }

    bool IsDefault { get; }

    string Cost { get; }

    IReadOnlyList<ILimitViewModel> Limits { get; }

    IReadOnlyList<IAgentViewModel> Agents { get; }

    double Used { get; }

    string Use { get; }

    bool IsNearLimit { get; }

    bool IsProminent { get; }

    string Summary { get; }
}

internal interface IAgentViewModel
{
    JobId Job { get; }

    string Title { get; }

    JobStatus Status { get; }

    string Fact { get; }

    string State { get; }

    IStatusDotViewModel Dot { get; }

    string Summary { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ConnectionCardViewModel : IConnectionCardViewModel
{
    private readonly ObservableCollection<ILimitViewModel> limits = [];
    private readonly ObservableCollection<AgentViewModel> agents = [];

    public ConnectionCardViewModel(ConnectionState connection, Option<double> hold)
    {
        Connection = connection.Name;
        Name = connection.Name.Value;
        Provider = string.Empty;
        Account = string.Empty;
        Cost = string.Empty;
        Use = string.Empty;
        Update(connection, hold);
    }

    public ConnectionName Connection { get; }

    public string Name { get; }

    [ObservableProperty]
    public partial string Provider { get; private set; }

    [ObservableProperty]
    public partial string Account { get; private set; }

    [ObservableProperty]
    public partial bool IsDefault { get; private set; }

    [ObservableProperty]
    public partial string Cost { get; private set; }

    [ObservableProperty]
    public partial double Used { get; private set; }

    [ObservableProperty]
    public partial string Use { get; private set; }

    [ObservableProperty]
    public partial bool IsNearLimit { get; private set; }

    [ObservableProperty]
    public partial bool IsProminent { get; private set; }

    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    public IReadOnlyList<ILimitViewModel> Limits => limits;

    public IReadOnlyList<IAgentViewModel> Agents => agents;

    public void Update(ConnectionState connection, Option<double> hold)
    {
        Provider = connection.Provider;
        Account = connection.Account.Match(account => account.Label, () => "account not reported yet");
        IsDefault = connection.IsDefault;
        Cost = connection.Usage.Match(usage => Amounts.Costs(usage.Costs), () => "no usage yet");
        var reported = connection.Usage.Match<IReadOnlyList<Agents.Contracts.Events.UsageLimit>>(usage => usage.Limits, () => []);
        limits.ShowOnly(reported.Select(limit => new LimitViewModel(limit, hold)));
        var highest = reported.OrderByDescending(limit => limit.UsedFraction).Take(1).ToList();
        Used = highest.Sum(limit => limit.UsedFraction);
        Use = highest.Count == 0 ? "no limit" : $"{highest[0].Window} · {Amounts.Percent(Used)}";
        IsNearLimit = highest.Count > 0 && hold.Match(threshold => Used >= threshold - MeterViewModel.AttentionMargin, () => false);
        agents.Reconcile(connection.Agents, agent => agent.Job, job => job.Job, job => new AgentViewModel(job), (agent, job) => agent.Update(job));
        IsProminent = agents.Count >= 2;
        Summary = OverviewPhrases.Hub(Account, Cost, IsDefault);
    }
}

[INotifyPropertyChanged]
internal sealed partial class AgentViewModel : IAgentViewModel
{
    private readonly StatusDotViewModel dot = new(StatusKind.Working);

    public AgentViewModel(BoardJob job)
    {
        Job = job.Job;
        Title = FactPhrases.Title(job.Summary.Instruction);
        Fact = string.Empty;
        State = string.Empty;
        Update(job);
    }

    public JobId Job { get; }

    public string Title { get; }

    public IStatusDotViewModel Dot => dot;

    [ObservableProperty]
    public partial JobStatus Status { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string Fact { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    public partial string State { get; private set; }

    public string Summary => $"{State} — {Fact}";

    public void Update(BoardJob job)
    {
        Status = job.Status;
        Fact = FactPhrases.Of(job.Fact);
        dot.Kind = FactPhrases.Dot(job);
        State = OverviewPhrases.State(dot.Kind);
    }
}
