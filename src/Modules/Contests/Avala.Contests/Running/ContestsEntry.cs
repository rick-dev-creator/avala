using Avala.Contests.Contesting;
using Avala.Contests.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Contests.Running;

internal sealed class ContestsEntry(ContestBook book, ContestStarter starter, IBestOfFile file) : IContests
{
    public async ValueTask<Result<ContestRecord, ContestRejection>> StartAsync(ContestRequest request, CancellationToken cancellationToken)
    {
        var declared = await file.OfRepositoryAsync(request.Job.RepositoryPath, cancellationToken);
        var sized = declared.Bind(bestOf => Contest.Sized(request.Candidates, bestOf.Max)
            .MapError(error => error == ContestError.TooFewCandidates ? ContestRejection.TooFewCandidates : ContestRejection.TooManyCandidates));

        return await sized.Match(
            candidates => starter.StartAsync(request, declared.Match(bestOf => bestOf.Max, _ => BestOf.Default.Max), cancellationToken),
            rejection => Task.FromResult(Result<ContestRecord, ContestRejection>.Failure(rejection)));
    }

    public async ValueTask<Result<BestOf, ContestRejection>> BestOfAsync(string repository, CancellationToken cancellationToken) =>
        await file.OfRepositoryAsync(repository, cancellationToken);

    public Option<ContestRecord> Of(ContestId contest) => book.Of(contest);

    public Option<ContestRecord> OfJob(JobId job) => book.OfJob(job);

    public IReadOnlyList<ContestRecord> All() => book.All();
}
