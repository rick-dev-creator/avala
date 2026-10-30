using System.Globalization;
using Avala.Server.Activity.Contracts.Usage;

namespace Avala.Server.Activity.EndPoints.Usage;

internal static class ListUsageMapper
{
    public static ListUsage ToQuery(ListUsageRequest request) => new(request.From, request.To, request.Zone, request.Step, request.Repository);

    public static ListUsageResponse ToResponse(UsageReport report) =>
        new(
            Date(report.From),
            Date(report.To),
            report.Zone,
            report.Step,
            report.Reference is { } reference ? new CostKindResponse(reference.Currency, Spending.Name(reference.Basis)) : null,
            new UsageTotalsResponse(Spending.Tokens(report.Totals.Tokens), Spending.Costs(report.Totals.Costs), report.Totals.Jobs, report.Totals.Instances),
            [.. report.Series.Select(period => new UsagePeriodResponse(Date(period.Start), Spending.Tokens(period.Tokens), Spending.Costs(period.Costs)))],
            [.. report.Repositories.Select(share => new UsageRepositoryResponse(share.Key, Spending.Name(share.Kind), Spending.Tokens(share.Tokens), Spending.Costs(share.Costs), share.Jobs))],
            [.. report.Instances.Select(share => new UsageInstanceResponse(share.Key, share.Name, Spending.Tokens(share.Tokens), Spending.Costs(share.Costs), share.Jobs))],
            [.. report.Models.Select(share => new UsageModelResponse(share.Key, Spending.Tokens(share.Tokens), Spending.Costs(share.Costs)))]);

    private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
