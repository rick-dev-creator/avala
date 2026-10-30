using Avala.Components.Privacy;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.FleetActivity;

internal sealed class DesignFleetActivityViewModel : IFleetActivityViewModel
{
    public IFleetRangeViewModel Range { get; init; } = new DesignFleetRangeViewModel();

    public string Notice { get; init; } = string.Empty;

    public bool HasNotice => Notice.Length > 0;

    public bool HasFigures { get; init; } = true;

    public string ZoneNote { get; init; } = string.Empty;

    public bool HasZoneNote => ZoneNote.Length > 0;

    public Personal Scope { get; init; } = Personal.None;

    public bool IsScoped => Scope.Value.Length > 0;

    public bool AreNamesShown { get; init; }

    public string NamesAction => AreNamesShown ? "Hide names" : "Show names";

    public IReadOnlyList<IFleetTileViewModel> Tiles { get; init; } =
    [
        new FleetTileViewModel("Spend", "12.40 USD", "at API prices", "▲ 18% vs the previous 7 days", [0.2, 0.45, 0.3, 0.7, 1, 0.55, 0.65]),
        new FleetTileViewModel("Tokens", "18.4M", "1.2M in · 310K out · 16.9M cache read", "▲ 9% vs the previous 7 days", [0.3, 0.5, 0.35, 0.8, 1, 0.5, 0.6]),
        new FleetTileViewModel("Jobs", "23", "in 3 repositories", "▲ 4% vs the previous 7 days", []),
        new FleetTileViewModel("Per job", "0.54 USD", "spend divided by the jobs", "▼ 6% vs the previous 7 days", []),
        new FleetTileViewModel("Cache hit", "92%", "of input read from cache", "▲ 3 pts vs the previous 7 days", []),
        new FleetTileViewModel("Instances", "3", "that sent usage", "no change vs the previous 7 days", []),
    ];

    public IFleetTrendViewModel Trend { get; init; } = new DesignFleetTrendViewModel();

    public IReadOnlyList<IFleetShareListViewModel> Lists { get; init; } =
    [
        new DesignFleetShareListViewModel
        {
            Title = "Repositories",
            Rows =
            [
                new DesignFleetShareRowViewModel { Name = Personal.AccountName("remote:github.com/rick/avala"), Share = 1, Value = "8.10 USD", Detail = "64% · ▲ 12%", CanChoose = true },
                new DesignFleetShareRowViewModel { Name = Personal.AccountName("remote:github.com/rick/shop"), Share = 0.4, Value = "3.20 USD", Detail = "25% · ▼ 4%", CanChoose = true },
                new DesignFleetShareRowViewModel { Name = Personal.Plain("Outside any job"), Share = 0.14, Value = "1.10 USD", Detail = "9% · new" },
            ],
        },
        new DesignFleetShareListViewModel
        {
            Title = "Instances",
            Rows =
            [
                new DesignFleetShareRowViewModel { Name = Personal.AccountName("laptop"), Share = 1, Value = "7.30 USD", Detail = "59% · ▲ 20%" },
                new DesignFleetShareRowViewModel { Name = Personal.AccountName("desktop"), Share = 0.7, Value = "5.10 USD", Detail = "41% · ▼ 8%" },
            ],
        },
        new DesignFleetShareListViewModel
        {
            Title = "Models",
            Rows =
            [
                new DesignFleetShareRowViewModel { Name = Personal.Plain("opus"), Share = 1, Value = "10.90 USD", Detail = "88% · ▲ 15%" },
                new DesignFleetShareRowViewModel { Name = Personal.Plain("sonnet"), Share = 0.14, Value = "1.50 USD", Detail = "12% · ▼ 30%" },
            ],
        },
    ];

    public IFleetRepositoryJobsViewModel Jobs { get; init; } = new DesignFleetRepositoryJobsViewModel();

    public IAsyncRelayCommand RefreshCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IAsyncRelayCommand ClearScopeCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);

    public IRelayCommand ToggleNamesCommand { get; init; } = new RelayCommand(() => { });
}

internal sealed class DesignFleetRangeViewModel : IFleetRangeViewModel
{
    public IReadOnlyList<IFleetRangeOptionViewModel> Options { get; init; } =
    [
        new DesignFleetRangeOptionViewModel { Label = "7 days", IsSelected = true },
        new DesignFleetRangeOptionViewModel { Label = "30 days" },
        new DesignFleetRangeOptionViewModel { Label = "This month" },
        new DesignFleetRangeOptionViewModel { Label = "3 months" },
        new DesignFleetRangeOptionViewModel { Label = "6 months" },
        new DesignFleetRangeOptionViewModel { Label = "12 months" },
        new DesignFleetRangeOptionViewModel { Label = "Custom" },
    ];

    public string Caption { get; init; } = "19 Oct 2026 – 25 Oct 2026";

