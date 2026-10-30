using Ardalis.Result;
using Avala.Server.Activity.Application.Usage;
using Avala.Server.Activity.Contracts.Usage;
using Avala.Server.CrossCuttingConcerns.Validation;

namespace Avala.Server.Activity.Tests.Application;

public sealed class ListUsageValidatorTests
{
    private readonly ListUsageValidator validator = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public static TheoryData<string?, string?, string?, string?, string?, string> Refused => new()
    {
        { "19/10/2026", "2026-10-26", "UTC", "day", null, "the field 'from' must be a date such as 2026-10-19" },
        { "2026-10-19", null, "UTC", "day", null, "the field 'to' must be a date such as 2026-10-19" },
        { "2026-10-19", "2026-10-26", "Mars/Olympus", "day", null, "the field 'zone' must be an IANA time zone such as Europe/Madrid" },
        { "2026-10-19", "2026-10-26", "UTC", "hour", null, "the field 'step' must be day, week or month" },
        { "2026-10-26", "2026-10-19", "UTC", "day", null, "the range must end after it starts" },
        { "2025-01-01", "2026-03-01", "UTC", "day", null, "the range is longer than 400 days" },
        { "2026-10-20", "2026-10-26", "UTC", "week", null, "a range by week must start and end on a Monday" },
        { "2026-10-19", "2026-10-26", "UTC", "day", "remote:rick@github.com/rick/avala", "the repository key carries credentials" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void AQueryThatCannotBeAnsweredIsInvalidWithItsReason(string? from, string? to, string? zone, string? step, string? repository, string reason) =>
        Assert.Equal([reason], validator.Validate(new ListUsage(from, to, zone, step, repository)).Errors.Select(error => error.ErrorMessage));

    [Fact]
    public void AWholeQueryIsValid() =>
        Assert.True(validator.Validate(new ListUsage("2026-10-19", "2026-10-26", "Europe/Madrid", "day", "remote:GitHub.com/Rick/Avala.git")).IsValid);

    [Fact]
    public async Task TheGoldenRequestIsValidAsync()
    {
        var fields = (await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "FleetWire", "v1", "requests", "usage.query"), Cancellation))
            .Split('&')
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]), StringComparer.Ordinal);

        Assert.True((await validator.ValidateAsync(new ListUsage(fields["from"], fields["to"], fields["zone"], fields["step"], fields["repository"]), Cancellation)).IsValid);
    }

    [Fact]
    public async Task AnInvalidQueryNeverReachesTheHandlerAsync()
    {
        var calls = 0;
        var behavior = new ValidationBehavior<ListUsage, Result<UsageReport>>([validator]);

        var result = await behavior.Handle(
            new ListUsage("2026-10-26", "2026-10-19", "UTC", "day", null),
            (_, _) =>
            {
                calls++;

                return ValueTask.FromResult(Result<UsageReport>.Error());
            },
            Cancellation);

        Assert.Equal((ResultStatus.Invalid, 0), (result.Status, calls));
    }
}
