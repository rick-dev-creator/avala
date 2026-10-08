using Avala.Jobs.Domain;
using Avala.Sdk;

namespace Avala.Jobs.Tests.Domain;

internal static class Outcomes
{
    public static T Succeeds<T>(Result<T, JobError> result)
    {
        Assert.True(result.TryGetValue(out var value, out var error), $"Expected success, got {error}");

        return value;
    }

    public static JobError FailsWith<T>(Result<T, JobError> result)
    {
        Assert.False(result.TryGetValue(out _, out var error), "Expected a failure, got a success");

        return error;
    }

    public static JobError? ErrorOf<T>(Result<T, JobError> result) =>
        result.Match<JobError?>(_ => null, error => error);
}
