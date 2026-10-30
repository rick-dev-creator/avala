using System.Collections.ObjectModel;
using Avala.Agents.Contracts.Connections;
using Avala.Handoffs.Contracts;
using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Settings;

internal interface IUsageLimitsViewModel
{
    IReadOnlyList<string> Actions { get; }

    string Action { get; set; }

    bool HandsOff { get; }

    double Threshold { get; set; }

    double HardCap { get; set; }

    string ThresholdLabel { get; }

    string HardCapLabel { get; }

    IReadOnlyList<string> Strategies { get; }

    string Strategy { get; set; }

    string StrategyExplanation { get; }

    bool ResumesByItself { get; set; }

    bool WindsDown { get; set; }

    bool CompactsOnResume { get; set; }

    IReadOnlyList<ILimitConnectionViewModel> Connections { get; }

    string ConnectionsHint { get; }

    string Summary { get; }

    string File { get; }

    string Error { get; }

    bool IsChanged { get; }

    IAsyncRelayCommand SaveCommand { get; }
}

internal interface ILimitConnectionViewModel
{
    string Name { get; }

    bool IsIncluded { get; set; }

    string Position { get; }

    bool CanMoveUp { get; }

    IRelayCommand MoveUpCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class UsageLimitsViewModel(ILimitSettings limits, IConnections connections, IUiDispatcher ui) : IUsageLimitsViewModel, IActivatable
{
    private readonly ObservableCollection<LimitConnectionViewModel> rows = [];
    private string saved = string.Empty;
    private bool showing;

    public IReadOnlyList<string> Actions => LimitPhrases.Actions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HandsOff))]
    public partial string Action { get; set; } = LimitPhrases.Actions[0];

    public bool HandsOff => LimitPhrases.ActionOf(Action) != OnLimit.Hold;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThresholdLabel))]
    public partial double Threshold { get; set; } = 90;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HardCapLabel))]
    public partial double HardCap { get; set; } = 98;

    public string ThresholdLabel => LimitPhrases.Percent(Threshold);

    public string HardCapLabel => LimitPhrases.Percent(HardCap);

    public IReadOnlyList<string> Strategies => LimitPhrases.Strategies;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StrategyExplanation))]
    public partial string Strategy { get; set; } = LimitPhrases.Strategies[0];

    public string StrategyExplanation => LimitPhrases.Explain(LimitPhrases.StrategyOf(Strategy));

    [ObservableProperty]
    public partial bool ResumesByItself { get; set; }

    [ObservableProperty]
    public partial bool WindsDown { get; set; } = true;

    [ObservableProperty]
    public partial bool CompactsOnResume { get; set; } = true;

    public IReadOnlyList<ILimitConnectionViewModel> Connections => rows;

    [ObservableProperty]
    public partial string ConnectionsHint { get; private set; } = LimitPhrases.EveryConnection;

    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string File { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsChanged { get; private set; }

    public Task Loading { get; private set; } = Task.CompletedTask;

    public void Activate() => Loading = LoadAsync(CancellationToken.None);

    public void Deactivate()
    {
    }

    partial void OnActionChanged(string value) => Refresh();

    partial void OnThresholdChanged(double value)
    {
        if (HardCap < value)
        {
            HardCap = value;
        }

        Refresh();
    }

    partial void OnHardCapChanged(double value)
    {
        if (value < Threshold)
        {
            HardCap = Threshold;
            return;
        }

        Refresh();
    }

    partial void OnStrategyChanged(string value) => Refresh();

    partial void OnResumesByItselfChanged(bool value) => Refresh();

    partial void OnWindsDownChanged(bool value) => Refresh();

    partial void OnCompactsOnResumeChanged(bool value) => Refresh();

    [RelayCommand(CanExecute = nameof(IsChanged))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var changed = await limits.ChangeMachineAsync(Draft(), cancellationToken);
        Error = changed.Match(_ => string.Empty, LimitPhrases.Refused);

        if (changed.TryGetValue(out var file, out _))
        {
            Show(file, [.. rows.Select(row => row.Name)]);
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var file = await limits.MachineAsync(cancellationToken);
        var catalog = await connections.CatalogAsync(cancellationToken);

        await ui.InvokeAsync(() => Show(file, [.. catalog.Connections.Select(connection => connection.Name.Value)]), cancellationToken);
    }

    private void Show(LimitFile file, IReadOnlyList<string> known)
    {
        var settings = file.Settings;
        showing = true;
        Action = LimitPhrases.ActionName(settings.OnLimit);
        Threshold = Math.Round(settings.Threshold * 100);
        HardCap = Math.Max(Threshold, Math.Round(settings.HardCap * 100));
        Strategy = LimitPhrases.StrategyName(settings.Strategy);
        ResumesByItself = settings.Resume == ResumeAtReset.Auto;
        WindsDown = settings.WindDown;
        CompactsOnResume = settings.CompactOnResume;
        var included = settings.Connections.Select(connection => connection.Value).ToList();
        rows.Clear();

        foreach (var name in included.Concat(known.Where(name => !included.Contains(name, StringComparer.Ordinal))))
        {
            rows.Add(new LimitConnectionViewModel(name, included.Contains(name, StringComparer.Ordinal), MoveUp, Refresh));
        }

        File = LimitPhrases.File(file);
        showing = false;
        saved = LimitPhrases.Key(Draft());
        Refresh();
    }

    private void MoveUp(LimitConnectionViewModel row)
    {
        var at = rows.IndexOf(row);

        if (at > 0)
        {
            rows.Move(at, at - 1);
            Refresh();
        }
    }

    private LimitSettings Draft() =>
        new(LimitPhrases.ActionOf(Action), Threshold / 100, HardCap / 100, [.. rows.Where(row => row.IsIncluded).Select(row => new ConnectionName(row.Name))])
        {
            Strategy = LimitPhrases.StrategyOf(Strategy),
            Resume = ResumesByItself ? ResumeAtReset.Auto : ResumeAtReset.Ask,
            WindDown = WindsDown,
            CompactOnResume = CompactsOnResume,
        };

    private void Refresh()
    {
        if (showing)
        {
            return;
        }

        for (var at = 0; at < rows.Count; at++)
        {
            rows[at].Place(at);
        }

        var draft = Draft();
        ConnectionsHint = draft.Connections.Count == 0 ? LimitPhrases.EveryConnection : LimitPhrases.OnlyThese(draft.Connections.Count);
        Summary = LimitPhrases.Summary(draft);
        IsChanged = saved.Length > 0 && LimitPhrases.Key(draft) != saved;
    }
}

[INotifyPropertyChanged]
internal sealed partial class LimitConnectionViewModel(string name, bool included, Action<LimitConnectionViewModel> moveUp, Action changed) : ILimitConnectionViewModel
{
    public string Name { get; } = name;

    [ObservableProperty]
    public partial bool IsIncluded { get; set; } = included;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMoveUp))]
    public partial string Position { get; private set; } = string.Empty;

    public bool CanMoveUp => Position != "1";

    public void Place(int at) => Position = (at + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    partial void OnIsIncludedChanged(bool value) => changed();

    [RelayCommand]
    private void MoveUp() => moveUp(this);
}
