using Avala.Agents.Contracts.Events;
using Avala.Contests.Contracts;
using Avala.Workbench.NewJob;

namespace Avala.Workbench.Tests.NewJob;

public sealed class JobOptionsViewModelScripts
{
    private readonly FakeContests contests = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ARepositoryWithoutABestOfSectionStartsWithOneCandidateAndOffersUpToFourAsync()
    {
        var options = Launching.Options(contests);

        await options.ReadAsync("/repositories/shop", Cancellation);

        Assert.Equal((1, "1 candidate", true, false), (options.Candidates, options.CandidatesText, options.OffersContest, options.IsContest));
        Assert.Equal((true, false), (options.MoreCommand.CanExecute(null), options.FewerCommand.CanExecute(null)));
        Assert.Equal("Up to 4 candidates; the best verified run is kept and the others are discarded once you approve it.", options.Note);
    }

    [Fact]
    public async Task TheStepperStartsAtTheRepositorysCandidatesAndStaysWithinItsMostAsync()
    {
        contests.Declared = new BestOf(2, 3);
        var options = Launching.Options(contests);
        await options.ReadAsync("/repositories/shop", Cancellation);

        options.MoreCommand.Execute(null);

        Assert.Equal((3, false, true), (options.Candidates, options.MoreCommand.CanExecute(null), options.FewerCommand.CanExecute(null)));
    }

    [Fact]
    public async Task EachCandidatesShareOfAJobsBudgetFollowsTheNumberOfCandidatesAsync()
    {
        contests.Declared = new BestOf(2, 4);
        var options = Launching.Options(contests, new Cost(5.00m, "USD"));
        await options.ReadAsync("/repositories/shop", Cancellation);
        var half = options.Share;

        options.MoreCommand.Execute(null);

        Assert.Equal("Each candidate may spend half of a job's budget: 2.50 USD.", half);
        Assert.Equal("Each candidate may spend a third of a job's budget: 1.67 USD.", options.Share);
        options.FewerCommand.Execute(null);
        options.FewerCommand.Execute(null);
        Assert.Equal(string.Empty, options.Share);
    }

    [Fact]
    public async Task ARepositoryThatCapsNoJobSaysTheCandidatesSpendWithoutAShareAsync()
    {
        contests.Declared = new BestOf(3, 4);
        var options = Launching.Options(contests);

        await options.ReadAsync("/repositories/shop", Cancellation);

        Assert.Equal("The repository caps no job, so the candidates spend without a share.", options.Share);
    }

    [Fact]
    public async Task ARejectedBestOfSectionOffersOneCandidateAndSaysWhyAsync()
    {
        contests.Declared = ContestRejection.InvalidMax;
        var options = Launching.Options(contests);

        await options.ReadAsync("/repositories/shop", Cancellation);

        Assert.Equal((1, false), (options.Candidates, options.OffersContest));
        Assert.Equal("The repository's .avala/jobs.json bestOf section is rejected: max must be a whole number from 1 to 8. One candidate per job.", options.Note);
    }
}
