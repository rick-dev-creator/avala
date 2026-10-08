namespace Avala.Sdk.Tests;

public sealed class OptionTests
{
    [Fact]
    public void MatchHandsOverAPresentValue()
    {
        Option<string> present = "login";

        Assert.Equal("some login", present.Match(value => $"some {value}", () => "none"));
    }

    [Fact]
    public void MatchHandlesAnAbsentValue() =>
        Assert.Equal("none", Option<string>.None.Match(value => $"some {value}", () => "none"));

    [Fact]
    public void TheDefaultOptionIsAbsent() =>
        Assert.True(default(Option<int>).IsNone);

    [Fact]
    public void MapAndBindTransformOnlyPresentValues()
    {
        Option<int> present = 20;

        Assert.Equal(Option<int>.Some(42), present.Map(value => value * 2).Bind(value => Option<int>.Some(value + 2)));
        Assert.Equal(Option<int>.None, Option<int>.None.Map(value => value * 2).Bind(value => Option<int>.Some(value + 2)));
    }

    [Fact]
    public void ToResultTurnsAbsenceIntoATypedError()
    {
        Option<int> present = 7;

        Assert.Equal(7, present.ToResult(Missing.Value).Match(value => value, _ => 0));
        Assert.Equal(Missing.Value, Option<int>.None.ToResult(Missing.Value).Match(_ => Missing.Unknown, error => error));
    }

    [Fact]
    public void NullsFromOutsideBecomeAbsentOptions()
    {
        string? missingText = null;
        int? missingNumber = null;

        Assert.Equal(Option<string>.None, missingText.ToOption());
        Assert.Equal(Option<int>.None, missingNumber.ToOption());
        Assert.Equal(Option<string>.Some("login"), "login".ToOption());
        Assert.Equal(Option<int>.Some(3), ((int?)3).ToOption());
    }

    [Fact]
    public async Task MatchAsyncAwaitsTheBranchOfAPendingOptionAsync()
    {
        var pending = Task.FromResult(Option<string>.Some("login"));

        Assert.Equal("some login", await pending.MatchAsync(value => Task.FromResult($"some {value}"), () => Task.FromResult("none")));
    }

    [Fact]
    public void SomeRefusesNull() =>
        Assert.Throws<ArgumentNullException>(() => Option<string>.Some(null!));

    private enum Missing
    {
        Unknown,
        Value,
    }
}
