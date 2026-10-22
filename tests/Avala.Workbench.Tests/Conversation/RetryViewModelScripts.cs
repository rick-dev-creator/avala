using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Conversation;

public sealed class RetryViewModelScripts
{
    [Theory]
    [InlineData(3, 10, false, false, "The harness is retrying · attempt 3 of 10 (overloaded)", true)]
    [InlineData(1, 0, false, false, "The harness is retrying · attempt 1 (overloaded)", true)]
    [InlineData(2, 10, true, false, "The harness recovered after 2 retries (overloaded).", false)]
    [InlineData(10, 10, false, true, "The harness stopped retrying after 10 retries (overloaded).", false)]
    public void ARetrySaysHowOftenTheHarnessTriedWhyAndWhetherItRecovered(int attempt, int most, bool recovered, bool gaveUp, string note, bool live)
    {
        var retry = new RetryViewModel(new RetryEntry("retry", 1, most, TimeSpan.FromSeconds(1), "overloaded"));

        retry.Update(new RetryEntry("retry", attempt, most, TimeSpan.FromSeconds(1), "overloaded") { Recovered = recovered, GaveUp = gaveUp });

        Assert.Equal((note, live, gaveUp), (retry.Note, retry.IsLive, retry.GaveUp));
    }
}
