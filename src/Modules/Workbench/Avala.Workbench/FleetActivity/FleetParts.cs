using System.Collections.ObjectModel;
using System.Globalization;
using Avala.Components.Privacy;
using Avala.Fleet.Contracts;
using Avala.Sdk;
using Avala.Workbench.Presenting;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.FleetActivity;

internal interface IFleetColumnViewModel
{
    double Share { get; }

    double Previous { get; }

    bool HasPrevious { get; }

    bool IsStacked { get; }

    double InputReach { get; }

    double OutputReach { get; }

    double ReasoningReach { get; }

    double CacheWriteReach { get; }

    string Tick { get; }

    string Tip { get; }
}

internal sealed record FleetColumnViewModel(FleetColumn Column, bool IsStacked) : IFleetColumnViewModel
{
    public double Share => Column.Share;

    public double Previous => Column.Previous.Match(previous => previous, () => 0);

    public bool HasPrevious => Column.Previous.IsSome;

    public double InputReach => Column.Stack.Input;

    public double OutputReach => Column.Stack.Output;

    public double ReasoningReach => Column.Stack.Reasoning;

    public double CacheWriteReach => Column.Stack.CacheWrite;

    public string Tick => Column.Tick;

    public string Tip => Column.Tip;
}

internal interface IFleetTrendViewModel
{
    bool ShowsSpend { get; }

    bool CanShowSpend { get; }

    string Axis { get; }

    bool IsEmpty { get; }

    string Previous { get; }

    bool HasPrevious { get; }

    IReadOnlyList<IFleetColumnViewModel> Columns { get; }

    IRelayCommand ShowSpendCommand { get; }

