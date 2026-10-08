using Avala.Sdk;
using Xunit;

namespace Avala.Testing;

public static class Outcomes
{
    public static T Succeeds<T, TError>(Result<T, TError> result)
        where TError : struct, Enum
    {
        Assert.True(result.TryGetValue(out var value, out var error), $"Expected success, got {error}");

        return value;
    }

    public static TError FailsWith<T, TError>(Result<T, TError> result)
        where TError : struct, Enum
    {
        Assert.False(result.TryGetValue(out _, out var error), "Expected a failure, got a success");

        return error;
    }

    public static T Present<T>(Option<T> option)
        where T : notnull
    {
        Assert.True(option.IsSome, "Expected a value, got none");

        return option.Match(value => value, () => default!);
    }

    public static TError? ErrorOf<T, TError>(Result<T, TError> result)
        where TError : struct, Enum =>
        result.Match<TError?>(_ => null, error => error);
}
