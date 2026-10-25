using Avala.Verification.Discovering;

namespace Avala.Verification.Tests.Discovering;

public sealed class ScoutOfferTests
{
    [Fact]
    public async Task OnlyTheScoutIsOfferedTheToolThatProposesChecksAndEveryOtherToolIsLeftToOthersAsync()
    {
        await using var scouted = Scouted.Fresh();
        var job = await scouted.RunningAsync();
        var offer = new ScoutOffer(scouted.Jobs, scouted.Jobs);
        var other = ScoutBrief.Tool with { Name = "canvas" };

        Assert.Equal(
            (true, false, true),
            (await offer.OffersAsync(ScoutBrief.Tool, Scouted.FakeJobs.Worktree(scouted.LastScout), Scouted.Cancellation),
                await offer.OffersAsync(ScoutBrief.Tool, Scouted.FakeJobs.Worktree(job), Scouted.Cancellation),
                await offer.OffersAsync(other, Scouted.FakeJobs.Worktree(job), Scouted.Cancellation)));
    }
}
