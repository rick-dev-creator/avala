using Ardalis.Result;
using Avala.Server.Activity.Application.RepositoryJobs;
using Avala.Server.Activity.Contracts.RepositoryJobs;
using Avala.Server.CrossCuttingConcerns.Validation;

namespace Avala.Server.Activity.Tests.Application;

public sealed class ListRepositoryJobsValidatorTests
{
    private readonly ListRepositoryJobsValidator validator = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void AMissingRepositoryKeyIsInvalid() =>
        Assert.Equal(["a repository key is required"], validator.Validate(new ListRepositoryJobs(null)).Errors.Select(error => error.ErrorMessage));

    [Fact]
    public void ARefusedRepositoryKeyIsInvalidWithItsReason() =>
        Assert.Equal(
            ["the repository key carries credentials"],
            validator.Validate(new ListRepositoryJobs("remote:rick@github.com/rick/avala")).Errors.Select(error => error.ErrorMessage));

    [Fact]
    public void AWellFormedKeyIsValid() =>
        Assert.True(validator.Validate(new ListRepositoryJobs("remote:GitHub.com/Rick/Avala.git")).IsValid);

    [Fact]
    public async Task ARefusedRepositoryKeyNeverReachesTheHandlerAsync()
    {
        var calls = 0;
        var behavior = new ValidationBehavior<ListRepositoryJobs, Result<IReadOnlyList<RepositoryJob>>>([validator]);

        var result = await behavior.Handle(
            new ListRepositoryJobs("remote:github.com:22/rick/avala"),
            (_, _) =>
            {
                calls++;

                return ValueTask.FromResult(Result<IReadOnlyList<RepositoryJob>>.Success([]));
            },
            Cancellation);

        Assert.Equal((ResultStatus.Invalid, 0), (result.Status, calls));
    }
}
