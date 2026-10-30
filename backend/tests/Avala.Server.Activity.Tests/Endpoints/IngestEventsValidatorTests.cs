using Avala.Server.Activity.EndPoints.Ingestion;
using static Avala.Server.Activity.Tests.Endpoints.Batches;

namespace Avala.Server.Activity.Tests.Endpoints;

public sealed class IngestEventsValidatorTests
{
    private readonly IngestEventsValidator validator = new();

    [Fact]
    public void AMissingBatchIsRefused() =>
        Assert.Equal(["the batch has no events"], validator.Validate(new IngestEventsRequest(null)).Errors.Select(error => error.ErrorMessage));

    [Fact]
    public void AnEmptyBatchIsRefused() =>
        Assert.False(validator.Validate(Request()).IsValid);

    [Fact]
    public void MoreThanFiveHundredEventsAreRefused() =>
        Assert.False(validator.Validate(Request([.. Enumerable.Range(1, 501).Select(number => Progressed(number))])).IsValid);
}
