using Avala.Agents.Contracts.Events;

namespace Avala.ClaudeCode.Tests.Conversations;

public sealed class RetryTests
{
    [Fact]
    public void ARequestClaudeCodeRetriesIsReportedAsTheHarnessRetryingWithItsAttemptWaitAndReason()
    {
        var talk = new Talk().Begin();
        talk.Receive(Cli.Init());
        talk.Drain();

        talk.Receive(Cli.Parse("""{ "type": "system", "subtype": "api_retry", "attempt": 3, "max_retries": 10, "retry_delay_ms": 4000, "error_status": 529, "error": "overloaded" }"""));

        Assert.Equal(
            new HarnessRetrying(talk.Session, talk.Turn, 3, 10, TimeSpan.FromSeconds(4), "overloaded"),
            Assert.Single(talk.Events));
    }
}
