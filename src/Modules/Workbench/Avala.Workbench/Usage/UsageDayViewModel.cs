using System.Globalization;
using Avala.Observability.Contracts;
using Avala.Workbench.Presenting;
using Avala.Workbench.Spending;

namespace Avala.Workbench.Usage;

internal interface IUsageDayViewModel
{
    string Day { get; }

    string Tokens { get; }

    string Cost { get; }

    string Types { get; }

    double Share { get; }

    bool IsToday { get; }
}

internal sealed class UsageDayViewModel(UsagePeriod day, long busiest, bool isToday) : IUsageDayViewModel
{
    public string Day { get; } = UsageRangeViewModel.Day(DateOnly.FromDateTime(day.From.DateTime)) + (isToday ? " · today" : string.Empty);

    public string Tokens { get; } = Amounts.Tokens(day.Usage.Tokens.Total());

    public string Cost { get; } = Amounts.Costs(day.Usage.Costs);

    public string Types { get; } = string.Create(
        CultureInfo.InvariantCulture,
        $"input {day.Usage.Tokens.Input:N0} · output {day.Usage.Tokens.Output:N0} · cache read {day.Usage.Tokens.CacheRead:N0} · cache write {day.Usage.Tokens.CacheWrite:N0} · reasoning {day.Usage.Tokens.Reasoning:N0}");

    public double Share { get; } = busiest == 0 ? 0 : (double)day.Usage.Tokens.Total() / busiest;

    public bool IsToday { get; } = isToday;
}