    public bool IsCustomOpen { get; init; }

    public string CustomFrom { get; set; } = "2026-10-01";

    public string CustomTo { get; set; } = "2026-10-25";

    public string Refusal { get; init; } = string.Empty;

    public bool HasRefusal => Refusal.Length > 0;

    public IRelayCommand ApplyCustomCommand { get; init; } = new RelayCommand(() => { });
}

internal sealed class DesignFleetRangeOptionViewModel : IFleetRangeOptionViewModel
{
    public string Label { get; init; } = "7 days";

    public bool IsSelected { get; init; }

    public IRelayCommand ChooseCommand { get; init; } = new RelayCommand(() => { });
}

internal sealed class DesignFleetTrendViewModel : IFleetTrendViewModel
{
    public bool ShowsSpend { get; init; } = true;

    public bool CanShowSpend { get; init; } = true;

    public string Axis { get; init; } = "3.40 USD at API prices, the highest day";

    public bool IsEmpty { get; init; }

    public string Previous { get; init; } = "the previous 7 days";

    public bool HasPrevious => Previous.Length > 0;

    public IReadOnlyList<IFleetColumnViewModel> Columns { get; init; } =
    [
        .. new[] { 0.2, 0.45, 0.3, 0.7, 1, 0.55, 0.65 }.Select((share, index) => new FleetColumnViewModel(
            new FleetColumn(
                share,
                Math.Min(1, share * 0.85 + 0.05),
                FleetStack.Flat,
                $"{19 + index} Oct",
                $"Day {19 + index} Oct · {share * 3.4:0.00} USD at API prices"),
            IsStacked: false)),
    ];

    public IRelayCommand ShowSpendCommand { get; init; } = new RelayCommand(() => { });

    public IRelayCommand ShowTokensCommand { get; init; } = new RelayCommand(() => { });
}

internal sealed class DesignFleetShareListViewModel : IFleetShareListViewModel
{
    public string Title { get; init; } = "Repositories";

    public IReadOnlyList<IFleetShareRowViewModel> Rows { get; init; } = [new DesignFleetShareRowViewModel()];

    public string More { get; init; } = string.Empty;

    public bool HasMore => More.Length > 0;
}

internal sealed class DesignFleetShareRowViewModel : IFleetShareRowViewModel
{
    public Personal Name { get; init; } = Personal.AccountName("remote:github.com/rick/avala");

    public double Share { get; init; } = 1;

    public string Value { get; init; } = "8.10 USD";

    public string Detail { get; init; } = "64% · ▲ 12%";

    public bool CanChoose { get; init; }

    public bool IsChosen { get; init; }

    public IAsyncRelayCommand ChooseCommand { get; init; } = new AsyncRelayCommand(() => Task.CompletedTask);
}

internal sealed class DesignFleetRepositoryJobsViewModel : IFleetRepositoryJobsViewModel
{
    public Personal Repository { get; init; } = Personal.AccountName("remote:github.com/rick/avala");

    public string Notice { get; init; } = "All its jobs, newest activity first, whatever the range.";

    public IReadOnlyList<IFleetJobRowViewModel> Jobs { get; init; } = [new DesignFleetJobRowViewModel()];
}

internal sealed class DesignFleetJobRowViewModel : IFleetJobRowViewModel
{
    public Personal Instance { get; init; } = Personal.AccountName("laptop");

    public string State { get; init; } = "awaitingReview · 2 attempts, last passed · 25/10/2026 12:01";

    public string Spending { get; init; } = "1.2M tokens · 1.20 USD at API prices";
}

internal sealed class DesignFleetTileViewModel : IFleetTileViewModel
{
    public string Label { get; init; } = "Spend";

    public string Value { get; init; } = "12.40 USD";

    public string Caption { get; init; } = "at API prices";

    public string Change { get; init; } = "▲ 18% vs the previous 7 days";

    public bool HasChange => Change.Length > 0;

    public IReadOnlyList<double> Spark { get; init; } = [0.2, 0.45, 0.3, 0.7, 1, 0.55, 0.65];

    public bool HasSpark => Spark.Count > 0;
}

internal sealed class DesignFleetColumnViewModel : IFleetColumnViewModel
{
    public double Share { get; init; } = 0.6;

    public double Previous { get; init; } = 0.5;

    public bool HasPrevious { get; init; } = true;

    public bool IsStacked { get; init; } = true;

    public double InputReach { get; init; } = 0.05;

    public double OutputReach { get; init; } = 0.08;

    public double ReasoningReach { get; init; } = 0.1;

    public double CacheWriteReach { get; init; } = 0.15;

    public string Tick { get; init; } = "19 Oct";

    public string Tip { get; init; } = "Mon 19 Oct · 2.1M tokens · 2.04 USD at API prices";
}
