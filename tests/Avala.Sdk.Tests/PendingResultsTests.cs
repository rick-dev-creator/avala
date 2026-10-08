namespace Avala.Sdk.Tests;

public sealed class PendingResultsTests
{
    [Fact]
    public async Task ChainsSuccessesThroughSynchronousAndAsynchronousStepsAsync()
    {
        var result = await StartAsync(2)
            .BindAsync(value => Result<int, StepError>.Success(value * 10))
            .BindAsync(value => Task.FromResult(Result<int, StepError>.Success(value + 1)))
            .MapAsync(value => $"#{value}");

        Assert.Equal("#21", result.Match(value => value, error => error.ToString()));
    }

    [Fact]
    public async Task StopsAtTheFirstFailureAsync()
    {
        var later = 0;

        var result = await StartAsync(2)
            .BindAsync(_ => Result<int, StepError>.Failure(StepError.Rejected))
            .BindAsync(value =>
            {
                later++;
                return Task.FromResult(Result<int, StepError>.Success(value));
            })
            .MapAsync(value =>
            {
                later++;
                return value;
            });

        Assert.Equal(StepError.Rejected, result.Match(_ => StepError.Unknown, error => error));
        Assert.Equal(0, later);
    }

    private static Task<Result<int, StepError>> StartAsync(int value) =>
        Task.FromResult(Result<int, StepError>.Success(value));

    private enum StepError
    {
        Unknown,
        Rejected,
    }
}