    IRelayCommand ShowTokensCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class FleetTrendViewModel : IFleetTrendViewModel
{
    private readonly ObservableCollection<IFleetColumnViewModel> columns = [];

    private FleetUsage usage = FleetUsage.Empty;

    private FleetComparison comparison = FleetComparison.None;

    private FleetStep step = FleetStep.Day;

    private bool tokensChosen;

    public event EventHandler? MetricChanged;

    public IReadOnlyList<IFleetColumnViewModel> Columns => columns;

    [ObservableProperty]
    public partial bool ShowsSpend { get; private set; }

    [ObservableProperty]
    public partial bool CanShowSpend { get; private set; }

    [ObservableProperty]
    public partial string Axis { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; } = true;

    [ObservableProperty]
    public partial string Previous { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasPrevious { get; private set; }

    public void Show(FleetUsage shown, FleetComparison compared, FleetStep stepped)
    {
        usage = shown;
        comparison = compared;
        step = stepped;
        CanShowSpend = shown.Reference.IsSome;
        ShowsSpend = CanShowSpend && !tokensChosen;
        HasPrevious = compared.Before.IsSome;
        Previous = HasPrevious ? compared.Previous : string.Empty;
        Draw();
    }

    [RelayCommand]
    private void ShowSpend()
    {
        tokensChosen = false;

        if (CanShowSpend && !ShowsSpend)
        {
            ShowsSpend = true;
            Draw();
            MetricChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [RelayCommand]
    private void ShowTokens()
    {
        tokensChosen = true;

        if (ShowsSpend)
        {
            ShowsSpend = false;
            Draw();
            MetricChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Draw()
    {
        var highest = FleetColumns.Highest(usage.Series, ShowsSpend, usage.Reference);
        IsEmpty = highest == 0;
        Axis = ShowsSpend
            ? usage.Reference.Match(reference => $"{FleetFigures.Money(highest, reference.Currency)} {FleetFigures.Basis(reference.Basis)}, the highest {Unit()}", () => string.Empty)
            : $"{FleetFigures.Tokens(highest)} tokens, the highest {Unit()}";
        columns.ShowOnly(FleetColumns.Of(usage.Series, step, ShowsSpend, usage.Reference, comparison.Series).Select(column => new FleetColumnViewModel(column, !ShowsSpend)));
    }

    private string Unit() => step switch
    {
        FleetStep.Week => "week",
        FleetStep.Month => "month",
        _ => "day",
    };
}

internal interface IFleetShareRowViewModel
{
    Personal Name { get; }

    double Share { get; }

    string Value { get; }

    string Detail { get; }

    bool CanChoose { get; }

    bool IsChosen { get; }

    IAsyncRelayCommand ChooseCommand { get; }
}

internal sealed record FleetShareRow(FleetShare Share, double Portion, string Value, string Detail, bool Chosen);

internal sealed partial class FleetShareRowViewModel(FleetShareRow row, Func<string, Task> choose) : IFleetShareRowViewModel
{
    public Personal Name { get; } = row.Share.Kind switch
    {
        FleetShareKind.Unknown => Personal.Plain("Repository not known yet"),
        FleetShareKind.OutsideAnyJob => Personal.Plain("Outside any job"),
        FleetShareKind.Model => Personal.Plain(row.Share.Key.Length > 0 ? row.Share.Key : "Model not reported"),
        FleetShareKind.Instance => Personal.AccountName(row.Share.Name),
        _ => Personal.AccountName(FleetFigures.Repository(row.Share.Key)),
    };

    public double Share => row.Portion;

    public string Value => row.Value;

    public string Detail => row.Detail;

    public bool CanChoose => row.Share.Kind == FleetShareKind.Repository;

    public bool IsChosen => row.Chosen;

    [RelayCommand]
    private Task ChooseAsync() => CanChoose ? choose(row.Share.Key) : Task.CompletedTask;
}

internal interface IFleetShareListViewModel
{
    string Title { get; }

    IReadOnlyList<IFleetShareRowViewModel> Rows { get; }

    string More { get; }

    bool HasMore { get; }
}

[INotifyPropertyChanged]
internal sealed partial class FleetShareListViewModel(string title) : IFleetShareListViewModel
{
    public const int Shown = 8;

    private readonly ObservableCollection<IFleetShareRowViewModel> rows = [];

    public string Title => title;

    public IReadOnlyList<IFleetShareRowViewModel> Rows => rows;

    [ObservableProperty]
    public partial string More { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasMore { get; private set; }

    public void Show(
        IReadOnlyList<FleetShare> shares,
        Option<IReadOnlyList<FleetShare>> before,
        bool spend,
        Option<FleetCostKind> reference,
        Option<string> chosen,
        Func<string, Task> choose)
    {
        decimal Measure(FleetShare share) => spend ? FleetFigures.Spent(share.Costs, reference) : FleetFigures.Total(share.Tokens);
        Option<decimal> Earlier(FleetShare share) =>
            before.Map(earlier => earlier.Where(other => other.Kind == share.Kind && other.Key == share.Key).Sum(Measure));

        var ranked = shares.OrderByDescending(Measure).ToList();
        var highest = ranked.Select(Measure).DefaultIfEmpty(0).Max();
        var total = ranked.Sum(Measure);
        rows.ShowOnly(ranked.Take(Shown).Select(share => new FleetShareRowViewModel(
            new FleetShareRow(
                share,
                highest == 0 ? 0 : (double)(Measure(share) / highest),
                spend ? reference.Match(kind => FleetFigures.Money(Measure(share), kind.Currency), () => string.Empty) : FleetFigures.Tokens(Measure(share)),
                string.Join(" · ", new[] { FleetFigures.Percent(Measure(share), total), FleetFigures.Shift(Measure(share), Earlier(share)) }.Where(part => part.Length > 0)),
                share.Kind == FleetShareKind.Repository && chosen.Match(key => key == share.Key, () => false)),
            choose)));
        HasMore = shares.Count > Shown;
        More = string.Create(CultureInfo.CurrentCulture, $"and {shares.Count - Shown} more");
    }
}

internal interface IFleetRepositoryJobsViewModel
{
    Personal Repository { get; }

    string Notice { get; }

    IReadOnlyList<IFleetJobRowViewModel> Jobs { get; }
}

[INotifyPropertyChanged]
internal sealed partial class FleetRepositoryJobsViewModel : IFleetRepositoryJobsViewModel
{
    private readonly ObservableCollection<IFleetJobRowViewModel> jobs = [];

    public IReadOnlyList<IFleetJobRowViewModel> Jobs => jobs;

    [ObservableProperty]
    public partial Personal Repository { get; private set; } = Personal.None;

    [ObservableProperty]
    public partial string Notice { get; private set; } = string.Empty;

    public void Show(string repository, FleetJobsView view)
    {
        Repository = Personal.AccountName(FleetFigures.Repository(repository));
        Notice = view.State == FleetReadState.Read
            ? view.Jobs.Count == 0 ? "No job of this repository yet." : "All its jobs, newest activity first, whatever the range."
            : FleetFigures.Said(view.State);
        jobs.ShowOnly(view.Jobs.OrderByDescending(job => job.LastActivityAt).Select(job => new FleetJobRowViewModel(job)));
    }
}

internal interface IFleetJobRowViewModel
{
    Personal Instance { get; }

    string State { get; }

    string Spending { get; }
}

internal sealed class FleetJobRowViewModel(FleetJob job) : IFleetJobRowViewModel
{
    public Personal Instance { get; } = Personal.AccountName(job.InstanceName);

    public string State { get; } = string.Join(
        " · ",
        new[]
        {
            job.Status,
            job.Hold.Length > 0 ? $"held: {job.Hold}" : string.Empty,
            job.Attempts > 0 ? $"{FleetFigures.Counted(job.Attempts, "attempt")}, last {job.LastOutcome}" : string.Empty,
            job.LastActivityAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
        }.Where(part => part.Length > 0));

    public string Spending { get; } = FleetFigures.Spending(job.Tokens, job.Costs);
}
