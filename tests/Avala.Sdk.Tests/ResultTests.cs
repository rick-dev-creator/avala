namespace Avala.Sdk.Tests;

public sealed class ResultTests
{
    [Fact]
    public void SuccessHandsOverItsValue()
    {
        Result<int, SampleError> result = 42;

        Assert.True(result.TryGetValue(out var value, out _));
        Assert.Equal(42, value);
    }

    [Fact]
    public void FailureHandsOverItsError()
    {
        Result<int, SampleError> result = SampleError.Second;

        Assert.False(result.TryGetValue(out _, out var error));
        Assert.Equal(SampleError.Second, error);
    }

    [Fact]
    public void MatchRunsTheSuccessBranchForASuccess()
    {
        Result<int, SampleError> result = 7;

        Assert.Equal("value 7", result.Match(value => $"value {value}", error => $"error {error}"));
    }

    [Fact]
    public void MatchRunsTheFailureBranchForAFailure()
    {
        Result<int, SampleError> result = SampleError.First;

        Assert.Equal("error First", result.Match(value => $"value {value}", error => $"error {error}"));
    }

    [Fact]
    public void MapTransformsASuccess()
    {
        Result<int, SampleError> result = 20;

        Assert.Equal(21, result.Map(value => value + 1).Match(value => value, _ => 0));
    }

    [Fact]
    public void MapKeepsAFailure()
    {
        Result<int, SampleError> result = SampleError.Second;

        Assert.Equal(SampleError.Second, result.Map(value => value + 1).Match(_ => SampleError.First, error => error));
    }

    [Fact]
    public void BindChainsASuccess()
    {
        Result<int, SampleError> result = 3;

        var chained = result.Bind(value => Result<string, SampleError>.Success($"#{value}"));

        Assert.Equal("#3", chained.Match(value => value, _ => string.Empty));
    }

    [Fact]
    public void BindShortCircuitsAFailure()
    {
        Result<int, SampleError> result = SampleError.First;
        var invoked = false;

        var chained = result.Bind(value =>
        {
            invoked = true;
            return Result<string, SampleError>.Success($"#{value}");
        });

        Assert.False(invoked);
        Assert.True(chained.IsFailure);
    }

    [Fact]
    public void MapErrorTranslatesAFailureAcrossABoundary()
    {
        Result<int, SampleError> result = SampleError.Second;

        var translated = result.MapError(_ => BoundaryError.Rejected);

        Assert.Equal(BoundaryError.Rejected, translated.Match(_ => BoundaryError.Unknown, error => error));
    }

    [Fact]
    public void MapErrorKeepsASuccess()
    {
        Result<int, SampleError> result = 5;

        Assert.Equal(5, result.MapError(_ => BoundaryError.Rejected).Match(value => value, _ => 0));
    }

    [Fact]
    public void SuccessRefusesANullValue() =>
        Assert.Throws<ArgumentNullException>(() => Result<string, SampleError>.Success(null!));

    private enum SampleError
    {
        First,
        Second,
    }

    private enum BoundaryError
    {
        Unknown,
        Rejected,
    }
}
